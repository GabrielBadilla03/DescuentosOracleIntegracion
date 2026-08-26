using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SolicitudesDescuentos.Data;
using SolicitudesDescuentos.ModelsOracle;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace SolicitudesDescuentos.Services;

public class ArchivosDescuentosService : IArchivosDescuentosService
{
    private readonly OracleContext _OracleContext;
    private readonly record struct XxoraKey(string PartyNumber, string ItemNumber, string Uom);

    public ArchivosDescuentosService(OracleContext oracleContext)
    {
        _OracleContext = oracleContext;
    }

    // Compatible con modelos donde FECHASOLICITUD sea DateTime o DateTime?.
    // El resultado se normaliza a DateTime? para poder validarlo y formatearlo igual.
    private static DateTime? CalcularFechaFinPricingTerm(DateTime fechaSolicitud)
        => fechaSolicitud.AddYears(5);

    private static DateTime? CalcularFechaFinPricingTerm(DateTime? fechaSolicitud)
        => fechaSolicitud.HasValue
            ? fechaSolicitud.Value.AddYears(5)
            : null;

    private static string SafeFileToken(string? raw)
    {
        var s = (raw ?? "").Trim();

        if (string.IsNullOrWhiteSpace(s))
            return "SINCONSECUTIVO";

        var sb = new StringBuilder(s.Length);

        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
                sb.Append(ch);
            else
                sb.Append('_');
        }

        var result = sb.ToString().Trim('_');

