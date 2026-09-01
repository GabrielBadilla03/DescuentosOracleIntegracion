using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SolicitudesDescuentos.Data;
using SolicitudesDescuentos.ModelsOracle;

namespace SolicitudesDescuentos.Services;

public class DescuentosBatchService : IDescuentosBatchService
{
    private const string BuImpulsadores = "LANCO_CR";
    private const string CiaLanco = "001";
    private const int OracleInChunkSize = 900;

    private static readonly SemaphoreSlim _executionGate = new(1, 1);

    private readonly OracleContext _db;
    private readonly LancoDbContext _lancoDb;
    private readonly ILogger<DescuentosBatchService> _logger;
    private readonly DescuentosWorkerOptions _options;
    private readonly IArchivosDescuentosService _archivosService;
    private readonly DescuentosSftpOptions _sftpOptions;

    private static string? _cachedFingerprint;
    private static readonly object _fingerprintLock = new();

    public DescuentosBatchService(
        OracleContext db,
        LancoDbContext lancoDb,
        ILogger<DescuentosBatchService> logger,
        IOptions<DescuentosWorkerOptions> options,
        IOptions<DescuentosSftpOptions> sftpOptions,
        IArchivosDescuentosService archivosService)
    {
        _db = db;
        _lancoDb = lancoDb;
        _logger = logger;
        _options = options.Value;
        _sftpOptions = sftpOptions.Value;
        _archivosService = archivosService;
    }

    public async Task ProcesarPendientesAsync(
        CancellationToken cancellationToken = default)
    {
        /*
         * Protección adicional dentro del proceso. El HostedService tiene un
         * lock entre procesos IIS; este gate evita además una segunda llamada
         * concurrente al servicio desde el mismo proceso.
         */
        var acquired = await _executionGate.WaitAsync(0, cancellationToken);

        if (!acquired)
        {
            _logger.LogInformation(
                "Se omitió la ejecución de ProcesarPendientesAsync porque " +
                "ya existe otra ejecución activa dentro del proceso.");

            return;
        }

        try
        {
            await EjecutarLimpiezaImpulsadoresAsync(cancellationToken);

            /*
             * Se normalizan GENERADO y ESTADO para evitar que espacios o
             * diferencias de mayúsculas/minúsculas dejen solicitudes sin
             * procesar. No se cambia ningún valor almacenado.
             */
            var headers = await _db.PREDESCUENTOs
                .AsNoTracking()
                .Where(p =>
                    p.GENERADO != null &&
                    p.ESTADO != null &&
                    p.GENERADO.Trim().ToUpper() == "N" &&
                    (
                        p.ESTADO.Trim().ToUpper() == "APROBADO" ||
                        p.ESTADO.Trim().ToUpper() == "REVERSADO"
                    ))
                .ToListAsync(cancellationToken);

            headers = OrdenarSolicitudesPorConsecutivo(headers).ToList();

            if (headers.Count > 0)
            {
                /*
                 * Los tipos que no reconoce NormalizeTipo no se silencian.
                 * Permanecen pendientes, pero quedan registrados claramente
                 * en logs para poder corregir el dato sin alterar la lógica.
                 */
                var tipoDesconocido = headers
                    .Where(x => string.IsNullOrWhiteSpace(
                        ArchivosDescuentosService.NormalizeTipo(x.TIPODESCUENTO)))
                    .ToList();

                foreach (var solicitud in tipoDesconocido)
                {
                    _logger.LogWarning(
                        "Solicitud pendiente con TIPODESCUENTO no reconocido. " +
                        "BU={BU}, Consecutivo={Consecutivo}, Estado={Estado}, Tipo={Tipo}. " +
                        "La solicitud no será procesada en este ciclo.",
                        solicitud.BU_NOMBRE,
                        solicitud.CONSECUTIVO,
                        solicitud.ESTADO,
                        solicitud.TIPODESCUENTO);
                }

                var aprobados = headers
                    .Where(x => Eq(x.ESTADO, "Aprobado"))
                    .ToList();

                var reversados = headers
                    .Where(x => Eq(x.ESTADO, "Reversado"))
                    .ToList();

                if (aprobados.Count > 0)
                {
                    await ProcesarGrupoAsync(
                        aprobados
                            .Where(x => ArchivosDescuentosService.NormalizeTipo(x.TIPODESCUENTO) == "promocional")
                            .ToList(),
                        "promocional",
                        forzarVencimientoDiaAnterior: false,
                        cancellationToken);

                    await ProcesarGrupoAsync(
                        aprobados
                            .Where(x => ArchivosDescuentosService.NormalizeTipo(x.TIPODESCUENTO) == "fijo")
                            .ToList(),
                        "fijo",
                        forzarVencimientoDiaAnterior: false,
                        cancellationToken);
                }

                if (reversados.Count > 0)
                {
                    await ProcesarGrupoAsync(
                        reversados
                            .Where(x => ArchivosDescuentosService.NormalizeTipo(x.TIPODESCUENTO) == "promocional")
                            .ToList(),
                        "promocional",
                        forzarVencimientoDiaAnterior: true,
                        cancellationToken);

                    await ProcesarGrupoAsync(
                        reversados
                            .Where(x => ArchivosDescuentosService.NormalizeTipo(x.TIPODESCUENTO) == "fijo")
                            .ToList(),
                        "fijo",
                        forzarVencimientoDiaAnterior: true,
                        cancellationToken);
                }
            }

            await ProcesarArticulosNoPromoAsync(cancellationToken);
        }
        finally
        {
            _executionGate.Release();
        }
    }

