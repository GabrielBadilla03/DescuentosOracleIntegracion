using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SolicitudesDescuentos.Data;
using SolicitudesDescuentos.ModelsOracle;

namespace SolicitudesDescuentos.Services;

public class DescuentosBatchService : IDescuentosBatchService
{
    private const string BuImpulsadores = "LANCO_CR";
    private const string CiaLanco = "001";

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

    public async Task ProcesarPendientesAsync(CancellationToken cancellationToken = default)
    {
        await EjecutarLimpiezaImpulsadoresAsync(cancellationToken);

        var headers = await _db.PREDESCUENTOs
            .AsNoTracking()
            .Where(p =>
                p.GENERADO == "N" &&
                (p.ESTADO == "Aprobado" || p.ESTADO == "Reversado"))
            .OrderBy(p => p.CONSECUTIVO)
            .ToListAsync(cancellationToken);

        if (headers.Count > 0)
        {
            var aprobados = headers
                .Where(x => Eq(x.ESTADO, "Aprobado"))
                .ToList();

            var reversados = headers
                .Where(x => Eq(x.ESTADO, "Reversado"))
                .ToList();

            if (aprobados.Count > 0)
            {
                await ProcesarGrupoAsync(
                    aprobados.Where(x => ArchivosDescuentosService.NormalizeTipo(x.TIPODESCUENTO) == "promocional").ToList(),
                    "promocional",
                    forzarVencimientoDiaAnterior: false,
                    cancellationToken);

                await ProcesarGrupoAsync(
                    aprobados.Where(x => ArchivosDescuentosService.NormalizeTipo(x.TIPODESCUENTO) == "fijo").ToList(),
                    "fijo",
                    forzarVencimientoDiaAnterior: false,
                    cancellationToken);
            }

            if (reversados.Count > 0)
            {
                await ProcesarGrupoAsync(
                    reversados.Where(x => ArchivosDescuentosService.NormalizeTipo(x.TIPODESCUENTO) == "promocional").ToList(),
                    "promocional",
                    forzarVencimientoDiaAnterior: true,
                    cancellationToken);

                await ProcesarGrupoAsync(
                    reversados.Where(x => ArchivosDescuentosService.NormalizeTipo(x.TIPODESCUENTO) == "fijo").ToList(),
                    "fijo",
                    forzarVencimientoDiaAnterior: true,
                    cancellationToken);
            }
        }

        await ProcesarArticulosNoPromoAsync(cancellationToken);
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
         * Al ser dos DbContext diferentes, primero se cargan los códigos
         * inactivos y luego se eliminan las asignaciones correspondientes.
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
            .Select(T)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (empleadosInactivos.Count == 0)
        {
            _logger.LogInformation(
                "Limpieza de impulsadores finalizada: no hay empleados inactivos " +
                "en PLAEMPLEADO para CIA={Cia}.",
                CiaLanco);

            return;
        }

        /*
         * Se cargan únicamente los registros de la BU correspondiente.
         * La comparación final se hace en memoria para evitar problemas de
         * traducción entre proveedores y límites de listas IN en Oracle.
         */
        var asignaciones = await _db.IMPULSADORESORACLEs
            .Where(x => x.BU_NOMBRE == BuImpulsadores)
            .ToListAsync(cancellationToken);

        var asignacionesAEliminar = asignaciones
            .Where(x => empleadosInactivos.Contains(T(x.EMPLEADO)))
            .ToList();

        if (asignacionesAEliminar.Count == 0)
        {
            _logger.LogInformation(
                "Limpieza de impulsadores finalizada: ningún empleado inactivo " +
                "está asignado en IMPULSADORESORACLE para BU={Bu}.",
                BuImpulsadores);

            return;
        }

        var empleadosEliminados = asignacionesAEliminar
            .Select(x => T(x.EMPLEADO))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        _db.IMPULSADORESORACLEs.RemoveRange(asignacionesAEliminar);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Limpieza de IMPULSADORESORACLE completada. " +
            "Asignaciones eliminadas={CantidadAsignaciones}, " +
            "empleados inactivos afectados={CantidadEmpleados}, " +
            "BU={Bu}, CIA={Cia}. Empleados={Empleados}",
            asignacionesAEliminar.Count,
            empleadosEliminados.Count,
            BuImpulsadores,
            CiaLanco,
            string.Join(", ", empleadosEliminados));
    }

    private async Task ProcesarArticulosNoPromoAsync(CancellationToken cancellationToken)
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
            var result = await _archivosService.GenerarNoPromoPendienteAsync(
                bu: T(art.BU_NAME),
                org: T(art.ORGANIZATION_CODE),
                itemNumber: T(art.ITEM_NUMBER),
                marcarComoGenerado: false,
                ct: cancellationToken);

            if (!result.Ok || result.ArchivoBytes == null || result.ArchivoBytes.Length == 0)
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

            await File.WriteAllBytesAsync(fullPath, result.ArchivoBytes, cancellationToken);

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

            sftp.UploadFile(
                localFullPath: fullPath,
                remoteDir: _sftpOptions.RemoteDirPending,
                remoteFileName: fileName,
                overwrite: true
            );

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
    }

    private async Task ProcesarGrupoAsync(
        List<PREDESCUENTO> grupo,
        string tipoFiltro,
        bool forzarVencimientoDiaAnterior,
        CancellationToken cancellationToken)
    {
        if (grupo.Count == 0)
            return;

        foreach (var solicitud in grupo.OrderBy(x => T(x.CONSECUTIVO)))
        {
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

            if (!result.Ok || result.ArchivoBytes == null || result.ArchivoBytes.Length == 0)
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

            var fileName = string.IsNullOrWhiteSpace(result.NombreArchivo)
                ? "Descuentos_COSTARICA_ALL.zip"
                : result.NombreArchivo.Trim();

            var fullPath = Path.Combine(_options.OutputFolder, fileName);

            await File.WriteAllBytesAsync(fullPath, result.ArchivoBytes, cancellationToken);

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

            sftp.UploadFile(
                localFullPath: fullPath,
                remoteDir: _sftpOptions.RemoteDirPending,
                remoteFileName: fileName,
                overwrite: true
            );

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

    private static string T(string? s) => (s ?? "").Trim();

    private static bool Eq(string? a, string b) =>
        string.Equals((a ?? "").Trim(), b, StringComparison.OrdinalIgnoreCase);
}