        return string.IsNullOrWhiteSpace(result)
            ? "SINCONSECUTIVO"
            : result;
    }

    private static string BuildZipFileNameByConsecutivo(string? consecutivo, bool esReversa)
    {
        var consecutivoToken = SafeFileToken(consecutivo);

        return esReversa
            ? $"Descuentos_COSTARICA_ALL_{consecutivoToken}_REVERSA.zip"
            : $"Descuentos_COSTARICA_ALL_{consecutivoToken}.zip";
    }


    public async Task<ArchivoProcesoResult> IniciarFlujoItemAsync(string itemNumber, CancellationToken ct = default)
    {
        itemNumber = (itemNumber ?? "").Trim();

        if (string.IsNullOrWhiteSpace(itemNumber))
            return ArchivoProcesoResult.Fallo("Faltan datos: seleccioná un item.");

        const string bu = "LANCO_CR";
        const string org = "CR_3";
        var itemKey = itemNumber.ToUpperInvariant();

        static string N(string? s) => (s ?? "").Trim().ToUpperInvariant();

        static string RuleBucketLocal(string? rule)
        {
            var x = N(rule);
            if (string.IsNullOrWhiteSpace(x)) return "";
            if (x == "PROMOCION" || x.Contains("PROMOC")) return "PROMOCION";
            if (x == "CLIENTE" || x.Contains("CLIENT")) return "CLIENTE";
            return "";
        }

        static DateTime? NormalizeEndDateByRule(string? rule, DateTime? end)
            => string.Equals(RuleBucketLocal(rule), "CLIENTE", StringComparison.OrdinalIgnoreCase)
                ? null
                : end;

        await using var trx = await _OracleContext.Database.BeginTransactionAsync(ct);

        try
        {
            var existeHeader = await _OracleContext.ART_NO_PROMOs
                .AnyAsync(a =>
                    (a.BU_NAME ?? "").Trim().ToUpper() == bu &&
                    (a.ORGANIZATION_CODE ?? "").Trim().ToUpper() == org &&
                    (a.ITEM_NUMBER ?? "").Trim().ToUpper() == itemKey, ct);

            if (!existeHeader)
            {
                _OracleContext.ART_NO_PROMOs.Add(new ART_NO_PROMO
                {
                    BU_NAME = bu,
                    ORGANIZATION_CODE = org,
                    ITEM_NUMBER = itemKey
                });

                await _OracleContext.SaveChangesAsync(ct);
            }

            var xxoraRows = await _OracleContext.XXORA_DISCOUNT_LISTs
                .AsNoTracking()
                .Where(x =>
                    (x.BU_NAME ?? "").Trim().ToUpper() == bu &&
                    (x.ITEM_NUMBER ?? "").Trim().ToUpper() == itemKey)
                .Select(x => new
                {
                    x.RULE_DISCOUNT_NAME,
                    x.PARTY_NUMBER,
                    x.PRICING_UOM_CODE,
                    x.DISCOUNT_PRICE,
                    x.START_DATE,
                    x.END_DATE
                })
                .ToListAsync(ct);

            if (xxoraRows.Count == 0)
            {
                await trx.CommitAsync(ct);
                return ArchivoProcesoResult.Fallo($"ART_NO_PROMO OK. No hay filas en XXORA_DISCOUNT_LIST para BU={bu} ITEM={itemKey}.");
            }

            var existentes = await _OracleContext.ART_DET_NO_PROMOs
                .AsNoTracking()
                .Where(d =>
                    (d.BU_NAME ?? "").Trim().ToUpper() == bu &&
                    (d.ORGANIZATION_CODE ?? "").Trim().ToUpper() == org &&
                    (d.ITEM_NUMBER ?? "").Trim().ToUpper() == itemKey)
                .Select(d => new
                {
                    d.RULE_DISCOUNT_NAME,
                    d.PARTY_NUMBER,
                    d.PRICING_UOM_CODE,
                    d.DISCOUNT_PRICE,
                    d.START_DATE,
                    d.END_DATE
                })
                .ToListAsync(ct);

            var existentesSet = new HashSet<string>(
                existentes.Select(e =>
                    MakeArtDetKey(
                        e.RULE_DISCOUNT_NAME,
                        e.PARTY_NUMBER,
                        e.DISCOUNT_PRICE,
                        e.START_DATE,
                        NormalizeEndDateByRule(e.RULE_DISCOUNT_NAME, e.END_DATE))),
                StringComparer.OrdinalIgnoreCase);

            foreach (var r in xxoraRows)
            {
                var rule = (r.RULE_DISCOUNT_NAME ?? "").Trim();
                if (string.IsNullOrWhiteSpace(rule)) continue;

                var party = (r.PARTY_NUMBER ?? "").Trim();
                if (string.IsNullOrWhiteSpace(party)) continue;

                var uom = string.IsNullOrWhiteSpace(r.PRICING_UOM_CODE) ? null : r.PRICING_UOM_CODE.Trim();
                var effectiveEnd = NormalizeEndDateByRule(rule, r.END_DATE);
                var k = MakeArtDetKey(rule, party, r.DISCOUNT_PRICE, r.START_DATE, effectiveEnd);

                if (!existentesSet.Add(k))
                    continue;

                _OracleContext.ART_DET_NO_PROMOs.Add(new ART_DET_NO_PROMO
                {
                    BU_NAME = bu,
                    ORGANIZATION_CODE = org,
                    ITEM_NUMBER = itemKey,
                    RULE_DISCOUNT_NAME = rule,
                    PARTY_NUMBER = party,
                    PRICING_UOM_CODE = uom,
                    DISCOUNT_PRICE = r.DISCOUNT_PRICE,
                    START_DATE = r.START_DATE,
                    END_DATE = effectiveEnd
                });
            }

            await _OracleContext.SaveChangesAsync(ct);
            await trx.CommitAsync(ct);

            var zipBytes = await GenerarZipUpdateDesdeXxoraAsync(
                bu: bu,
                itemNumber: itemKey,
                startDate: DateTime.Now,
                endDate: null,
                descuento: 0m,
                ct: ct);

            if (zipBytes.Length == 0)
                return ArchivoProcesoResult.Fallo($"Flujo OK, pero no se pudo generar ZIP (sin filas XXORA) para BU={bu} ITEM={itemKey}.");

            return ArchivoProcesoResult.Exito(zipBytes);
        }
        catch (Exception ex)
        {
            await trx.RollbackAsync(ct);
            return ArchivoProcesoResult.Fallo($"Error en flujo NO PROMO: {ex.Message}");
        }
    }

    public async Task<ArchivoProcesoResult> ReactivarFlujoItemAsync(
        string itemNumber,
        DateTime? startDate,
        DateTime? endDate,
        decimal descuento,
        CancellationToken ct = default)
    {
        itemNumber = (itemNumber ?? "").Trim();

        if (string.IsNullOrWhiteSpace(itemNumber))
            return ArchivoProcesoResult.Fallo("Faltan datos: seleccioná un item.");

        if (!startDate.HasValue)
            return ArchivoProcesoResult.Fallo("Faltan datos: start_date.");

        if (endDate.HasValue && endDate.Value.Date < startDate.Value.Date)
            return ArchivoProcesoResult.Fallo("End Date no puede ser menor que Start Date.");

        const string bu = "LANCO_CR";
        const string org = "CR_3";
        var itemKey = itemNumber.ToUpperInvariant();

        var detCount = await _OracleContext.ART_DET_NO_PROMOs
            .AsNoTracking()
            .CountAsync(d =>
                (d.BU_NAME ?? "").Trim().ToUpper() == bu &&
                (d.ORGANIZATION_CODE ?? "").Trim().ToUpper() == org &&
                (d.ITEM_NUMBER ?? "").Trim().ToUpper() == itemKey, ct);

        if (detCount == 0)
            return ArchivoProcesoResult.Fallo($"No hay registros en ART_DET_NO_PROMO para BU={bu} ITEM={itemKey}.");

        var zipBytes = await GenerarZipReactivarDesdeArtDetAsync(
            bu: bu,
            org: org,
            itemNumber: itemKey,
            startDate: startDate.Value,
            endDate: endDate,
            descuento: descuento,
            ct: ct);

        if (zipBytes.Length == 0)
            return ArchivoProcesoResult.Fallo($"Flujo OK, pero no se pudo generar ZIP (sin filas ART_DET_NO_PROMO) para BU={bu} ITEM={itemKey}.");

        return ArchivoProcesoResult.Exito(zipBytes);
    }

    public async Task<ArchivoProcesoResult> DescargarExcelAsync(
        List<string> seleccionados,
        string tipoFiltro,
        bool marcarComoGenerado = true,
        bool forzarVencimientoDiaAnterior = false,
        CancellationToken ct = default)
    {
        var tipo = NormalizeTipo(tipoFiltro);
        if (tipo == "") tipo = "promocional";

        if (seleccionados == null || seleccionados.Count == 0)
            return ArchivoProcesoResult.Fallo("Debe seleccionar al menos una solicitud para generar el Excel.");

        var pares = new List<(string CodCia, string Consecutivo)>();

        foreach (var s in seleccionados)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            var parts = s.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2) continue;
            pares.Add((parts[0], parts[1]));
        }

        pares = pares.Distinct().ToList();

        if (pares.Count == 0)
            return ArchivoProcesoResult.Fallo("No se recibieron solicitudes válidas.");

        return await DescargarExcelDesdeParesAsync(
            pares,
            tipo,
            marcarComoGenerado,
            forzarVencimientoDiaAnterior,
            ct);
    }

    public async Task<ArchivoProcesoResult> DescargarExcelDesdeParesAsync(
        List<(string CodCia, string Consecutivo)> pares,
        string tipoFiltro,
        bool marcarComoGenerado = true,
        bool forzarVencimientoDiaAnterior = false,
        CancellationToken ct = default)
    {
        static string T(string? s) => (s ?? "").Trim();
        static string N(string? s) => (s ?? "").Trim().ToUpperInvariant();

        static string FormatDateLocal(DateTime? fecha) =>
            fecha.HasValue ? fecha.Value.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture) : string.Empty;

        static string SafeId(string? raw, int maxLen = 40)
        {
            var s = (raw ?? "").Trim();
            if (string.IsNullOrEmpty(s)) return "X";

            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');

            var outp = sb.ToString();
            while (outp.Contains("__")) outp = outp.Replace("__", "_");

            if (outp.Length > maxLen) outp = outp[..maxLen];
            return outp.Trim('_');
        }

        static string RuleBucket(string tipoNorm) =>
            string.Equals(tipoNorm, "promocional", StringComparison.OrdinalIgnoreCase)
                ? "PROMOCION"
                : "CLIENTE";

        string BuildSridMr(string itemKey, string partyCode, string bucket)
        {
            var partyId = SafeId(partyCode, 30);

            if (string.Equals(bucket, "PROMOCION", StringComparison.OrdinalIgnoreCase))
                return $"SRID_SMID_STID_SDLID{itemKey}_PROMOCION_{partyId}";

            return $"SRID_SMID_STID_SDLID{itemKey}_{partyId}";
        }

        const string ACTION_CREATE = "CREATE";
        const string ACTION_UPDATE = "UPDATE";
        const string ACTION_NOOP = "NO-OP";

        const string sourceDiscountListId = "LP_001CR_CL";
        const string name = "Descuentos_CostaRica";
        const string description = "Descuentos_CostaRica";
        const string businessUnitId = "";
        const string businessUnitName = "LANCO_CR";
        const string currencyCode = "CRC";
        const string statusCode = "APPROVED";

        var tipo = T(tipoFiltro).ToLowerInvariant();
        if (tipo != "promocional" && tipo != "fijo")
            return ArchivoProcesoResult.Fallo("tipoFiltro inválido. Use 'promocional' o 'fijo'.");

        var bucketName = RuleBucket(tipo);
        var esReversa = forzarVencimientoDiaAnterior;
        var fechaFinReversa = DateTime.Today.AddSeconds(-1);

        if (pares == null || pares.Count == 0)
            return ArchivoProcesoResult.Fallo("No hay solicitudes seleccionadas.");

        var buSet = pares.Select(p => T(p.CodCia)).Where(x => x != "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var consecSet = pares.Select(p => T(p.Consecutivo)).Where(x => x != "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var headersRaw = await _OracleContext.PREDESCUENTOs
            .AsNoTracking()
            .Where(p => buSet.Contains(p.BU_NOMBRE) && consecSet.Contains(p.CONSECUTIVO))
            .ToListAsync(ct);

        static bool Eq(string? a, string b) =>
            string.Equals((a ?? "").Trim(), b, StringComparison.OrdinalIgnoreCase);

        var headers = headersRaw
            .Where(h => pares.Any(k => Eq(h.BU_NOMBRE, k.CodCia) && Eq(h.CONSECUTIVO, k.Consecutivo)))
            .ToList();

        if (headers.Count == 0)
            return ArchivoProcesoResult.Fallo("No se encontraron solicitudes con los consecutivos seleccionados.");

        var consecutivosUnicos = headers
            .Select(h => T(h.CONSECUTIVO))
            .Where(x => x != "")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (consecutivosUnicos.Count != 1)
            return ArchivoProcesoResult.Fallo("Debe generarse un archivo por cada consecutivo.");

        var nombreArchivo = BuildZipFileNameByConsecutivo(
            consecutivosUnicos[0],
            esReversa
        );

        // BATCH_NAME igual al nombre del archivo, pero sin extensión .zip
        // Ejemplo normal: Descuentos_COSTARICA_ALL_123
        // Ejemplo reversa: Descuentos_COSTARICA_ALL_123_REVERSA
        var batchName = Path.GetFileNameWithoutExtension(nombreArchivo);

        if (headers.Any(h => !Eq(h.ESTADO, "Aprobado") && !Eq(h.ESTADO, "Reversado")))
            return ArchivoProcesoResult.Fallo("Hay solicitudes con estado inválido para generar.");

        if (headers.Any(h => !Eq(h.GENERADO, "N")))
            return ArchivoProcesoResult.Fallo("Hay solicitudes que ya fueron generadas (GENERADO != 'N').");

        var mixed = headers
            .Where(h => NormalizeTipo(h.TIPODESCUENTO) != tipo)
            .Select(h => $"{T(h.BU_NOMBRE)}|{T(h.CONSECUTIVO)} ({T(h.TIPODESCUENTO)})")
            .ToList();

        if (mixed.Count > 0)
            return ArchivoProcesoResult.Fallo("Anti-mezcla: seleccionaste solicitudes con tipo distinto al filtro. Ej: " + string.Join(", ", mixed));

        var exportRows = new List<(string BU, string PartyCode, string ItemNumber, string Uom, decimal Valor, DateTime? Start, DateTime? End, DateTime? PricingTermEnd)>();

        foreach (var h in headers)
        {
            var bu = T(h.BU_NOMBRE);
            var party = T(h.COD_CLIENTE);
            if (bu == "" || party == "") continue;

            var start = tipo == "promocional"
                ? (h.FECHAINICIO ?? h.FECHASOLICITUD)
                : h.FECHASOLICITUD;

            // SOLO PARA PricingTermsInterface:
            // la fecha fin siempre es FECHASOLICITUD + 5 años,
            // tanto para descuentos fijos como promocionales y también en reversa.
            // El helper soporta FECHASOLICITUD como DateTime o DateTime?.
            var pricingTermEnd = CalcularFechaFinPricingTerm(h.FECHASOLICITUD);

            if (!pricingTermEnd.HasValue)
                return ArchivoProcesoResult.Fallo(
                    $"La solicitud {T(h.CONSECUTIVO)} no tiene FECHASOLICITUD. " +
                    "No se puede calcular END_DATE de PricingTermsInterface.");

            DateTime? end;
            if (forzarVencimientoDiaAnterior)
                end = DateTime.Today.AddSeconds(-1);
            else
                end = tipo == "promocional" ? h.FECHAFIN : null;

            var detalles = await _OracleContext.PREDETDESCUENTOs
                .AsNoTracking()
                .Where(d => d.BU_NOMBRE == bu && d.COD_CLIENTE == party && d.CONSECUTIVO == h.CONSECUTIVO)
                .ToListAsync(ct);

            if (detalles.Count == 0) continue;

            var byArticulo = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var byLineaClase = new Dictionary<(string Linea, string Clase), decimal>();
            var byLinea = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            foreach (var d in detalles)
            {
                var codArt = T(d.COD_ARTICULO);
                var codLin = T(d.COD_LINEA);
                var codCla = T(d.COD_CLASE);

                if (Eq(codArt, "NULL")) codArt = "";
                if (Eq(codCla, "NULL")) codCla = "";

                var val = d.VALOR;

                if (!string.IsNullOrWhiteSpace(codArt))
                {
                    byArticulo[codArt] = val;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(codLin) && !string.IsNullOrWhiteSpace(codCla))
                {
                    byLineaClase[(codLin, codCla)] = val;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(codLin))
                    byLinea[codLin] = val;
            }

            var lineasNeeded = byLinea.Keys
                .Concat(byLineaClase.Keys.Select(x => x.Linea))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var invItems = new List<(string CodArticulo, string Medida, string CodLinea, string CodClase)>();

            var artsExplicit = byArticulo.Keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (artsExplicit.Count > 0)
            {
                foreach (var chunk in ChunkList(artsExplicit, 900))
                {
                    var items = await _OracleContext.INV_ARTICULOs
                        .AsNoTracking()
                        .Where(i => i.COD_ARTICULO != null && chunk.Contains(i.COD_ARTICULO))
                        .Select(i => new { i.COD_ARTICULO, i.MEDIDA, i.COD_LINEA, COD_CLASE = i.COD_CLASE })
                        .ToListAsync(ct);

                    invItems.AddRange(items.Select(x => (T(x.COD_ARTICULO), T(x.MEDIDA), T(x.COD_LINEA), T(x.COD_CLASE))));
                }
            }

            if (lineasNeeded.Count > 0)
            {
                foreach (var chunk in ChunkList(lineasNeeded, 200))
                {
                    var items = await _OracleContext.INV_ARTICULOs
                        .AsNoTracking()
                        .Where(i => i.COD_LINEA != null && chunk.Contains(i.COD_LINEA))
                        .Select(i => new { i.COD_ARTICULO, i.MEDIDA, i.COD_LINEA, COD_CLASE = i.COD_CLASE })
                        .ToListAsync(ct);

                    invItems.AddRange(items.Select(x => (T(x.COD_ARTICULO), T(x.MEDIDA), T(x.COD_LINEA), T(x.COD_CLASE))));
                }
            }

            var invByArt = invItems
                .Where(x => x.CodArticulo != "")
                .GroupBy(x => x.CodArticulo, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            foreach (var it in invByArt)
            {
                var item = it.CodArticulo;
                var uom = it.Medida;
                var lin = it.CodLinea;
                var cla = it.CodClase;

                if (item == "" || uom == "")
                    continue;

                decimal? valorAplicable = null;

                if (byArticulo.TryGetValue(item, out var vArt))
                    valorAplicable = vArt;
                else if (lin != "" && cla != "" && byLineaClase.TryGetValue((lin, cla), out var vLC))
                    valorAplicable = vLC;
                else if (lin != "" && byLinea.TryGetValue(lin, out var vL))
                    valorAplicable = vL;

                if (!valorAplicable.HasValue) continue;

                exportRows.Add((bu, party, item, uom, valorAplicable.Value, start, end, pricingTermEnd));
            }
        }

        if (exportRows.Count == 0)
            return ArchivoProcesoResult.Fallo("No se pudieron generar filas exportables (sin detalles o sin artículos coincidentes).");

        static string Key4(string bu, string party, string item, string uom) =>
            $"{(bu ?? "").Trim().ToUpperInvariant()}|{(party ?? "").Trim().ToUpperInvariant()}|{(item ?? "").Trim().ToUpperInvariant()}|{(uom ?? "").Trim().ToUpperInvariant()}";

        exportRows = exportRows
            .GroupBy(r => Key4(r.BU, r.PartyCode, r.ItemNumber, r.Uom), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var itemsAll = exportRows.Select(r => T(r.ItemNumber)).Where(x => x != "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var buAll = exportRows.Select(r => T(r.BU)).Where(x => x != "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        static string KeyBUItem(string bu, string item) =>
    $"{(bu ?? "").Trim().ToUpperInvariant()}|{(item ?? "").Trim().ToUpperInvariant()}";

        static string KeyBUItemBucket(string bu, string item, string bucket) =>
            $"{(bu ?? "").Trim().ToUpperInvariant()}|{(item ?? "").Trim().ToUpperInvariant()}|{(bucket ?? "").Trim().ToUpperInvariant()}";

        var anyItemInXxora = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Primero valida XXORA_DISCOUNT_LIST por tipo
        foreach (var buRaw in buAll)
        {
            var bu = N(buRaw);

            foreach (var chunkRaw in ChunkList(itemsAll.Select(N), 700))
            {
                var chunk = chunkRaw
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (chunk.Count == 0)
                    continue;

                var q = _OracleContext.XXORA_DISCOUNT_LISTs
                    .AsNoTracking()
                    .Where(x =>
                        x.BU_NAME != null &&
                        x.ITEM_NUMBER != null &&
                        x.RULE_DISCOUNT_NAME != null)
                    .Where(x => x.BU_NAME.Trim().ToUpper() == bu)
                    .Where(x => chunk.Contains(x.ITEM_NUMBER.Trim().ToUpper()));

                if (tipo == "promocional")
                {
                    // Si la solicitud es PROMOCION, solo cuenta registros PROMOCION.
                    // Si existe como CLIENTE, no cuenta.
                    q = q.Where(x =>
                        x.RULE_DISCOUNT_NAME.Trim().ToUpper().Contains("PROMOC"));
                }
                else
                {
                    // Si la solicitud es CLIENTE/FIJO, solo cuenta registros CLIENTE.
                    // Si existe como PROMOCION, no cuenta.
                    q = q.Where(x =>
                        x.RULE_DISCOUNT_NAME.Trim().ToUpper().Contains("CLIENT"));
                }

                var rows = await q
                    .Select(x => new
                    {
                        x.BU_NAME,
                        x.ITEM_NUMBER
                    })
                    .Distinct()
                    .ToListAsync(ct);

                foreach (var it in rows)
                {
                    var item = N(it.ITEM_NUMBER);

                    if (item != "")
                        anyItemInXxora.Add(KeyBUItemBucket(bu, item, bucketName));
                }
            }
        }

        var anyItemInOtherProcessedRequest = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 2. Luego valida si ya se procesó en otra solicitud APROBADA y GENERADA del mismo tipo.
        // No importa si la fecha de solicitud es anterior, igual o posterior.
        // Si la actual es PROMOCION, solo busca promociones.
        // Si la actual es CLIENTE/FIJO, solo busca cliente/fijo.
        if (!esReversa)
        {
            foreach (var buRaw in buAll)
            {
                var bu = N(buRaw);

                foreach (var chunkRaw in ChunkList(itemsAll.Select(N), 700))
                {
                    var chunk = chunkRaw
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (chunk.Count == 0)
                        continue;

                    var predetQuery =
                        from h in _OracleContext.PREDESCUENTOs.AsNoTracking()
                        join d in _OracleContext.PREDETDESCUENTOs.AsNoTracking()
                            on new
                            {
                                BU = h.BU_NOMBRE,
                                Cliente = h.COD_CLIENTE,
                                Consecutivo = h.CONSECUTIVO
                            }
                            equals new
                            {
                                BU = d.BU_NOMBRE,
                                Cliente = d.COD_CLIENTE,
                                Consecutivo = d.CONSECUTIVO
                            }
                        where h.BU_NOMBRE != null
                           && h.TIPODESCUENTO != null
                           && h.GENERADO != null
                           && h.ESTADO != null
                           && h.CONSECUTIVO != null
                           && d.COD_ARTICULO != null
                           && h.BU_NOMBRE.Trim().ToUpper() == bu
                           && h.GENERADO.Trim().ToUpper() == "S"
                           && h.ESTADO.Trim().ToUpper() == "APROBADO"
                           && !consecSet.Contains(h.CONSECUTIVO)
                           && chunk.Contains(d.COD_ARTICULO.Trim().ToUpper())
                        select new
                        {
                            h.BU_NOMBRE,
                            h.TIPODESCUENTO,
                            d.COD_ARTICULO
                        };

                    if (tipo == "promocional")
                    {
                        // Si la solicitud actual es PROMOCION, solo valida promociones aprobadas/generadas.
                        predetQuery = predetQuery.Where(x =>
                            x.TIPODESCUENTO.Trim().ToUpper().Contains("PROMO"));
                    }
                    else
                    {
                        // Si la solicitud actual es CLIENTE/FIJO, solo valida cliente/fijo aprobadas/generadas.
                        predetQuery = predetQuery.Where(x =>
                            x.TIPODESCUENTO.Trim().ToUpper().Contains("CLIENT") ||
                            x.TIPODESCUENTO.Trim().ToUpper().Contains("FIJO") ||
                            x.TIPODESCUENTO.Trim().ToUpper().Contains("ACTIVO"));
                    }

                    var rowsPredet = await predetQuery
                        .Distinct()
                        .ToListAsync(ct);

                    foreach (var r in rowsPredet)
                    {
                        var item = N(r.COD_ARTICULO);

                        if (item != "")
                            anyItemInOtherProcessedRequest.Add(KeyBUItemBucket(bu, item, bucketName));
                    }
                }
            }
        }

        string MdAction(string bu, string item)
        {
            var key = KeyBUItemBucket(bu, item, bucketName);

            // 1. Primero valida XXORA_DISCOUNT_LIST por tipo
            if (anyItemInXxora.Contains(key))
                return ACTION_NOOP;

            // 2. Luego valida si ya se procesó en otra solicitud anterior del mismo tipo
            if (anyItemInOtherProcessedRequest.Contains(key))
                return ACTION_NOOP;

            // 3. Si no existe en ninguno, CREATE
            return ACTION_CREATE;
        }


        string DiscountListItemAction(string bu, string item)
        {
            return ACTION_NOOP;
        }

        string PricingTermAction(string bu, string item)
        {
            if (esReversa)
                return ACTION_NOOP;

            var key = KeyBUItemBucket(bu, item, bucketName);

            // 1. Primero valida XXORA_DISCOUNT_LIST por tipo
            if (anyItemInXxora.Contains(key))
                return ACTION_NOOP;

            // 2. Luego valida si ya se procesó en otra solicitud aprobada/generada del mismo tipo
            if (anyItemInOtherProcessedRequest.Contains(key))
                return ACTION_NOOP;

            // 3. Si no existe en ninguno, CREATE
            return ACTION_CREATE;
        }

        string MatrixDimensionsAction(string bu, string item)
        {
            if (esReversa)
                return ACTION_NOOP;

            return PricingTermAction(bu, item);
        }

        var existingMap = new Dictionary<string, (decimal Price, DateTime? Start, DateTime? End)>(StringComparer.OrdinalIgnoreCase);

        var groupsBuPartyKey = exportRows
            .Select(r => $"{N(r.BU)}|{N(r.PartyCode)}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var gk in groupsBuPartyKey)
        {
            var parts = gk.Split('|');
            var bu = parts.Length > 0 ? parts[0] : "";
            var party = parts.Length > 1 ? parts[1] : "";
            if (bu == "" || party == "") continue;

            var items = exportRows
                .Where(x => N(x.BU) == bu && N(x.PartyCode) == party)
                .Select(x => T(x.ItemNumber))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var uoms = exportRows
                .Where(x => N(x.BU) == bu && N(x.PartyCode) == party)
                .Select(x => T(x.Uom))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var loaded = await LoadXxoraMapFastAsync(
                bu,
                new[] { party },
                items,
                uoms,
                soloFijosEndNull: tipo == "fijo",
                ruleFilter: tipo == "promocional" ? "PROMOC" : "CLIENT",
                ct: ct);

            foreach (var kv in loaded)
            {
                var k = Key4(bu, party, kv.Key.ItemNumber, kv.Key.Uom);
                existingMap[k] = (kv.Value.DiscountValue ?? 0m, kv.Value.Start, kv.Value.End);
            }
        }

        string DecideRuleAction(string bu, string party, string item, string uom, decimal newPrice, DateTime? newStart, DateTime? newEnd)
        {
            var k = Key4(bu, party, item, uom);

            if (!existingMap.TryGetValue(k, out var old))
                return ACTION_CREATE;

            bool samePrice =
                decimal.Round(old.Price, 6) == decimal.Round(newPrice, 6);

            DateTime? oldStart = NormalizeDt(old.Start);
            DateTime? newStartNorm = NormalizeDt(newStart);

            DateTime? oldEnd = NormalizeDt(old.End);
            DateTime? newEndNorm = NormalizeDt(newEnd);

            if (tipo == "fijo" && !forzarVencimientoDiaAnterior)
            {
                bool sameStart = oldStart == newStartNorm;
                bool sameEnd = oldEnd == null && newEndNorm == null;

                return samePrice && sameStart && sameEnd
                    ? ACTION_NOOP
                    : ACTION_UPDATE;
            }

            bool sameDates =
                oldStart == newStartNorm &&
                oldEnd == newEndNorm;

            return samePrice && sameDates
                ? ACTION_NOOP
                : ACTION_UPDATE;
        }

        var clientesUnicos = exportRows
            .Select(r => T(r.PartyCode))
            .Where(x => x != "")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var nombreClienteMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (clientesUnicos.Count > 0)
        {
            var tmp = await _OracleContext.GEN_CLIENTEs
                .AsNoTracking()
                .Where(c => c.IDCLIENTE != null && clientesUnicos.Contains(c.IDCLIENTE))
                .Select(c => new { c.IDCLIENTE, c.NOMBRE_CLIENTE })
                .ToListAsync(ct);

            foreach (var x in tmp)
                if (!string.IsNullOrWhiteSpace(x.IDCLIENTE))
                    nombreClienteMap[T(x.IDCLIENTE)] = x.NOMBRE_CLIENTE ?? "";
        }

        using var wb = new XLWorkbook();

        var wsDL = wb.Worksheets.Add("DiscountListsInterface");
        wsDL.Cell(1, 1).Value = "*BATCH_NAME";
        wsDL.Cell(1, 2).Value = "*OPERATION_CODE";
        wsDL.Cell(1, 3).Value = "*SOURCE_DISCOUNT_LIST_ID";
        wsDL.Cell(1, 4).Value = "*NAME";
        wsDL.Cell(1, 5).Value = "DESCRIPTION";
        wsDL.Cell(1, 6).Value = "**BUSINESS_UNIT_ID";
        wsDL.Cell(1, 7).Value = "**BUSINESS_UNIT_NAME";
        wsDL.Cell(1, 8).Value = "*CURRENCY_CODE";
        wsDL.Cell(1, 9).Value = "*START_DATE";
        wsDL.Cell(1, 10).Value = "END_DATE";
        wsDL.Cell(1, 11).Value = "*STATUS_CODE";
        wsDL.Row(1).Style.Font.Bold = true;

        var wsItems = wb.Worksheets.Add("DiscountListItemsInterface");
        wsItems.Cell(1, 1).Value = "*OPERATION_CODE";
        wsItems.Cell(1, 2).Value = "*SOURCE_DISCOUNT_LIST_ID";
        wsItems.Cell(1, 3).Value = "*SOURCE_DISCOUNT_LIST_ITEM_ID";
        wsItems.Cell(1, 4).Value = "*ITEM_LEVEL_CODE";
        wsItems.Cell(1, 5).Value = "**ITEM_NUMBER";
        wsItems.Cell(1, 6).Value = "**ITEM_ID";
        wsItems.Cell(1, 7).Value = "**PRICING_UOM";
        wsItems.Cell(1, 8).Value = "**PRICING_UOM_CODE";
        wsItems.Cell(1, 9).Value = "*LINE_TYPE_CODE";
        wsItems.Row(1).Style.Font.Bold = true;

        var wsPT = wb.Worksheets.Add("PricingTermsInterface");
        wsPT.Cell(1, 1).Value = "*OPERATION_CODE";
        wsPT.Cell(1, 2).Value = "*SOURCE_ROOT_PARENT_ID";
        wsPT.Cell(1, 3).Value = "*SOURCE_PARENT_ID";
        wsPT.Cell(1, 4).Value = "*SOURCE_TERM_ID";
        wsPT.Cell(1, 5).Value = "*NAME";
        wsPT.Cell(1, 6).Value = "*PRICING_RULE_TYPE_CODE";
        wsPT.Cell(1, 7).Value = "*PRICE_TYPE_CODE";
        wsPT.Cell(1, 8).Value = "*CHARGE_TYPE_CODE";
        wsPT.Cell(1, 9).Value = "*CHARGE_SUBTYPE_CODE";
        wsPT.Cell(1, 10).Value = "**PRICE_PERIODICITY";
        wsPT.Cell(1, 11).Value = "**PRICE_PERIODICITY_CODE";
        wsPT.Cell(1, 12).Value = "ADJUSTMENT_TYPE_CODE";
        wsPT.Cell(1, 13).Value = "ADJUSTMENT_AMOUNT";
        wsPT.Cell(1, 14).Value = "**ADJUSTMENT_BASIS";
        wsPT.Cell(1, 15).Value = "**ADJUSTMENT_BASIS_ID";
        wsPT.Cell(1, 16).Value = "APPLY_TO_ROLLUP_FLAG";
        wsPT.Cell(1, 17).Value = "*START_DATE";
        wsPT.Cell(1, 18).Value = "END_DATE";
        wsPT.Row(1).Style.Font.Bold = true;

        var wsMD = wb.Worksheets.Add("MatrixDimensionsInterface");
        wsMD.Cell(1, 1).Value = "*OPERATION_CODE";
        wsMD.Cell(1, 2).Value = "*SOURCE_ROOT_PARENT_ID";
        wsMD.Cell(1, 3).Value = "*SOURCE_PARENT_ID";
        wsMD.Cell(1, 4).Value = "*SOURCE_MATRIX_ID";
        wsMD.Cell(1, 5).Value = "*DIMENSION_NAME";
        wsMD.Cell(1, 6).Value = "*DIMENSION_TYPE";
        wsMD.Cell(1, 7).Value = "*MAP_TO_RULE_COLUMN";
        wsMD.Row(1).Style.Font.Bold = true;

        var wsMR = wb.Worksheets.Add("MatrixRulesInterface");
        wsMR.Cell(1, 1).Value = "*OPERATION_CODE";
        wsMR.Cell(1, 2).Value = "*SOURCE_ROOT_PARENT_ID";
        wsMR.Cell(1, 3).Value = "*SOURCE_MATRIX_ID";
        wsMR.Cell(1, 4).Value = "*SOURCE_RULE_ID";
        wsMR.Cell(1, 5).Value = "VALUE_STRING1";
        wsMR.Cell(1, 6).Value = "VALUE_STRING2";
        wsMR.Cell(1, 7).Value = "VALUE_STRING3";
        wsMR.Cell(1, 8).Value = "VALUE_STRING4";
        wsMR.Row(1).Style.Font.Bold = true;

        var minStart = exportRows.Select(r => r.Start).Where(x => x.HasValue).Min() ?? DateTime.Today;
        var maxEnd = exportRows.Select(r => r.End).Where(x => x.HasValue).Max();

        int rDL = 2;
        wsDL.Cell(rDL, 1).Value = batchName;
        wsDL.Cell(rDL, 2).Value = ACTION_NOOP;
        wsDL.Cell(rDL, 3).Value = sourceDiscountListId;
        wsDL.Cell(rDL, 4).Value = name;
        wsDL.Cell(rDL, 5).Value = description;
        wsDL.Cell(rDL, 6).Value = businessUnitId;
        wsDL.Cell(rDL, 7).Value = businessUnitName;
        wsDL.Cell(rDL, 8).Value = currencyCode;
        wsDL.Cell(rDL, 9).Value = FormatDateDiscountListsLocal(minStart);
        wsDL.Cell(rDL, 10).Value = !esReversa && tipo == "promocional"
            ? FormatDateLocal(maxEnd)
            : "";
        wsDL.Cell(rDL, 11).Value = statusCode;

        var itemRows = exportRows
            .GroupBy(r => new { BU = T(r.BU), Item = T(r.ItemNumber), Uom = T(r.Uom) })
            .Select(g => g.First())
            .OrderBy(x => x.ItemNumber)
            .ToList();

        int rItems = 2;
        int rPT = 2;
        int rMD = 2;
        int rMR = 2;

        foreach (var row in itemRows)
        {
            var bu = T(row.BU);
            var item = T(row.ItemNumber);
            var uom = T(row.Uom);
            var bucket = bucketName;

            var sdlid = $"SDLID_{item}";
            var stid = string.Equals(bucket, "PROMOCION", StringComparison.OrdinalIgnoreCase)
                ? $"STID_SDLID_{item}_PROMOCION"
                : $"STID_SDLID_{item}";
            var smid = $"SMID_{stid}";
            var itemOp = DiscountListItemAction(bu, item);
            var ptOp = PricingTermAction(bu, item);
            var mdOp = MatrixDimensionsAction(bu, item);

            wsItems.Cell(rItems, 1).Value = itemOp;
            wsItems.Cell(rItems, 2).Value = sourceDiscountListId;
            wsItems.Cell(rItems, 3).Value = sdlid;
            wsItems.Cell(rItems, 4).Value = "ITEM";
            wsItems.Cell(rItems, 5).Value = item;
            wsItems.Cell(rItems, 6).Value = "";
            wsItems.Cell(rItems, 7).Value = "";
            wsItems.Cell(rItems, 8).Value = uom;
            wsItems.Cell(rItems, 9).Value = "ORA_BUY";
            rItems++;

            wsPT.Cell(rPT, 1).Value = ptOp;
            wsPT.Cell(rPT, 2).Value = sourceDiscountListId;
            wsPT.Cell(rPT, 3).Value = sdlid;
            wsPT.Cell(rPT, 4).Value = stid;
            wsPT.Cell(rPT, 5).Value = bucket;
            wsPT.Cell(rPT, 6).Value = "ATTRIBUTE_PRICING";
            wsPT.Cell(rPT, 7).Value = "ALL";
            wsPT.Cell(rPT, 8).Value = "ORA_SALE";
            wsPT.Cell(rPT, 9).Value = "ORA_PRICE";
            wsPT.Cell(rPT, 10).Value = "";
            wsPT.Cell(rPT, 11).Value = "ALL";
            wsPT.Cell(rPT, 12).Value = "";
            wsPT.Cell(rPT, 13).Value = "";
            wsPT.Cell(rPT, 14).Value = "";
            wsPT.Cell(rPT, 15).Value = "";
            wsPT.Cell(rPT, 16).Value = "";
            wsPT.Cell(rPT, 17).Value = FormatDateLocal(row.Start);
            wsPT.Cell(rPT, 18).Value = FormatDateLocal(row.PricingTermEnd);
            rPT++;

            var dims = new (string Name, string Type, string Column)[]
            {
                ("Adjustment Amount", "Result", "VALUE_STRING3"),
                ("Adjustment Basis", "Result", "VALUE_STRING4"),
                ("Adjustment Type", "Result", "VALUE_STRING2"),
                (bucket, "Condition", "VALUE_STRING1")
            };

            foreach (var d in dims)
            {
                wsMD.Cell(rMD, 1).Value = mdOp;
                wsMD.Cell(rMD, 2).Value = sourceDiscountListId;
                wsMD.Cell(rMD, 3).Value = stid;
                wsMD.Cell(rMD, 4).Value = smid;
                wsMD.Cell(rMD, 5).Value = d.Name;
                wsMD.Cell(rMD, 6).Value = d.Type;
                wsMD.Cell(rMD, 7).Value = d.Column;
                rMD++;
            }

            var ruleRows = exportRows
                .Where(x => T(x.BU) == bu && T(x.ItemNumber) == item && T(x.Uom) == uom)
                .OrderBy(x => x.PartyCode)
                .ToList();

            foreach (var rr in ruleRows)
            {
                var partyCode = T(rr.PartyCode);
                var mrAction = esReversa
                    ? ACTION_UPDATE
                    : ptOp == ACTION_CREATE
                        ? ACTION_CREATE
                        : DecideRuleAction(bu, partyCode, item, uom, rr.Valor, rr.Start, rr.End);
                var srid = BuildSridMr(item, partyCode, bucket);

                wsMR.Cell(rMR, 1).Value = mrAction;
                wsMR.Cell(rMR, 2).Value = sourceDiscountListId;
                wsMR.Cell(rMR, 3).Value = smid;
                wsMR.Cell(rMR, 4).Value = srid;
                wsMR.Cell(rMR, 5).Value = partyCode;
                wsMR.Cell(rMR, 6).Value = "DISCOUNT_PERCENT";
                wsMR.Cell(rMR, 7).Value =
                    rr.Valor.ToString(
                        "0.############################",
                        CultureInfo.InvariantCulture);
                wsMR.Cell(rMR, 8).Value = "Adjustment Basis";
                wsMR.Cell(rMR, 15).Value = FormatDateLocal(rr.Start);
                wsMR.Cell(rMR, 16).Value = esReversa
                    ? FormatDateLocal(fechaFinReversa)
                    : FormatDateLocal(rr.End);
                rMR++;
            }
        }

        if (rMR == 2)
            return ArchivoProcesoResult.Fallo("No se generaron reglas (MatrixRulesInterface) para exportar.");

        if (marcarComoGenerado)
        {
            await using var trx = await _OracleContext.Database.BeginTransactionAsync(ct);
            try
            {
                foreach (var h in headers)
                {
                    var db = await _OracleContext.PREDESCUENTOs
                        .FirstOrDefaultAsync(p =>
                            p.BU_NOMBRE == h.BU_NOMBRE &&
                            p.COD_CLIENTE == h.COD_CLIENTE &&
                            p.CONSECUTIVO == h.CONSECUTIVO, ct);

                    if (db != null)
                    {
                        db.GENERADO = "S";
                        _OracleContext.PREDESCUENTOs.Update(db);
                    }
                }

                await _OracleContext.SaveChangesAsync(ct);
                await trx.CommitAsync(ct);
            }
            catch
            {
                await trx.RollbackAsync(ct);
                throw;
            }
        }

        var sheets = new[]
        {
            "DiscountListsInterface",
            "DiscountListItemsInterface",
            "PricingTermsInterface",
            "MatrixDimensionsInterface",
            "MatrixRulesInterface"
        };

        var zipBytes = ZipWorksheetsAsCsv(wb, sheets);

        return ArchivoProcesoResult.Exito(zipBytes, nombreArchivo);
    }

    private sealed class XxoraSnap
    {
        public DateTime? Start { get; init; }
        public DateTime? End { get; init; }
        public decimal? DiscountValue { get; init; }
    }

    public static string NormalizeTipo(string? s)
    {
        s = (s ?? "").Trim().ToLowerInvariant();

        if (s.Contains("promo"))
            return "promocional";

        if (s.Contains("fijo") || s.Contains("activo") || s.Contains("client"))
            return "fijo";

        return "";
    }

    private static DateTime? NormalizeDt(DateTime? dt)
    {
        if (dt == null) return null;
        var v = dt.Value;
        return new DateTime(v.Year, v.Month, v.Day, v.Hour, v.Minute, v.Second);
    }

    private static decimal? NormalizeDec(decimal? d)
    {
        if (d == null) return null;
        return decimal.Round(d.Value, 6);
    }

    private static string CalcAction(XxoraSnap? existing, DateTime start, DateTime? end, decimal discountValue)
    {
        if (existing == null) return "CREATE";

        var s1 = NormalizeDt(existing.Start);
        var e1 = NormalizeDt(existing.End);
        var s2 = NormalizeDt(start);
        var e2 = NormalizeDt(end);
        var v1 = NormalizeDec(existing.DiscountValue);
        var v2 = NormalizeDec(discountValue);

        bool same = s1 == s2 && e1 == e2 && v1 == v2;
        return same ? "NO-OP" : "UPDATE";
    }

    private static IEnumerable<List<T>> ChunkList<T>(IEnumerable<T> src, int size)
    {
        var bucket = new List<T>(size);
        foreach (var x in src)
        {
            bucket.Add(x);
            if (bucket.Count == size)
            {
                yield return bucket;
                bucket = new List<T>(size);
            }
        }
        if (bucket.Count > 0) yield return bucket;
    }

    private async Task<Dictionary<XxoraKey, XxoraSnap>> LoadXxoraMapFastAsync(
        string buName,
        IEnumerable<string> partyNumbers,
        IEnumerable<string> itemNumbers,
        IEnumerable<string> uoms,
        bool soloFijosEndNull,
        string? ruleFilter = null,
        CancellationToken ct = default)
    {
        static string Norm(string? s) => (s ?? "").Trim().ToUpperInvariant();

        var buNorm = Norm(buName);

        var partyList = partyNumbers.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Norm).Distinct().ToList();
        var itemList = itemNumbers.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Norm).Distinct().ToList();
        var uomList = uoms.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Norm).Distinct().ToList();

        var result = new Dictionary<XxoraKey, XxoraSnap>();

        foreach (var partyChunk in ChunkList(partyList, 900))
            foreach (var itemChunk in ChunkList(itemList, 900))
            {
                var q = _OracleContext.XXORA_DISCOUNT_LISTs
                    .AsNoTracking()
                    .Where(x => x.BU_NAME != null && x.BU_NAME.Trim().ToUpper() == buNorm)
                    .Where(x => x.PARTY_NUMBER != null && partyChunk.Contains(x.PARTY_NUMBER.Trim().ToUpper()))
                    .Where(x => x.ITEM_NUMBER != null && itemChunk.Contains(x.ITEM_NUMBER.Trim().ToUpper()));

                if (!string.IsNullOrWhiteSpace(ruleFilter))
                {
                    var ruleNorm = ruleFilter.Trim().ToUpperInvariant();

                    q = q.Where(x =>
                        x.RULE_DISCOUNT_NAME != null &&
                        x.RULE_DISCOUNT_NAME.Trim().ToUpper().Contains(ruleNorm));
                }

                q = soloFijosEndNull
                    ? q.Where(x => x.END_DATE == null)
                    : q.Where(x => x.END_DATE != null);

                if (uomList.Count > 0)
                {
                    q = q.Where(x => x.PRICING_UOM_CODE != null &&
                                     uomList.Contains(x.PRICING_UOM_CODE.Trim().ToUpper()));
                }

                var rows = await q.Select(x => new
                {
                    Party = x.PARTY_NUMBER,
                    Item = x.ITEM_NUMBER,
                    Uom = x.PRICING_UOM_CODE,
                    Start = x.START_DATE,
                    End = x.END_DATE,
                    Disc = x.DISCOUNT_PRICE
                }).ToListAsync(ct);

                foreach (var r in rows)
                {
                    var party = Norm(r.Party);
                    var item = Norm(r.Item);
                    var uom = Norm(r.Uom);

                    if (party == "" || item == "" || uom == "") continue;

                    var key = new XxoraKey(party, item, uom);

                    if (!result.TryGetValue(key, out var old))
                    {
                        result[key] = new XxoraSnap
                        {
                            Start = r.Start,
                            End = r.End,
                            DiscountValue = r.Disc
                        };
                        continue;
                    }

                    DateTime? oldStart = old.Start;
                    DateTime? newStart = r.Start;

                    if ((oldStart ?? DateTime.MinValue) < (newStart ?? DateTime.MinValue))
                    {
                        result[key] = new XxoraSnap
                        {
                            Start = r.Start,
                            End = r.End,
                            DiscountValue = r.Disc
                        };
                    }
                }
            }

        return result;
    }

    private static string FormatDateDiscountListsLocal(DateTime? fecha) =>
        fecha.HasValue
            ? fecha.Value.Date.ToString("yyyy/MM/dd HH:mm:ss")
            : string.Empty;

    private static byte[] ZipWorksheetsAsCsv(XLWorkbook wb, IEnumerable<string> worksheetNames)
    {
        var suffixBySheet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DiscountListItemsInterface"] = ",END",
            ["DiscountListSetsInterface"] = ",END",
            ["DiscountListsInterface"] = ",END",
            ["MatrixDimensionsInterface"] = ",END",
            ["MatrixRulesInterface"] = ",END",
            ["PricingTermsInterface"] = ",END",
        };

        using var outMs = new MemoryStream();
        using (var zip = new ZipArchive(outMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var sheetName in worksheetNames)
            {
                var ws = wb.Worksheet(sheetName);
                var entry = zip.CreateEntry($"{sheetName}.csv", CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream, new UTF8Encoding(false));

                var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
                var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

                var minColsBySheet = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["DiscountListsInterface"] = 52,
                    ["DiscountListItemsInterface"] = 53,
                    ["PricingTermsInterface"] = 59,
                    ["MatrixDimensionsInterface"] = 7,
                    ["MatrixRulesInterface"] = 31
                };

                if (minColsBySheet.TryGetValue(sheetName, out var minCols))
                    lastCol = Math.Max(lastCol, minCols);

                if (lastRow < 2 || lastCol < 1)
                    continue;

                for (int r = 2; r <= lastRow; r++)
                {
                    var fields = new string[lastCol];
                    bool hasAny = false;

                    for (int c = 1; c <= lastCol; c++)
                    {
                        var raw = GetCellValueInvariant(ws.Cell(r, c));

                        if (!string.IsNullOrEmpty(raw)) hasAny = true;
                        fields[c - 1] = CsvEscape(raw);
                    }

                    if (!hasAny) continue;

                    var line = string.Join(",", fields);

                    if (suffixBySheet.TryGetValue(sheetName, out var suffix))
                        line += suffix;

                    writer.WriteLine(line);
                }
            }
        }

        return outMs.ToArray();
    }

    private static string CsvEscape(string s)
    {
        bool mustQuote = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
        if (!mustQuote) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    private static byte[] WorksheetToCsvUtf8(IXLWorksheet ws)
    {
        var range = ws.RangeUsed();
        if (range == null)
            return Array.Empty<byte>();

        int firstRow = range.FirstRowUsed().RowNumber();
        int lastRow = range.LastRowUsed().RowNumber();
        int firstCol = range.FirstColumnUsed().ColumnNumber();
        int lastCol = range.LastColumnUsed().ColumnNumber();

        using var ms = new MemoryStream();
        using var sw = new StreamWriter(ms, new UTF8Encoding(false), 1024, leaveOpen: true);

        for (int r = firstRow; r <= lastRow; r++)
        {
            var values = new string[lastCol - firstCol + 1];
            for (int c = firstCol; c <= lastCol; c++)
                values[c - firstCol] = GetCellCsvValue(ws.Cell(r, c));

            sw.WriteLine(string.Join(",", values));
        }

        sw.Flush();
        return ms.ToArray();
    }

    private static string GetCellCsvValue(IXLCell cell)
    {
        var raw = cell.GetFormattedString() ?? "";
        return EscapeCsv(raw);
    }

    private static string EscapeCsv(string input)
    {
        if (input.Contains('"'))
            input = input.Replace("\"", "\"\"");

        if (input.Contains(',') || input.Contains('\n') || input.Contains('\r') || input.Contains('"'))
            return $"\"{input}\"";

        return input;
    }

    private async Task<byte[]> GenerarZipUpdateDesdeXxoraAsync(
        string bu,
        string itemNumber,
        DateTime startDate,
        DateTime? endDate,
        decimal descuento,
        CancellationToken ct = default)
    {
        // PEGÁ AQUÍ EL CUERPO ACTUAL DE TU MÉTODO,
        // solo agregando el parámetro CancellationToken en los ToListAsync/SaveChangesAsync/CommitAsync.
        throw new NotImplementedException();
    }

    private async Task<byte[]> GenerarZipReactivarDesdeArtDetAsync(
        string bu,
        string org,
        string itemNumber,
        DateTime startDate,
        DateTime? endDate,
        decimal descuento,
        CancellationToken ct = default)
    {
        // PEGÁ AQUÍ EL CUERPO ACTUAL DE TU MÉTODO,
        // solo agregando el parámetro CancellationToken en los ToListAsync.
        throw new NotImplementedException();
    }

    public async Task<ArchivoProcesoResult> GenerarNoPromoPendienteAsync(
    string bu,
    string org,
    string itemNumber,
    bool marcarComoGenerado = false,
    CancellationToken ct = default)
    {
        static string T(string? s) => (s ?? "").Trim();
        static string N(string? s) => (s ?? "").Trim().ToUpperInvariant();

        static bool Eq(string? a, string b) =>
            string.Equals((a ?? "").Trim(), b, StringComparison.OrdinalIgnoreCase);

        static string FormatDateLocal(DateTime? fecha) =>
            fecha.HasValue
                ? fecha.Value.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture)
                : string.Empty;

        static string SafeId(string? raw, int maxLen = 40)
        {
            var s = (raw ?? "").Trim();
            if (string.IsNullOrEmpty(s)) return "X";

            var sb = new StringBuilder(s.Length);

            foreach (var ch in s)
                sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');

            var outp = sb.ToString();

            while (outp.Contains("__"))
                outp = outp.Replace("__", "_");

            if (outp.Length > maxLen)
                outp = outp[..maxLen];

            return outp.Trim('_');
        }

        static string RuleBucketFromRule(string? rule)
        {
            var x = N(rule);

            if (x.Contains("PROMOC"))
                return "PROMOCION";

            if (x.Contains("CLIENT") || x.Contains("FIJO"))
                return "CLIENTE";

            return "";
        }

        string BuildSridMr(string itemKey, string partyCode, string bucket)
        {
            var partyId = SafeId(partyCode, 30);

            if (Eq(bucket, "PROMOCION"))
                return $"SRID_SMID_STID_SDLID{itemKey}_PROMOCION_{partyId}";

            return $"SRID_SMID_STID_SDLID{itemKey}_{partyId}";
        }

        const string ACTION_CREATE = "CREATE";
        const string ACTION_UPDATE = "UPDATE";
        const string ACTION_NOOP = "NO-OP";

        const string sourceDiscountListId = "LP_001CR_CL";
        const string name = "Descuentos_CostaRica";
        const string description = "Descuentos_CostaRica";
        const string businessUnitId = "";
        const string businessUnitName = "LANCO_CR";
        const string currencyCode = "CRC";
        const string statusCode = "APPROVED";

        bu = T(bu);
        org = T(org);
        itemNumber = T(itemNumber);

        if (string.IsNullOrWhiteSpace(bu))
            return ArchivoProcesoResult.Fallo("Falta BU_NAME.");

        if (string.IsNullOrWhiteSpace(org))
            return ArchivoProcesoResult.Fallo("Falta ORGANIZATION_CODE.");

        if (string.IsNullOrWhiteSpace(itemNumber))
            return ArchivoProcesoResult.Fallo("Falta ITEM_NUMBER.");

        var buKey = N(bu);
        var orgKey = N(org);
        var itemKey = N(itemNumber);

        var header = await _OracleContext.ART_NO_PROMOs
             .AsNoTracking()
             .FirstOrDefaultAsync(a =>
         a.BU_NAME != null &&
         a.ORGANIZATION_CODE != null &&
         a.ITEM_NUMBER != null &&
         a.ESTADO != null &&
         a.GENERADO != null &&
         a.BU_NAME.Trim().ToUpper() == buKey &&
         a.ORGANIZATION_CODE.Trim().ToUpper() == orgKey &&
         a.ITEM_NUMBER.Trim().ToUpper() == itemKey &&
         a.GENERADO.Trim().ToUpper() == "N" &&
         (
             a.ESTADO.Trim().ToUpper() == "ACTIVO" ||
             a.ESTADO.Trim().ToUpper() == "INACTIVO" ||
             a.ESTADO.Trim().ToUpper() == "NUEVO"
         ),
         ct);

        if (header == null)
        {
            return ArchivoProcesoResult.Fallo(
                $"No hay ART_NO_PROMO Activo/Inactivo/Nuevo y GENERADO='N' para BU={buKey}, ORG={orgKey}, ITEM={itemKey}.");
        }

        var estadoHeader = N(header.ESTADO);
        var esReactivacion = Eq(estadoHeader, "INACTIVO");
        var esNuevo = Eq(estadoHeader, "NUEVO");

        var hoy = DateTime.Today;
        var fechaFinNoPromo = hoy.AddSeconds(-1);

        var fallbackUom = await _OracleContext.INV_ARTICULOs
            .AsNoTracking()
            .Where(i =>
                i.COD_ARTICULO != null &&
                i.COD_ARTICULO.Trim().ToUpper() == itemKey)
            .Select(i => i.MEDIDA)
            .FirstOrDefaultAsync(ct);

        var fallbackUomKey = T(fallbackUom);

        var detallesQuery = _OracleContext.ART_DET_NO_PROMOs
            .AsNoTracking()
            .Where(d =>
                d.BU_NAME != null &&
                d.ORGANIZATION_CODE != null &&
                d.ITEM_NUMBER != null &&
                d.PARTY_NUMBER != null &&
                d.RULE_DISCOUNT_NAME != null &&
                d.BU_NAME.Trim().ToUpper() == buKey &&
                d.ORGANIZATION_CODE.Trim().ToUpper() == orgKey &&
                d.ITEM_NUMBER.Trim().ToUpper() == itemKey);

        // ACTIVO = desactivar: solo toma descuentos vigentes.
        // INACTIVO = reactivar: conserva los datos guardados.
        // NUEVO = crear: conserva exactamente las fechas de ART_DET_NO_PROMO.
        if (!esReactivacion && !esNuevo)
        {
            detallesQuery = detallesQuery.Where(d =>
                d.END_DATE == null || d.END_DATE >= hoy);
        }

        var detallesDb = await detallesQuery.ToListAsync(ct);

        var exportRows = detallesDb
            .Select(d => new
            {
                BU = buKey,
                PartyCode = T(d.PARTY_NUMBER),
                ItemNumber = itemKey,
                Uom = string.IsNullOrWhiteSpace(T(d.PRICING_UOM_CODE))
                    ? fallbackUomKey
                    : T(d.PRICING_UOM_CODE),
                Valor = d.DISCOUNT_PRICE,
                Start = (DateTime?)d.START_DATE,
                End = esNuevo || esReactivacion
                    ? (DateTime?)d.END_DATE
                    : (DateTime?)fechaFinNoPromo,
                Bucket = RuleBucketFromRule(d.RULE_DISCOUNT_NAME)
            })
            .Where(r =>
                !string.IsNullOrWhiteSpace(r.PartyCode) &&
                !string.IsNullOrWhiteSpace(r.Uom) &&
                !string.IsNullOrWhiteSpace(r.Bucket))
            .GroupBy(r =>
                $"{N(r.BU)}|{N(r.Bucket)}|{N(r.PartyCode)}|{N(r.ItemNumber)}|{N(r.Uom)}",
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(x => x.Start ?? DateTime.MinValue)
                .First())
            .ToList();

        if (exportRows.Count == 0)
            return ArchivoProcesoResult.Fallo($"No se pudieron generar filas para ART_NO_PROMO ITEM={itemKey}. Revise RULE_DISCOUNT_NAME, PARTY_NUMBER o PRICING_UOM_CODE.");

        var fechaNombreArchivo = DateTime.Today.ToString(
            "ddMMyy",
            CultureInfo.InvariantCulture);

        var nombreArchivo = esNuevo
            ? $"Descuentos_COSTARICA_ALL_NUEVO_{SafeFileToken(itemKey)}_{fechaNombreArchivo}.zip"
            : esReactivacion
                ? $"Descuentos_COSTARICA_ALL_REACTIVAR_{SafeFileToken(itemKey)}_{fechaNombreArchivo}.zip"
                : $"Descuentos_COSTARICA_ALL_DESACTIVAR_{SafeFileToken(itemKey)}_{fechaNombreArchivo}.zip";

        var batchName = Path.GetFileNameWithoutExtension(nombreArchivo);

        var clientesUnicos = exportRows
            .Select(r => T(r.PartyCode))
            .Where(x => x != "")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var nombreClienteMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (clientesUnicos.Count > 0)
        {
            var clientes = await _OracleContext.GEN_CLIENTEs
                .AsNoTracking()
                .Where(c => c.IDCLIENTE != null && clientesUnicos.Contains(c.IDCLIENTE))
                .Select(c => new { c.IDCLIENTE, c.NOMBRE_CLIENTE })
                .ToListAsync(ct);

            foreach (var c in clientes)
            {
                var id = T(c.IDCLIENTE);

                if (!string.IsNullOrWhiteSpace(id))
                    nombreClienteMap[id] = T(c.NOMBRE_CLIENTE);
            }
        }

        using var wb = new XLWorkbook();

        var wsDL = wb.Worksheets.Add("DiscountListsInterface");
        wsDL.Cell(1, 1).Value = "*BATCH_NAME";
        wsDL.Cell(1, 2).Value = "*OPERATION_CODE";
        wsDL.Cell(1, 3).Value = "*SOURCE_DISCOUNT_LIST_ID";
        wsDL.Cell(1, 4).Value = "*NAME";
        wsDL.Cell(1, 5).Value = "DESCRIPTION";
        wsDL.Cell(1, 6).Value = "**BUSINESS_UNIT_ID";
        wsDL.Cell(1, 7).Value = "**BUSINESS_UNIT_NAME";
        wsDL.Cell(1, 8).Value = "*CURRENCY_CODE";
        wsDL.Cell(1, 9).Value = "*START_DATE";
        wsDL.Cell(1, 10).Value = "END_DATE";
        wsDL.Cell(1, 11).Value = "*STATUS_CODE";
        wsDL.Row(1).Style.Font.Bold = true;

        var wsItems = wb.Worksheets.Add("DiscountListItemsInterface");
        wsItems.Cell(1, 1).Value = "*OPERATION_CODE";
        wsItems.Cell(1, 2).Value = "*SOURCE_DISCOUNT_LIST_ID";
        wsItems.Cell(1, 3).Value = "*SOURCE_DISCOUNT_LIST_ITEM_ID";
        wsItems.Cell(1, 4).Value = "*ITEM_LEVEL_CODE";
        wsItems.Cell(1, 5).Value = "**ITEM_NUMBER";
        wsItems.Cell(1, 6).Value = "**ITEM_ID";
        wsItems.Cell(1, 7).Value = "**PRICING_UOM";
        wsItems.Cell(1, 8).Value = "**PRICING_UOM_CODE";
        wsItems.Cell(1, 9).Value = "*LINE_TYPE_CODE";
        wsItems.Row(1).Style.Font.Bold = true;

        var wsPT = wb.Worksheets.Add("PricingTermsInterface");
        wsPT.Cell(1, 1).Value = "*OPERATION_CODE";
        wsPT.Cell(1, 2).Value = "*SOURCE_ROOT_PARENT_ID";
        wsPT.Cell(1, 3).Value = "*SOURCE_PARENT_ID";
        wsPT.Cell(1, 4).Value = "*SOURCE_TERM_ID";
        wsPT.Cell(1, 5).Value = "*NAME";
        wsPT.Cell(1, 6).Value = "*PRICING_RULE_TYPE_CODE";
        wsPT.Cell(1, 7).Value = "*PRICE_TYPE_CODE";
        wsPT.Cell(1, 8).Value = "*CHARGE_TYPE_CODE";
        wsPT.Cell(1, 9).Value = "*CHARGE_SUBTYPE_CODE";
        wsPT.Cell(1, 10).Value = "**PRICE_PERIODICITY";
        wsPT.Cell(1, 11).Value = "**PRICE_PERIODICITY_CODE";
        wsPT.Cell(1, 12).Value = "ADJUSTMENT_TYPE_CODE";
        wsPT.Cell(1, 13).Value = "ADJUSTMENT_AMOUNT";
        wsPT.Cell(1, 14).Value = "**ADJUSTMENT_BASIS";
        wsPT.Cell(1, 15).Value = "**ADJUSTMENT_BASIS_ID";
        wsPT.Cell(1, 16).Value = "APPLY_TO_ROLLUP_FLAG";
        wsPT.Cell(1, 17).Value = "*START_DATE";
        wsPT.Cell(1, 18).Value = "END_DATE";
        wsPT.Row(1).Style.Font.Bold = true;

        var wsMD = wb.Worksheets.Add("MatrixDimensionsInterface");
        wsMD.Cell(1, 1).Value = "*OPERATION_CODE";
        wsMD.Cell(1, 2).Value = "*SOURCE_ROOT_PARENT_ID";
        wsMD.Cell(1, 3).Value = "*SOURCE_PARENT_ID";
        wsMD.Cell(1, 4).Value = "*SOURCE_MATRIX_ID";
        wsMD.Cell(1, 5).Value = "*DIMENSION_NAME";
        wsMD.Cell(1, 6).Value = "*DIMENSION_TYPE";
        wsMD.Cell(1, 7).Value = "*MAP_TO_RULE_COLUMN";
        wsMD.Row(1).Style.Font.Bold = true;

        var wsMR = wb.Worksheets.Add("MatrixRulesInterface");
        wsMR.Cell(1, 1).Value = "*OPERATION_CODE";
        wsMR.Cell(1, 2).Value = "*SOURCE_ROOT_PARENT_ID";
        wsMR.Cell(1, 3).Value = "*SOURCE_MATRIX_ID";
        wsMR.Cell(1, 4).Value = "*SOURCE_RULE_ID";
        wsMR.Cell(1, 5).Value = "VALUE_STRING1";
        wsMR.Cell(1, 6).Value = "VALUE_STRING2";
        wsMR.Cell(1, 7).Value = "VALUE_STRING3";
        wsMR.Cell(1, 8).Value = "VALUE_STRING4";
        wsMR.Row(1).Style.Font.Bold = true;

        var minStart = exportRows
            .Select(r => r.Start)
            .Where(x => x.HasValue)
            .Min() ?? hoy;

        int rDL = 2;
        wsDL.Cell(rDL, 1).Value = batchName;
        wsDL.Cell(rDL, 2).Value = ACTION_NOOP;
        wsDL.Cell(rDL, 3).Value = sourceDiscountListId;
        wsDL.Cell(rDL, 4).Value = name;
        wsDL.Cell(rDL, 5).Value = description;
        wsDL.Cell(rDL, 6).Value = businessUnitId;
        wsDL.Cell(rDL, 7).Value = businessUnitName;
        wsDL.Cell(rDL, 8).Value = currencyCode;
        wsDL.Cell(rDL, 9).Value = FormatDateDiscountListsLocal(minStart);
        wsDL.Cell(rDL, 10).Value = "";
        wsDL.Cell(rDL, 11).Value = statusCode;

        var itemRows = exportRows
            .GroupBy(r => new { r.ItemNumber, r.Uom })
            .Select(g => g.First())
            .OrderBy(x => x.ItemNumber)
            .ToList();

        int rItems = 2;

        foreach (var row in itemRows)
        {
            var sdlid = $"SDLID_{row.ItemNumber}";

            wsItems.Cell(rItems, 1).Value = ACTION_NOOP;
            wsItems.Cell(rItems, 2).Value = sourceDiscountListId;
            wsItems.Cell(rItems, 3).Value = sdlid;
            wsItems.Cell(rItems, 4).Value = "ITEM";
            wsItems.Cell(rItems, 5).Value = row.ItemNumber;
            wsItems.Cell(rItems, 6).Value = "";
            wsItems.Cell(rItems, 7).Value = "";
            wsItems.Cell(rItems, 8).Value = row.Uom;
            wsItems.Cell(rItems, 9).Value = "ORA_BUY";

            rItems++;
        }

        int rPT = 2;
        int rMD = 2;
        int rMR = 2;

        var termRows = exportRows
            .GroupBy(r => new { r.ItemNumber, r.Uom, r.Bucket })
            .Select(g => new
            {
                g.Key.ItemNumber,
                g.Key.Uom,
                g.Key.Bucket,
                Start = g.Select(x => x.Start).Where(x => x.HasValue).Min() ?? hoy,
                Rules = g.OrderBy(x => x.PartyCode).ToList()
            })
            .OrderBy(x => x.ItemNumber)
            .ThenBy(x => x.Bucket)
            .ToList();

        foreach (var term in termRows)
        {
            var item = term.ItemNumber;
            var bucket = term.Bucket;

            var sdlid = $"SDLID_{item}";

            var stid = Eq(bucket, "PROMOCION")
                ? $"STID_SDLID_{item}_PROMOCION"
                : $"STID_SDLID_{item}";

            var smid = $"SMID_{stid}";

            wsPT.Cell(rPT, 1).Value = esNuevo
                ? ACTION_CREATE
                : ACTION_NOOP;
            wsPT.Cell(rPT, 2).Value = sourceDiscountListId;
            wsPT.Cell(rPT, 3).Value = sdlid;
            wsPT.Cell(rPT, 4).Value = stid;
            wsPT.Cell(rPT, 5).Value = bucket;
            wsPT.Cell(rPT, 6).Value = "ATTRIBUTE_PRICING";
            wsPT.Cell(rPT, 7).Value = "ALL";
            wsPT.Cell(rPT, 8).Value = "ORA_SALE";
            wsPT.Cell(rPT, 9).Value = "ORA_PRICE";
            wsPT.Cell(rPT, 10).Value = "";
            wsPT.Cell(rPT, 11).Value = "ALL";
            wsPT.Cell(rPT, 12).Value = "";
            wsPT.Cell(rPT, 13).Value = "";
            wsPT.Cell(rPT, 14).Value = "";
            wsPT.Cell(rPT, 15).Value = "";
            wsPT.Cell(rPT, 16).Value = "";
            wsPT.Cell(rPT, 17).Value = FormatDateLocal(term.Start);
            wsPT.Cell(rPT, 18).Value = "";
            rPT++;

            var dims = new (string Name, string Type, string Column)[]
            {
            ("Adjustment Amount", "Result", "VALUE_STRING3"),
            ("Adjustment Basis", "Result", "VALUE_STRING4"),
            ("Adjustment Type", "Result", "VALUE_STRING2"),
            (bucket, "Condition", "VALUE_STRING1")
            };

            foreach (var d in dims)
            {
                wsMD.Cell(rMD, 1).Value = esNuevo
                    ? ACTION_CREATE
                    : ACTION_NOOP;
                wsMD.Cell(rMD, 2).Value = sourceDiscountListId;
                wsMD.Cell(rMD, 3).Value = stid;
                wsMD.Cell(rMD, 4).Value = smid;
                wsMD.Cell(rMD, 5).Value = d.Name;
                wsMD.Cell(rMD, 6).Value = d.Type;
                wsMD.Cell(rMD, 7).Value = d.Column;
                rMD++;
            }

            foreach (var rr in term.Rules)
            {
                var partyCode = T(rr.PartyCode);

                var srid = BuildSridMr(item, partyCode, bucket);

                wsMR.Cell(rMR, 1).Value = esNuevo
                    ? ACTION_CREATE
                    : ACTION_UPDATE;
                wsMR.Cell(rMR, 2).Value = sourceDiscountListId;
                wsMR.Cell(rMR, 3).Value = smid;
                wsMR.Cell(rMR, 4).Value = srid;
                wsMR.Cell(rMR, 5).Value = partyCode;
                wsMR.Cell(rMR, 6).Value = "DISCOUNT_PERCENT";
                wsMR.Cell(rMR, 7).Value =
                    rr.Valor.ToString(
                        "0.############################",
                        CultureInfo.InvariantCulture);
                wsMR.Cell(rMR, 8).Value = "Adjustment Basis";
                wsMR.Cell(rMR, 15).Value = FormatDateLocal(rr.Start);
                wsMR.Cell(rMR, 16).Value = FormatDateLocal(rr.End);
                rMR++;
            }
        }

        if (rMR == 2)
            return ArchivoProcesoResult.Fallo("No se generaron reglas en MatrixRulesInterface para ART_NO_PROMO.");

        if (marcarComoGenerado)
        {
            await using var trx = await _OracleContext.Database.BeginTransactionAsync(ct);

            try
            {
                var db = await _OracleContext.ART_NO_PROMOs
                    .FirstOrDefaultAsync(a =>
                        a.BU_NAME == header.BU_NAME &&
                        a.ORGANIZATION_CODE == header.ORGANIZATION_CODE &&
                        a.ITEM_NUMBER == header.ITEM_NUMBER,
                        ct);

                if (db != null)
                    db.GENERADO = "S";

                await _OracleContext.SaveChangesAsync(ct);
                await trx.CommitAsync(ct);
            }
            catch
            {
                await trx.RollbackAsync(ct);
                throw;
            }
        }

        var sheets = new[]
        {
        "DiscountListsInterface",
        "DiscountListItemsInterface",
        "PricingTermsInterface",
        "MatrixDimensionsInterface",
        "MatrixRulesInterface"
    };

        var zipBytes = ZipWorksheetsAsCsv(wb, sheets);

        return ArchivoProcesoResult.Exito(zipBytes, nombreArchivo);
    }

    private static string GetCellValueInvariant(IXLCell cell)
    {
        if (cell.IsEmpty())
            return string.Empty;

        if (cell.DataType == XLDataType.Number)
        {
            var numero = cell.GetValue<decimal>();

            return numero.ToString(
                "0.############################",
                CultureInfo.InvariantCulture);
        }

        return cell.GetValue<string>() ?? string.Empty;
    }

    private static string MakeArtDetKey(
        string? ruleDiscountName,
        string? partyNumber,
        decimal discountPrice,
        DateTime startDate,
        DateTime? endDate)
    {
        static string N(string? s) => (s ?? "").Trim().ToUpperInvariant();

        var price = discountPrice.ToString(CultureInfo.InvariantCulture);
        var start = startDate.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var end = endDate.HasValue
            ? endDate.Value.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
            : "NULL";

        return $"{N(ruleDiscountName)}|{N(partyNumber)}|{price}|{start}|{end}";
    }
}