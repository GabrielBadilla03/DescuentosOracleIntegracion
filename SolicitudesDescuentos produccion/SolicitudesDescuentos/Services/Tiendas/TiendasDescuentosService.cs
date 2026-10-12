using Microsoft.EntityFrameworkCore;
using SolicitudesDescuentos.Data;

namespace SolicitudesDescuentos.Services.Tiendas;

public sealed class TiendasDescuentosService : ITiendasDescuentosService
{
    private const int OracleInChunkSize = 900;

    private readonly OracleContext _oracleContext;
    private readonly LancoTiendasContext _lancoTiendasContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TiendasDescuentosService> _logger;

    public TiendasDescuentosService(
        OracleContext oracleContext,
        LancoTiendasContext lancoTiendasContext,
        IConfiguration configuration,
        ILogger<TiendasDescuentosService> logger)
    {
        _oracleContext = oracleContext;
        _lancoTiendasContext = lancoTiendasContext;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<SincronizacionTiendasResult> SincronizarAsync(
        CancellationToken cancellationToken = default)
    {
        static string T(string? value)
            => (value ?? string.Empty).Trim();

        static string N(string? value)
            => T(value).ToUpperInvariant();

        var cedula =
            T(_configuration["IdentificadorTiendas"]);

        if (string.IsNullOrWhiteSpace(cedula))
        {
            return SincronizacionTiendasResult.Fallo(
                "No se configuró IdentificadorTiendas en appsettings.json.");
        }

        _logger.LogInformation(
            "[JOB TIENDAS][1/5] Buscando cliente configurado. Cédula={Cedula}.",
            cedula);

        /*
         * SELECT REGISTRY_ID
         * FROM XXORA_CUSTOMER_MASTER
         * WHERE CEDULA = :cedula
         */
        var registryId =
            await _oracleContext
                .XXORA_CUSTOMER_MASTERs
                .AsNoTracking()
                .Where(x =>
                    x.CEDULA != null &&
                    x.REGISTRY_ID != null &&
                    x.CEDULA.Trim() == cedula)
                .Select(x => x.REGISTRY_ID)
                .FirstOrDefaultAsync(
                    cancellationToken);

        registryId = T(registryId);

        if (string.IsNullOrWhiteSpace(registryId))
        {
            return SincronizacionTiendasResult.Fallo(
                $"No se encontró REGISTRY_ID en XXORA_CUSTOMER_MASTER para la cédula {cedula}.");
        }

        _logger.LogInformation(
            "[JOB TIENDAS][1/5] Cliente encontrado. RegistryId={RegistryId}.",
            registryId);

        _logger.LogInformation(
            "[JOB TIENDAS][2/5] Cargando descuentos del cliente...");

        /*
         * SELECT *
         * FROM XXORA_DISCOUNT_LIST
         * WHERE PARTY_NUMBER = :registryId
         */
        var descuentosDb =
            await _oracleContext
                .XXORA_DISCOUNT_LISTs
                .AsNoTracking()
                .Where(x =>
                    x.PARTY_NUMBER != null &&
                    x.PARTY_NUMBER.Trim() ==
                    registryId &&
                    x.ITEM_NUMBER != null &&
                    x.RULE_DISCOUNT_NAME != null)
                .Select(x => new
                {
                    x.ITEM_NUMBER,
                    x.RULE_DISCOUNT_NAME,
                    x.DISCOUNT_PRICE,
                    x.START_DATE,
                    x.END_DATE,
                    x.LAST_UPDATE_DATE
                })
                .ToListAsync(
                    cancellationToken);

        if (descuentosDb.Count == 0)
        {
            return SincronizacionTiendasResult.Fallo(
                $"No existen descuentos en XXORA_DISCOUNT_LIST para PARTY_NUMBER={registryId}.");
        }

        _logger.LogInformation(
            "[JOB TIENDAS][2/5] Filas de descuentos cargadas={Cantidad}.",
            descuentosDb.Count);

        _logger.LogInformation(
            "[JOB TIENDAS][3/5] Calculando descuentos CLIENTE y PROMOCIONES vigentes...");

        // Se compara solamente la fecha porque START_DATE y END_DATE
        // normalmente representan días completos.
        var fechaActual = DateTime.Today;

        /*
         * Descuentos CLIENTE vigentes.
         */
        var descuentosCliente =
            descuentosDb
                .Where(x =>
                    N(x.RULE_DISCOUNT_NAME) ==
                    "CLIENTE" ||
                    N(x.RULE_DISCOUNT_NAME)
                        .Contains("CLIENT"))
                .Where(x =>
                    x.START_DATE.Date <=
                    fechaActual &&
                    (
                        !x.END_DATE.HasValue ||
                        x.END_DATE.Value.Date >=
                        fechaActual
                    ))
                .GroupBy(
                    x => N(x.ITEM_NUMBER),
                    StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                    g.OrderByDescending(
                            x => x.START_DATE)
                        .ThenByDescending(
                            x => x.LAST_UPDATE_DATE)
                        .First())
                .Where(x =>
                    !string.IsNullOrWhiteSpace(
                        T(x.ITEM_NUMBER)))
                .ToDictionary(
                    x => N(x.ITEM_NUMBER),
                    x => x.DISCOUNT_PRICE,
                    StringComparer.OrdinalIgnoreCase);

        if (descuentosCliente.Count == 0)
        {
            _logger.LogWarning(
                "[JOB TIENDAS][3/5] El cliente {RegistryId} no tiene descuentos CLIENTE vigentes. " +
                "Los descuentos existentes en tiendas serán puestos en cero.",
                registryId);
        }

        /*
         * Promociones vigentes.
         */
        var promocionesVigentes =
            descuentosDb
                .Where(x =>
                    N(x.RULE_DISCOUNT_NAME) ==
                    "PROMOCION" ||
                    N(x.RULE_DISCOUNT_NAME)
                        .Contains("PROMOC"))
                .Where(x =>
                    x.START_DATE.Date <=
                    fechaActual &&
                    x.END_DATE.HasValue &&
                    x.END_DATE.Value.Date >=
                    fechaActual)
                .GroupBy(
                    x => N(x.ITEM_NUMBER),
                    StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                    g.OrderByDescending(
                            x => x.START_DATE)
                        .ThenByDescending(
                            x => x.LAST_UPDATE_DATE)
                        .First())
                .Where(x =>
                    !string.IsNullOrWhiteSpace(
                        T(x.ITEM_NUMBER)))
                .ToDictionary(
                    x => N(x.ITEM_NUMBER),
                    x => x.DISCOUNT_PRICE,
                    StringComparer.OrdinalIgnoreCase);

        /*
         * Resultado final:
         *
         * CLIENTE sin promoción:
         *     descuento final = CLIENTE
         *
         * CLIENTE con promoción vigente:
         *     descuento final = CLIENTE + PROMOCION
         */
        var descuentosFinales =
            new Dictionary<string, decimal>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var descuentoCliente
                 in descuentosCliente)
        {
            var itemNumber =
                descuentoCliente.Key;

            var porcentajeFinal =
                descuentoCliente.Value;

            if (promocionesVigentes.TryGetValue(
                    itemNumber,
                    out var porcentajePromocion))
            {
                porcentajeFinal +=
                    porcentajePromocion;
            }

            descuentosFinales[itemNumber] =
                decimal.Round(
                    porcentajeFinal,
                    2,
                    MidpointRounding.AwayFromZero);
        }

        _logger.LogInformation(
            "[JOB TIENDAS][3/5] Cálculo terminado. Cliente={Cliente}, Promociones={Promociones}, Finales={Finales}.",
            descuentosCliente.Count,
            promocionesVigentes.Count,
            descuentosFinales.Count);

        var itemNumbers =
            descuentosFinales.Keys
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        var articulosTiendas =
            new List<
                SolicitudesDescuentos.ModelsTiendas
                    .INV_ARTIC_PROV>();

        var filasActualizadas = 0;
        var filasPuestasEnCero = 0;

        _logger.LogInformation(
            "[JOB TIENDAS][4/5] Actualizando INV_ARTIC_PROV...");

        await using var transaction =
            await _lancoTiendasContext
                .Database
                .BeginTransactionAsync(
                    cancellationToken);

        try
        {
            /*
             * Primero se eliminan todos los descuentos actuales.
             * Después se colocan nuevamente solo los vigentes.
             */
            filasPuestasEnCero =
                await _lancoTiendasContext
                    .Database
                    .ExecuteSqlRawAsync(
                        @"UPDATE LANCOP.INV_ARTIC_PROV
             SET DESC_FIJO = 0
           WHERE NVL(DESC_FIJO, 0) <> 0",
                        cancellationToken);

            /*
             * Oracle admite aproximadamente 1000 expresiones
             * dentro de un IN. Se consulta por bloques.
             */
            foreach (var itemChunk
                     in Chunk(
                         itemNumbers,
                         OracleInChunkSize))
            {
                var chunkLocal =
                    itemChunk.ToList();

                var encontrados =
                    await _lancoTiendasContext
                        .INV_ARTIC_PROVs
                        .Where(x =>
                            x.COD_ARTIC_PROV !=
                            null &&
                            chunkLocal.Contains(
                                x.COD_ARTIC_PROV
                                    .Trim()
                                    .ToUpper()))
                        .ToListAsync(
                            cancellationToken);

                articulosTiendas.AddRange(
                    encontrados);
            }

            foreach (var articulo
                     in articulosTiendas)
            {
                var codArticuloProveedor =
                    N(articulo.COD_ARTIC_PROV);

                if (!descuentosFinales
                        .TryGetValue(
                            codArticuloProveedor,
                            out var nuevoDescuento))
                {
                    continue;
                }

                nuevoDescuento =
                    decimal.Round(
                        nuevoDescuento,
                        2,
                        MidpointRounding
                            .AwayFromZero);

                articulo.DESC_FIJO =
                    nuevoDescuento;

                if (nuevoDescuento != 0)
                    filasActualizadas++;
            }

            if (articulosTiendas.Count > 0)
            {
                await _lancoTiendasContext
                    .SaveChangesAsync(
                        cancellationToken);
            }

            _logger.LogInformation(
                "[JOB TIENDAS][4/5] Cambios preparados. Encontrados={Encontrados}, PuestosEnCero={Cero}, ConDescuento={Actualizados}.",
                articulosTiendas.Count,
                filasPuestasEnCero,
                filasActualizadas);

            await transaction.CommitAsync(
                cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(
                cancellationToken);

            throw;
        }

        var resultado =
            new SincronizacionTiendasResult
            {
                Ok = true,
                Mensaje =
                    $"Sincronización completada para PARTY_NUMBER={registryId}.",
                RegistryId = registryId,
                DescuentosCliente =
                    descuentosCliente.Count,
                PromocionesVigentes =
                    promocionesVigentes.Count,
                ArticulosCalculados =
                    descuentosFinales.Count,
                FilasEncontradasTiendas =
                    articulosTiendas.Count,
                FilasPuestasEnCero =
                    filasPuestasEnCero,
                FilasActualizadas =
                    filasActualizadas
            };

        _logger.LogInformation(
            "[JOB TIENDAS][5/5] OK. RegistryId={RegistryId}, Cliente={Cliente}, " +
            "Promociones={Promociones}, Finales={Finales}, Encontrados={Encontrados}, " +
            "Cero={Cero}, Actualizados={Actualizados}.",
            resultado.RegistryId,
            resultado.DescuentosCliente,
            resultado.PromocionesVigentes,
            resultado.ArticulosCalculados,
            resultado.FilasEncontradasTiendas,
            resultado.FilasPuestasEnCero,
            resultado.FilasActualizadas);

        return resultado;
    }

    private static IEnumerable<List<string>> Chunk(
        IReadOnlyList<string> source,
        int size)
    {
        for (var index = 0;
             index < source.Count;
             index += size)
        {
            yield return source
                .Skip(index)
                .Take(size)
                .ToList();
        }
    }
}