    private async Task EjecutarLimpiezaImpulsadoresAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await EliminarImpulsadoresInactivosAsync(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Si el SaveChanges de la limpieza falló, no se deben arrastrar
            // entidades Deleted al procesamiento posterior de descuentos.
            _db.ChangeTracker.Clear();

            _logger.LogError(
                ex,
                "Falló la limpieza de IMPULSADORESORACLE según el estado de PLAEMPLEADO. " +
                "El procesamiento de descuentos continuará.");
        }
    }

    private async Task EliminarImpulsadoresInactivosAsync(
        CancellationToken cancellationToken)
    {
        /*
         * PLAEMPLEADO se encuentra en LancoDbContext/NUEVO y
         * IMPULSADORESORACLE en OracleContext/BG_INTUSER.
         *
         * Al ser dos DbContext diferentes, primero se cargan únicamente los
         * códigos de empleados inactivos. Luego se consulta IMPULSADORESORACLE
         * por bloques de esos códigos, evitando cargar todas las asignaciones
         * de la BU en memoria cada dos minutos.
         */
        var empleadosInactivosDb = await _lancoDb.PLAEMPLEADOs
            .AsNoTracking()
            .Where(x =>
                x.CIA != null &&
                x.EMPLEADO != null &&
                x.ESTADO != null &&
                x.CIA.Trim() == CiaLanco &&
                x.ESTADO.Trim().ToUpper() == "I")
            .Select(x => x.EMPLEADO)
            .ToListAsync(cancellationToken);

        var empleadosInactivos = empleadosInactivosDb
            .Select(N)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (empleadosInactivos.Count == 0)
        {
            _logger.LogInformation(
                "Limpieza de impulsadores finalizada: no hay empleados inactivos " +
                "en PLAEMPLEADO para CIA={Cia}.",
                CiaLanco);

            return;
        }

        var empleadosEliminados = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var cantidadAsignacionesEliminadas = 0;

        foreach (var chunk in Chunk(empleadosInactivos, OracleInChunkSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var empleadosChunk = chunk
                .Select(N)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (empleadosChunk.Count == 0)
                continue;

            var asignaciones = await _db.IMPULSADORESORACLEs
                .Where(x =>
                    x.BU_NOMBRE != null &&
                    x.EMPLEADO != null &&
                    x.BU_NOMBRE.Trim().ToUpper() == BuImpulsadores &&
                    empleadosChunk.Contains(x.EMPLEADO.Trim().ToUpper()))
                .ToListAsync(cancellationToken);

            if (asignaciones.Count == 0)
                continue;

            foreach (var asignacion in asignaciones)
            {
                var empleado = T(asignacion.EMPLEADO);

                if (!string.IsNullOrWhiteSpace(empleado))
                    empleadosEliminados.Add(empleado);
            }

            cantidadAsignacionesEliminadas += asignaciones.Count;
            _db.IMPULSADORESORACLEs.RemoveRange(asignaciones);
        }

        if (cantidadAsignacionesEliminadas == 0)
        {
            _logger.LogInformation(
                "Limpieza de impulsadores finalizada: ningún empleado inactivo " +
                "está asignado en IMPULSADORESORACLE para BU={Bu}.",
                BuImpulsadores);

            return;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var empleadosOrdenados = empleadosEliminados
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation(
            "Limpieza de IMPULSADORESORACLE completada. " +
            "Asignaciones eliminadas={CantidadAsignaciones}, " +
            "empleados inactivos afectados={CantidadEmpleados}, " +
            "BU={Bu}, CIA={Cia}. Empleados={Empleados}",
            cantidadAsignacionesEliminadas,
            empleadosOrdenados.Count,
            BuImpulsadores,
            CiaLanco,
            string.Join(", ", empleadosOrdenados));
    }

    private async Task ProcesarArticulosNoPromoAsync(
        CancellationToken cancellationToken)
    {
        var articulos = await _db.ART_NO_PROMOs
            .AsNoTracking()
            .Where(a =>
                a.ESTADO != null &&
                a.GENERADO != null &&
                a.GENERADO.Trim().ToUpper() == "N" &&
                (
                    a.ESTADO.Trim().ToUpper() == "ACTIVO" ||
                    a.ESTADO.Trim().ToUpper() == "INACTIVO" ||
                    a.ESTADO.Trim().ToUpper() == "NUEVO"
                ))
            .OrderBy(a => a.ITEM_NUMBER)
            .ToListAsync(cancellationToken);

        if (articulos.Count == 0)
            return;

        foreach (var art in articulos)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await _archivosService.GenerarNoPromoPendienteAsync(
                    bu: T(art.BU_NAME),
                    org: T(art.ORGANIZATION_CODE),
                    itemNumber: T(art.ITEM_NUMBER),
                    marcarComoGenerado: false,
                    ct: cancellationToken);

                if (!result.Ok ||
                    result.ArchivoBytes == null ||
                    result.ArchivoBytes.Length == 0)
                {
                    _logger.LogWarning(
                        "No se generó ZIP ART_NO_PROMO para BU={BU}, ORG={Org}, ITEM={Item}. Motivo: {Motivo}",
                        art.BU_NAME,
                        art.ORGANIZATION_CODE,
                        art.ITEM_NUMBER,
                        result.Mensaje);

                    continue;
                }

                Directory.CreateDirectory(_options.OutputFolder);

                // Se conserva exactamente la nomenclatura vigente.
                var accion = Eq(art.ESTADO, "NUEVO")
                    ? "NUEVO"
                    : Eq(art.ESTADO, "INACTIVO")
                        ? "REACTIVAR"
                        : "DESACTIVAR";

                var fechaNombreArchivo = DateTime.Today.ToString("ddMMyy");

                var fileName = string.IsNullOrWhiteSpace(result.NombreArchivo)
                    ? $"Descuentos_COSTARICA_ALL_{accion}_{T(art.ITEM_NUMBER)}_{fechaNombreArchivo}.zip"
                    : result.NombreArchivo.Trim();

                var fullPath = Path.Combine(_options.OutputFolder, fileName);

                await File.WriteAllBytesAsync(
                    fullPath,
                    result.ArchivoBytes,
                    cancellationToken);

                var huella = ObtenerHuellaSftp();

                var sftp = new SftpService(
                    host: _sftpOptions.Host,
                    port: _sftpOptions.Port,
                    user: _sftpOptions.User,
                    privateKeyPath: _sftpOptions.PrivateKeyPath,
                    sshHostKeyFingerprint: huella,
                    privateKeyPassphrase: _sftpOptions.PrivateKeyPassphrase,
                    ignorarSeguridad: _sftpOptions.IgnorarSeguridad,
                    autoDiscoverFingerprintIfMissing: false
                );

                SftpUploadResult uploadResult;

                if (!string.IsNullOrWhiteSpace(result.NombreArchivo))
                {
                    uploadResult = sftp.UploadFileIdempotent(
                        localFullPath: fullPath,
                        remoteDir: _sftpOptions.RemoteDirPending,
                        remoteFileName: fileName,
                        overwrite: true
                    );
                }
                else
                {
                    // Fallback: conserva exactamente el comportamiento previo.
                    sftp.UploadFile(
                        localFullPath: fullPath,
                        remoteDir: _sftpOptions.RemoteDirPending,
                        remoteFileName: fileName,
                        overwrite: true
                    );

                    uploadResult = SftpUploadResult.Subido;
                }

                if (uploadResult == SftpUploadResult.YaExistiaMismoTamano)
                {
                    _logger.LogWarning(
                        "El ZIP ART_NO_PROMO ya existía en SFTP con el mismo tamaño. " +
                        "No se retransmitió y se continuará confirmando GENERADO='S'. " +
                        "BU={BU}, ORG={Org}, ITEM={Item}, Archivo={Archivo}.",
                        art.BU_NAME,
                        art.ORGANIZATION_CODE,
                        art.ITEM_NUMBER,
                        fileName);
                }

                var entity = await _db.ART_NO_PROMOs.FirstOrDefaultAsync(
                    a => a.BU_NAME == art.BU_NAME &&
                         a.ORGANIZATION_CODE == art.ORGANIZATION_CODE &&
                         a.ITEM_NUMBER == art.ITEM_NUMBER,
                    cancellationToken);

                if (entity != null)
                    entity.GENERADO = "S";

                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "ZIP ART_NO_PROMO generado y subido por SFTP: Local={Local}, Remoto={RemoteDir}/{FileName}, BU={BU}, ORG={Org}, ITEM={Item}.",
                    fullPath,
                    _sftpOptions.RemoteDirPending,
                    fileName,
                    art.BU_NAME,
                    art.ORGANIZATION_CODE,
                    art.ITEM_NUMBER);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                /*
                 * Limpia estados trackeados que hayan quedado pendientes por
                 * una excepción de SaveChanges y evita que contaminen el
                 * procesamiento del siguiente artículo.
                 */
                _db.ChangeTracker.Clear();

                /*
                 * Un artículo fallido no detiene el resto de ART_NO_PROMO.
                 * GENERADO permanece N y podrá reintentarse en otro ciclo.
                 */
                _logger.LogError(
                    ex,
                    "Error procesando ART_NO_PROMO. El job continuará con el siguiente artículo. " +
                    "BU={BU}, ORG={Org}, ITEM={Item}, Estado={Estado}.",
                    art.BU_NAME,
                    art.ORGANIZATION_CODE,
                    art.ITEM_NUMBER,
                    art.ESTADO);
            }
        }
    }

    private async Task ProcesarGrupoAsync(
        List<PREDESCUENTO> grupo,
        string tipoFiltro,
        bool forzarVencimientoDiaAnterior,
        CancellationToken cancellationToken)
    {
        if (grupo.Count == 0)
            return;

        foreach (var solicitud in OrdenarSolicitudesPorConsecutivo(grupo))
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pares = new List<(string CodCia, string Consecutivo)>
                {
                    (T(solicitud.BU_NOMBRE), T(solicitud.CONSECUTIVO))
                };

                var result = await _archivosService.DescargarExcelDesdeParesAsync(
                    pares,
                    tipoFiltro,
                    marcarComoGenerado: false,
                    forzarVencimientoDiaAnterior: forzarVencimientoDiaAnterior,
                    ct: cancellationToken);

                if (!result.Ok ||
                    result.ArchivoBytes == null ||
                    result.ArchivoBytes.Length == 0)
                {
                    _logger.LogWarning(
                        "No se generó ZIP para consecutivo {Consecutivo}, tipo {Tipo}, reversa={Reversa}. Motivo: {Motivo}",
                        solicitud.CONSECUTIVO,
                        tipoFiltro,
                        forzarVencimientoDiaAnterior,
                        result.Mensaje);

                    continue;
                }

                Directory.CreateDirectory(_options.OutputFolder);

                // Se conserva exactamente la nomenclatura vigente.
                var fileName = string.IsNullOrWhiteSpace(result.NombreArchivo)
                    ? "Descuentos_COSTARICA_ALL.zip"
                    : result.NombreArchivo.Trim();

                var fullPath = Path.Combine(_options.OutputFolder, fileName);

                await File.WriteAllBytesAsync(
                    fullPath,
                    result.ArchivoBytes,
                    cancellationToken);

                var huella = ObtenerHuellaSftp();

                var sftp = new SftpService(
                    host: _sftpOptions.Host,
                    port: _sftpOptions.Port,
                    user: _sftpOptions.User,
                    privateKeyPath: _sftpOptions.PrivateKeyPath,
                    sshHostKeyFingerprint: huella,
                    privateKeyPassphrase: _sftpOptions.PrivateKeyPassphrase,
                    ignorarSeguridad: _sftpOptions.IgnorarSeguridad,
                    autoDiscoverFingerprintIfMissing: false
                );

                SftpUploadResult uploadResult;

                if (!string.IsNullOrWhiteSpace(result.NombreArchivo))
                {
                    // El nombre generado por el servicio identifica la solicitud
                    // y permite un reintento idempotente seguro.
                    uploadResult = sftp.UploadFileIdempotent(
                        localFullPath: fullPath,
                        remoteDir: _sftpOptions.RemoteDirPending,
                        remoteFileName: fileName,
                        overwrite: true
                    );
                }
                else
                {
                    /*
                     * Si por alguna razón se utilizó el fallback genérico
                     * Descuentos_COSTARICA_ALL.zip, se conserva el comportamiento
                     * original de subirlo siempre para no confundir dos solicitudes
                     * diferentes que compartan ese nombre de contingencia.
                     */
                    sftp.UploadFile(
                        localFullPath: fullPath,
                        remoteDir: _sftpOptions.RemoteDirPending,
                        remoteFileName: fileName,
                        overwrite: true
                    );

                    uploadResult = SftpUploadResult.Subido;
                }

                if (uploadResult == SftpUploadResult.YaExistiaMismoTamano)
                {
                    _logger.LogWarning(
                        "El ZIP ya existía en SFTP con el mismo tamaño. " +
                        "No se retransmitió y se continuará confirmando GENERADO='S'. " +
                        "Consecutivo={Consecutivo}, Tipo={Tipo}, Reversa={Reversa}, Archivo={Archivo}.",
                        solicitud.CONSECUTIVO,
                        tipoFiltro,
                        forzarVencimientoDiaAnterior,
                        fileName);
                }

                var entity = await _db.PREDESCUENTOs.FirstOrDefaultAsync(
                    p => p.BU_NOMBRE == solicitud.BU_NOMBRE &&
                         p.CONSECUTIVO == solicitud.CONSECUTIVO,
                    cancellationToken);

                if (entity != null)
                    entity.GENERADO = "S";

                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "ZIP generado y subido por SFTP: Local={Local}, Remoto={RemoteDir}/{FileName}, Tipo={Tipo}, Reversa={Reversa}, Consecutivo={Consecutivo}.",
                    fullPath,
                    _sftpOptions.RemoteDirPending,
                    fileName,
                    tipoFiltro,
                    forzarVencimientoDiaAnterior,
                    solicitud.CONSECUTIVO);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                /*
                 * Limpia cualquier entidad que haya quedado modificada después
                 * de un SaveChanges fallido, de modo que el siguiente
                 * consecutivo empiece con un ChangeTracker limpio.
                 */
                _db.ChangeTracker.Clear();

                /*
                 * El error queda aislado al consecutivo actual. Los demás
                 * consecutivos del mismo grupo continúan procesándose.
                 */
                _logger.LogError(
                    ex,
                    "Error procesando solicitud. El job continuará con la siguiente. " +
                    "BU={BU}, Consecutivo={Consecutivo}, Tipo={Tipo}, Reversa={Reversa}.",
                    solicitud.BU_NOMBRE,
                    solicitud.CONSECUTIVO,
                    tipoFiltro,
                    forzarVencimientoDiaAnterior);
            }
        }
    }

    private string ObtenerHuellaSftp()
    {
        if (!string.IsNullOrWhiteSpace(_cachedFingerprint))
            return _cachedFingerprint;

        lock (_fingerprintLock)
        {
            if (!string.IsNullOrWhiteSpace(_cachedFingerprint))
                return _cachedFingerprint;

            if (!string.IsNullOrWhiteSpace(_sftpOptions.SshHostKeyFingerprint))
            {
                _cachedFingerprint = _sftpOptions.SshHostKeyFingerprint.Trim();
                return _cachedFingerprint;
            }

            _cachedFingerprint = SftpService.DescubrirHuellaSshHost(
                host: _sftpOptions.Host,
                port: _sftpOptions.Port,
                user: _sftpOptions.User,
                privateKeyPath: _sftpOptions.PrivateKeyPath,
                privateKeyPassphrase: _sftpOptions.PrivateKeyPassphrase
            );

            return _cachedFingerprint;
        }
    }

    private static IEnumerable<PREDESCUENTO> OrdenarSolicitudesPorConsecutivo(
        IEnumerable<PREDESCUENTO> solicitudes)
    {
        return solicitudes
            .OrderBy(x => EsConsecutivoNumerico(x.CONSECUTIVO) ? 0 : 1)
            .ThenBy(x => ObtenerConsecutivoNumerico(x.CONSECUTIVO))
            .ThenBy(x => T(x.CONSECUTIVO), StringComparer.OrdinalIgnoreCase);
    }

    private static bool EsConsecutivoNumerico(string? consecutivo) =>
        long.TryParse(T(consecutivo), out _);

    private static long ObtenerConsecutivoNumerico(string? consecutivo) =>
        long.TryParse(T(consecutivo), out var valor)
            ? valor
            : long.MaxValue;

    private static IEnumerable<List<string>> Chunk(
        IReadOnlyList<string> source,
        int size)
    {
        for (var index = 0; index < source.Count; index += size)
        {
            yield return source
                .Skip(index)
                .Take(size)
                .ToList();
        }
    }

    private static string T(string? s) => (s ?? "").Trim();

    private static string N(string? s) =>
        T(s).ToUpperInvariant();

    private static bool Eq(string? a, string b) =>
        string.Equals(T(a), b, StringComparison.OrdinalIgnoreCase);
}
