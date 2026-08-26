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

        var cedula = T(_configuration["IdentificadorTiendas"]);

        if (string.IsNullOrWhiteSpace(cedula))
        {
            return SincronizacionTiendasResult.Fallo(
                "No se configuró IdentificadorTiendas en appsettings.json.");
        }

        /*
         * Equivalente a:
         *
         * SELECT REGISTRY_ID
         * FROM XXORA_CUSTOMER_MASTER
         * WHERE CEDULA = :cedula
         */
        var registryId = await _oracleContext.XXORA_CUSTOMER_MASTERs
            .AsNoTracking()
            .Where(x =>
                x.CEDULA != null &&
                x.REGISTRY_ID != null &&
                x.CEDULA.Trim() == cedula)
            .Select(x => x.REGISTRY_ID)
            .FirstOrDefaultAsync(cancellationToken);

        registryId = T(registryId);

        if (string.IsNullOrWhiteSpace(registryId))
        {
            return SincronizacionTiendasResult.Fallo(
                $"No se encontró REGISTRY_ID en XXORA_CUSTOMER_MASTER para la cédula {cedula}.");
        }

        /*
         * Equivalente a:
         *
         * SELECT *
         * FROM XXORA_DISCOUNT_LIST
         * WHERE PARTY_NUMBER = :registryId
         */
        var descuentosDb = await _oracleContext.XXORA_DISCOUNT_LISTs
            .AsNoTracking()
            .Where(x =>
                x.PARTY_NUMBER != null &&
                x.PARTY_NUMBER.Trim() == registryId &&
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
            .ToListAsync(cancellationToken);

        if (descuentosDb.Count == 0)
        {
            return SincronizacionTiendasResult.Fallo(
                $"No existen descuentos en XXORA_DISCOUNT_LIST para PARTY_NUMBER={registryId}.");
        }

        // Se compara solamente la fecha porque START_DATE y END_DATE
        // normalmente representan días completos.
        //
        // Ejemplo:
        // END_DATE = 20/07/2026 continúa vigente durante todo el 20/07/2026.
        // Se considera vencido a partir del 21/07/2026.
        var fechaActual = DateTime.Today;

        /*
         * Descuentos CLIENTE vigentes.
         *
         * Reglas:
         * - START_DATE debe ser menor o igual a la fecha actual.
         * - Si END_DATE es NULL, el descuento no tiene vencimiento.
         * - Si END_DATE tiene valor, debe ser mayor o igual a la fecha actual.
         *
         * Si por historial aparecen varias filas CLIENTE vigentes para un mismo
         * artículo, se toma la más reciente según START_DATE y LAST_UPDATE_DATE.
         */
        var descuentosCliente = descuentosDb
            .Where(x =>
                N(x.RULE_DISCOUNT_NAME) == "CLIENTE" ||
                N(x.RULE_DISCOUNT_NAME).Contains("CLIENT"))
            .Where(x =>
                x.START_DATE.Date <= fechaActual &&
                (
                    !x.END_DATE.HasValue ||
                    x.END_DATE.Value.Date >= fechaActual
                ))
            .GroupBy(
                x => N(x.ITEM_NUMBER),
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(x => x.START_DATE)
                .ThenByDescending(x => x.LAST_UPDATE_DATE)
                .First())
            .Where(x => !string.IsNullOrWhiteSpace(T(x.ITEM_NUMBER)))
            .ToDictionary(
                x => N(x.ITEM_NUMBER),
                x => x.DISCOUNT_PRICE,
                StringComparer.OrdinalIgnoreCase);

        /*
         * No se debe detener la sincronización cuando no existen descuentos
         * CLIENTE vigentes.
         *
         * El proceso debe continuar para poner en cero los descuentos vencidos
         * que todavía estén registrados en INV_ARTIC_PROV.
         */
        if (descuentosCliente.Count == 0)
        {
            _logger.LogWarning(
                "El cliente {RegistryId} no tiene descuentos CLIENTE vigentes. " +
                "Los descuentos existentes en tiendas serán puestos en cero.",
                registryId);
        }

        /*
         * Promociones vigentes.
         *
         * Se valida tanto START_DATE como END_DATE. Si existen varias
         * promociones vigentes para el mismo artículo, se toma la más
         * reciente.
         */
        var promocionesVigentes = descuentosDb
            .Where(x =>
                N(x.RULE_DISCOUNT_NAME) == "PROMOCION" ||
                N(x.RULE_DISCOUNT_NAME).Contains("PROMOC"))
            .Where(x =>
                x.START_DATE.Date <= fechaActual &&
                x.END_DATE.HasValue &&
                x.END_DATE.Value.Date >= fechaActual)
            .GroupBy(
                x => N(x.ITEM_NUMBER),
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(x => x.START_DATE)
                .ThenByDescending(x => x.LAST_UPDATE_DATE)
                .First())
            .Where(x => !string.IsNullOrWhiteSpace(T(x.ITEM_NUMBER)))
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
        var descuentosFinales = new Dictionary<string, decimal>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var descuentoCliente in descuentosCliente)
        {
            var itemNumber = descuentoCliente.Key;
            var porcentajeFinal = descuentoCliente.Value;

            if (promocionesVigentes.TryGetValue(
                    itemNumber,
                    out var porcentajePromocion))
            {
                porcentajeFinal += porcentajePromocion;
            }

            descuentosFinales[itemNumber] = decimal.Round(
                porcentajeFinal,
                2,
                MidpointRounding.AwayFromZero);
        }

        var itemNumbers = descuentosFinales.Keys
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var articulosTiendas = new List<
            SolicitudesDescuentos.ModelsTiendas.INV_ARTIC_PROV>();

        var filasActualizadas = 0;
        var filasPuestasEnCero = 0;

        await using var transaction =
            await _lancoTiendasContext.Database.BeginTransactionAsync(
                cancellationToken);

        try
        {
            /*
             * Primero se eliminan todos los descuentos actuales.
             *
             * Después, dentro de esta misma transacción, se vuelven a colocar
             * únicamente los descuentos que actualmente existen en
             * XXORA_DISCOUNT_LIST para el cliente configurado.
             */
            filasPuestasEnCero =
                await _lancoTiendasContext.Database.ExecuteSqlRawAsync(
                    @"UPDATE LANCOP.INV_ARTIC_PROV
             SET DESC_FIJO = 0
           WHERE NVL(DESC_FIJO, 0) <> 0",
                    cancellationToken);

            /*
             * Ahora se buscan únicamente los artículos que sí deben conservar
             * un descuento.
             *
             * Se consulta por bloques porque Oracle admite un máximo aproximado
             * de 1000 expresiones dentro de un IN.
             */
            foreach (var itemChunk in Chunk(itemNumbers, OracleInChunkSize))
            {
                var chunkLocal = itemChunk.ToList();

                var encontrados = await _lancoTiendasContext.INV_ARTIC_PROVs
                    .Where(x =>
                        x.COD_ARTIC_PROV != null &&
                        chunkLocal.Contains(
                            x.COD_ARTIC_PROV.Trim().ToUpper()))
                    .ToListAsync(cancellationToken);

                articulosTiendas.AddRange(encontrados);
            }

            /*
             * Después de poner todo en cero, se asigna el descuento calculado
             * solamente a los artículos que sí aparecen en descuentosFinales.
             */
            foreach (var articulo in articulosTiendas)
            {
                var codArticuloProveedor = N(articulo.COD_ARTIC_PROV);

                if (!descuentosFinales.TryGetValue(
                        codArticuloProveedor,
                        out var nuevoDescuento))
                {
                    continue;
                }

                nuevoDescuento = decimal.Round(
                    nuevoDescuento,
                    2,
                    MidpointRounding.AwayFromZero);

                articulo.DESC_FIJO = nuevoDescuento;

                if (nuevoDescuento != 0)
                    filasActualizadas++;
            }

            if (articulosTiendas.Count > 0)
            {
                await _lancoTiendasContext.SaveChangesAsync(
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        var resultado = new SincronizacionTiendasResult
        {
            Ok = true,
            Mensaje =
                $"Sincronización completada para PARTY_NUMBER={registryId}.",
            RegistryId = registryId,
            DescuentosCliente = descuentosCliente.Count,
            PromocionesVigentes = promocionesVigentes.Count,
            ArticulosCalculados = descuentosFinales.Count,
            FilasEncontradasTiendas = articulosTiendas.Count,
            FilasPuestasEnCero = filasPuestasEnCero,
            FilasActualizadas = filasActualizadas
        };

        _logger.LogInformation(
            "Sincronización descuentos tiendas completada. " +
            "Cedula={Cedula}, RegistryId={RegistryId}, " +
            "Cliente={Cliente}, Promociones={Promociones}, " +
            "Calculados={Calculados}, Encontrados={Encontrados}, " +
            "PuestosEnCero={PuestosEnCero}, ConDescuento={ConDescuento}.",
            cedula,
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
        for (var index = 0; index < source.Count; index += size)
        {
            yield return source
                .Skip(index)
                .Take(size)
                .ToList();
        }
    }
}