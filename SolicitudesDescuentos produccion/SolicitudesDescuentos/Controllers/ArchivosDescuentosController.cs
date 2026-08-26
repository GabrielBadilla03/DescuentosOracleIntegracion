using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SolicitudesDescuentos.Services;

namespace SolicitudesDescuentos.Controllers;

[Authorize]
public class ArchivosDescuentosController : Controller
{
    private readonly IArchivosDescuentosService _service;

    public ArchivosDescuentosController(IArchivosDescuentosService service)
    {
        _service = service;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IniciarFlujoItem(string itemNumber)
    {
        var result = await _service.IniciarFlujoItemAsync(itemNumber);

        if (!result.Ok)
        {
            TempData["InfoFlujo"] = result.Mensaje;
            return RedirectToAction("Index", "Predescuentos");
        }

        return File(result.ArchivoBytes!, result.ContentType, result.NombreArchivo);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReactivarFlujoItem(string itemNumber, DateTime? startDate, DateTime? endDate, decimal descuento)
    {
        var result = await _service.ReactivarFlujoItemAsync(itemNumber, startDate, endDate, descuento);

        if (!result.Ok)
        {
            TempData["InfoFlujo"] = result.Mensaje;
            return RedirectToAction("Index", "Predescuentos");
        }

        return File(result.ArchivoBytes!, result.ContentType, result.NombreArchivo);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DescargarExcel(List<string> seleccionados, string tipoFiltro)
    {
        var result = await _service.DescargarExcelAsync(
            seleccionados,
            tipoFiltro,
            marcarComoGenerado: true,
            forzarVencimientoDiaAnterior: false);

        if (!result.Ok)
        {
            TempData["ErrorMessage"] = result.Mensaje;
            return RedirectToAction("Index", "Predescuentos");
        }

        return File(result.ArchivoBytes!, result.ContentType, result.NombreArchivo);
    }
}


/*using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SolicitudesDescuentos.Data;
using SolicitudesDescuentos.ModelsOracle;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace SolicitudesDescuentos.Controllers
{
    [Authorize]
    public class ArchivosDescuentosController : Controller
    {

        private readonly OracleContext _OracleContext;
        private readonly record struct XxoraKey(string PartyNumber, string ItemNumber, string Uom);


        public ArchivosDescuentosController(OracleContext oracleContext)
        {
            _OracleContext = oracleContext;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IniciarFlujoItem(string itemNumber)
        {
            itemNumber = (itemNumber ?? "").Trim();

            if (string.IsNullOrWhiteSpace(itemNumber))
            {
                TempData["InfoFlujo"] = "Faltan datos: seleccioná un item.";
                return RedirectToAction("Index", "Predescuentos");
            }

            const string bu = "LANCO_CR";
            const string org = "CR_3";
            var itemKey = itemNumber.ToUpperInvariant();

            // ✅ Helper local: detecta bucket igual que tu lógica tolerante
            static string N(string? s) => (s ?? "").Trim().ToUpperInvariant();

            static string RuleBucketLocal(string? rule)
            {
                var x = N(rule);
                if (string.IsNullOrWhiteSpace(x)) return "";
                if (x == "PROMOCION" || x.Contains("PROMOC")) return "PROMOCION";
                if (x == "CLIENTE" || x.Contains("CLIENT")) return "CLIENTE";
                return "";
            }

            // ✅ Normaliza END_DATE: si es CLIENTE => null, si es PROMOCION => se respeta
            static DateTime? NormalizeEndDateByRule(string? rule, DateTime? end)
                => string.Equals(RuleBucketLocal(rule), "CLIENTE", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : end;

            await using var trx = await _OracleContext.Database.BeginTransactionAsync();

            try
            {
                // =====================================================
                // 1) Insertar en ART_NO_PROMO (si no existe)
                // =====================================================
                var existeHeader = await _OracleContext.ART_NO_PROMOs
                    .AnyAsync(a =>
                        (a.BU_NAME ?? "").Trim().ToUpper() == bu &&
                        (a.ORGANIZATION_CODE ?? "").Trim().ToUpper() == org &&
                        (a.ITEM_NUMBER ?? "").Trim().ToUpper() == itemKey
                    );

                if (!existeHeader)
                {
                    _OracleContext.ART_NO_PROMOs.Add(new ART_NO_PROMO
                    {
                        BU_NAME = bu,
                        ORGANIZATION_CODE = org,
                        ITEM_NUMBER = itemKey
                    });

                    await _OracleContext.SaveChangesAsync();
                }

                // =====================================================
                // 2) Traer todo de XXORA_DISCOUNT_LIST por BU + ITEM
                // =====================================================
                var xxoraRows = await _OracleContext.XXORA_DISCOUNT_LISTs
                    .AsNoTracking()
                    .Where(x =>
                        (x.BU_NAME ?? "").Trim().ToUpper() == bu &&
                        (x.ITEM_NUMBER ?? "").Trim().ToUpper() == itemKey
                    )
                    .Select(x => new
                    {
                        x.RULE_DISCOUNT_NAME,
                        x.PARTY_NUMBER,
                        x.PRICING_UOM_CODE,
                        x.DISCOUNT_PRICE,
                        x.START_DATE,
                        x.END_DATE
                    })
                    .ToListAsync();

                if (xxoraRows.Count == 0)
                {
                    await trx.CommitAsync();
                    TempData["InfoFlujo"] = $"ART_NO_PROMO OK. No hay filas en XXORA_DISCOUNT_LIST para BU={bu} ITEM={itemKey}.";
                    return RedirectToAction("Index", "Predescuentos");
                }

                // =====================================================
                // 3) Evitar duplicados en ART_DET_NO_PROMO
                // =====================================================
                var existentes = await _OracleContext.ART_DET_NO_PROMOs
                    .AsNoTracking()
                    .Where(d =>
                        (d.BU_NAME ?? "").Trim().ToUpper() == bu &&
                        (d.ORGANIZATION_CODE ?? "").Trim().ToUpper() == org &&
                        (d.ITEM_NUMBER ?? "").Trim().ToUpper() == itemKey
                    )
                    .Select(d => new
                    {
                        d.RULE_DISCOUNT_NAME,
                        d.PARTY_NUMBER,
                        d.PRICING_UOM_CODE,
                        d.DISCOUNT_PRICE,
                        d.START_DATE,
                        d.END_DATE
                    })
                    .ToListAsync();

                // ✅ IMPORTANTE: para la llave de existentes, también normalizamos END_DATE (CLIENTE => null)
                var existentesSet = new HashSet<string>(
                    existentes.Select(e =>
                        MakeArtDetKey(
                            e.RULE_DISCOUNT_NAME,
                            e.PARTY_NUMBER,
                            e.DISCOUNT_PRICE,
                            e.START_DATE,
                            NormalizeEndDateByRule(e.RULE_DISCOUNT_NAME, e.END_DATE)
                        )
                    ),
                    StringComparer.OrdinalIgnoreCase
                );

                int insertados = 0;
                int omitidos = 0;

                foreach (var r in xxoraRows)
                {
                    var rule = (r.RULE_DISCOUNT_NAME ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(rule))
                    {
                        omitidos++;
                        continue;
                    }

                    var party = (r.PARTY_NUMBER ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(party))
                    {
                        omitidos++;
                        continue;
                    }

                    var uom = string.IsNullOrWhiteSpace(r.PRICING_UOM_CODE) ? null : r.PRICING_UOM_CODE.Trim();

                    // ✅ AQUI está el cambio: END_DATE efectivo según rule
                    var effectiveEnd = NormalizeEndDateByRule(rule, r.END_DATE);

                    // ✅ Key con END_DATE normalizado (CLIENTE => NULL)
                    var k = MakeArtDetKey(rule, party, r.DISCOUNT_PRICE, r.START_DATE, effectiveEnd);

                    if (!existentesSet.Add(k))
                    {
                        omitidos++;
                        continue;
                    }

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

                        // ✅ CLIENTE => null, PROMOCION => valor real
                        END_DATE = effectiveEnd
                    });

                    insertados++;
                }

                if (insertados > 0)
                    await _OracleContext.SaveChangesAsync();

                await trx.CommitAsync();

                var zipBytes = await GenerarZipUpdateDesdeXxoraAsync(
                    bu: bu,
                    itemNumber: itemKey,
                    startDate: DateTime.Now,
                    endDate: null,
                    descuento: 0m    // (no usado aquí)
                );

                if (zipBytes.Length == 0)
                {
                    TempData["InfoFlujo"] = $"Flujo OK, pero no se pudo generar ZIP (sin filas XXORA) para BU={bu} ITEM={itemKey}.";
                    return RedirectToAction("Index", "Predescuentos");
                }

                var zipName = $"Descuentos_COSTARICA_ALL.zip";
                return File(zipBytes, "application/zip", zipName);
            }
            catch (Exception ex)
            {
                await trx.RollbackAsync();
                TempData["ErrorMessage"] = $"Error en flujo NO PROMO: {ex.Message}";
                return RedirectToAction("Index", "Predescuentos");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReactivarFlujoItem(string itemNumber, DateTime? startDate, DateTime? endDate, decimal descuento)
        {
            itemNumber = (itemNumber ?? "").Trim();

            if (string.IsNullOrWhiteSpace(itemNumber))
            {
                TempData["InfoFlujo"] = "Faltan datos: seleccioná un item.";
                return RedirectToAction("Index", "Predescuentos");
            }

            // Mismas validaciones (aunque este flujo usa fechas guardadas en ART_DET_NO_PROMO)
            if (!startDate.HasValue)
            {
                TempData["InfoFlujo"] = "Faltan datos: start_date.";
                return RedirectToAction("Index", "Predescuentos");
            }
            if (endDate.HasValue && endDate.Value.Date < startDate.Value.Date)
            {
                TempData["InfoFlujo"] = "End Date no puede ser menor que Start Date.";
                return RedirectToAction("Index", "Predescuentos");
            }

            const string bu = "LANCO_CR";
            const string org = "CR_3";
            var itemKey = itemNumber.ToUpperInvariant();

            await using var trx = await _OracleContext.Database.BeginTransactionAsync();

            try
            {
                // =====================================================
                // 1) Confirmar que existan detalles guardados en ART_DET_NO_PROMO
                //    (esta tabla es la "fuente de verdad" para reactivar)
                // =====================================================
                var detCount = await _OracleContext.ART_DET_NO_PROMOs
                    .AsNoTracking()
                    .CountAsync(d =>
                        (d.BU_NAME ?? "").Trim().ToUpper() == bu &&
                        (d.ORGANIZATION_CODE ?? "").Trim().ToUpper() == org &&
                        (d.ITEM_NUMBER ?? "").Trim().ToUpper() == itemKey
                    );

                if (detCount == 0)
                {
                    await trx.CommitAsync();
                    TempData["InfoFlujo"] = $"No hay registros en ART_DET_NO_PROMO para BU={bu} ITEM={itemKey}.";
                    return RedirectToAction("Index", "Predescuentos");
                }

                await trx.CommitAsync();

                // ✅ Generar ZIP CSVs (UPDATE) pero ahora RESTAURANDO desde ART_DET_NO_PROMO
                var zipBytes = await GenerarZipReactivarDesdeArtDetAsync(
                    bu: bu,
                    org: org,
                    itemNumber: itemKey,
                    startDate: startDate.Value,
                    endDate: endDate,
                    descuento: descuento
                );

                if (zipBytes.Length == 0)
                {
                    TempData["InfoFlujo"] = $"Flujo OK, pero no se pudo generar ZIP (sin filas ART_DET_NO_PROMO) para BU={bu} ITEM={itemKey}.";
                    return RedirectToAction("Index", "Predescuentos");
                }

                var zipName = $"Descuentos_COSTARICA_ALL.zip";
                return File(zipBytes, "application/zip", zipName);
            }
            catch (Exception ex)
            {
                await trx.RollbackAsync();
                TempData["ErrorMessage"] = $"Error en reactivación NO PROMO: {ex.Message}";
                return RedirectToAction("Index", "Predescuentos");
            }
        }

        private async Task<byte[]> GenerarZipUpdateDesdeXxoraAsync(
                string bu,
                string itemNumber,
                DateTime startDate,     // (se mantiene firma, pero aquí NO lo usamos)
                DateTime? endDate,      // (se mantiene firma, pero aquí NO lo usamos)
                decimal descuento       // (se mantiene firma, pero aquí NO lo usamos)
            )
        {
            static string T(string? s) => (s ?? "").Trim();
            static string N(string? s) => (s ?? "").Trim().ToUpperInvariant();

            static string FormatDateLocal(DateTime? fecha) =>
                fecha.HasValue ? fecha.Value.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture) : string.Empty;

            static string FormatDateDiscountListsLocal(DateTime? fecha) =>
                fecha.HasValue ? fecha.Value.Date.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture) : string.Empty;

            static string SafeId(string? raw, int maxLen = 40)
            {
                var s = (raw ?? "").Trim();
                if (string.IsNullOrEmpty(s)) return "X";

                var sb = new StringBuilder(s.Length);
                foreach (var ch in s)
                {
                    if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                    else sb.Append('_');
                }

                var outp = sb.ToString();
                while (outp.Contains("__")) outp = outp.Replace("__", "_");

                if (outp.Length > maxLen) outp = outp.Substring(0, maxLen);
                return outp.Trim('_');
            }

            // Regla normalizada: SOLO CLIENTE o PROMOCION
            static string RuleBucket(string? rule)
            {
                var x = N(rule);
                if (string.IsNullOrWhiteSpace(x)) return "";

                // tolerante por si viniera "PROMOCION ..." o similar
                if (x == "PROMOCION" || x.Contains("PROMOC")) return "PROMOCION";
                if (x == "CLIENTE" || x.Contains("CLIENT")) return "CLIENTE";

                // si quieres ser estricto, cambia esto a return "";
                return "";
            }

            bu = T(bu);
            itemNumber = T(itemNumber);

            if (string.IsNullOrWhiteSpace(bu) || string.IsNullOrWhiteSpace(itemNumber))
                return Array.Empty<byte>();

            var buKey = N(bu);
            var itemKey = N(itemNumber);

            // =========================================================
            // 0) END_DATE NUEVA = HOY 23:59:59 (para EXPIRAR TODO)
            // =========================================================
            var endExpire = DateTime.Today.AddDays(1).AddSeconds(-1); // fin de hoy
            var endExpireStr = FormatDateLocal(endExpire);

            var todayStart = DateTime.Today;

            // =========================================================
            // 1) Traer TODO desde XXORA para BU + ITEM (fijos + promos)
            // =========================================================
            var xxora = await (
                from x in _OracleContext.ART_DET_NO_PROMOs.AsNoTracking()
                join c in _OracleContext.GEN_CLIENTEs.AsNoTracking()
                    on x.PARTY_NUMBER equals c.IDCLIENTE into gj
                from c in gj.DefaultIfEmpty()
                where x.BU_NAME != null
                   && x.ITEM_NUMBER != null
                   && x.PARTY_NUMBER != null
                   && x.BU_NAME.Trim().ToUpper() == buKey
                   && x.ITEM_NUMBER.Trim().ToUpper() == itemKey
                   && (x.END_DATE == null || x.END_DATE >= todayStart) // fijos+promos vigentes
                select new
                {
                    Rule = x.RULE_DISCOUNT_NAME,
                    PartyCode = x.PARTY_NUMBER,
                    PartyName = c.NOMBRE_CLIENTE,
                    Uom = x.PRICING_UOM_CODE,
                    Price = x.DISCOUNT_PRICE,
                    Start = x.START_DATE,
                    End = x.END_DATE
                }
            ).ToListAsync();

            if (xxora.Count == 0)
                return Array.Empty<byte>();

            // =========================================================
            // 2) Normalizar filas (evitar duplicados)
            //    Clave: party + uom + rule + start + price
            // =========================================================
            var rows = xxora
                .Where(r => !string.IsNullOrWhiteSpace(r.PartyCode))
                .Where(r => !string.IsNullOrWhiteSpace(r.Rule))
                .Where(r => r.Start != default) // por seguridad
                .GroupBy(r => $"{N(r.PartyCode)}|{N(r.Uom)}|{N(r.Rule)}|{r.Start:yyyyMMddHHmmss}|{(r.Price).ToString(CultureInfo.InvariantCulture)}",
                         StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(r => N(r.PartyCode))
                .ThenBy(r => N(r.Rule))
                .ThenBy(r => N(r.Uom))
                .ToList();

            if (rows.Count == 0)
                return Array.Empty<byte>();

            // =========================================================
            // 3) Constantes EXACTAS (tu formato actual)
            // =========================================================
            const string batchName = "Descuentos_COSTARICA_ALL";
            const string sourceDiscountListId = "LP_001CR_CL";

            const string name = "Descuentos_CostaRica";
            const string description = "Descuentos_CostaRica";
            const string businessUnitId = "";
            const string businessUnitName = "LANCO_CR";
            const string currencyCode = "CRC";
            const string statusCode = "APPROVED";

            // =========================================================
            // 4) IDs base
            // =========================================================
            var sdlid = $"SDLID_{itemKey}";

            var baseUom = T(rows.Select(r => r.Uom).FirstOrDefault(u => !string.IsNullOrWhiteSpace(T(u)))) ?? "";

            // DiscountListsInterface START_DATE: usamos min(Start) de XXORA a 00:00:00
            var minStart = rows.Select(r => (DateTime?)r.Start).Min()?.Date ?? DateTime.Today;
            var dlStartStr = FormatDateDiscountListsLocal(minStart);

            // =========================================================
            // 5) Workbook + hojas
            // =========================================================
            using var wb = new XLWorkbook();

            var ws1 = wb.Worksheets.Add("DiscountListsInterface");
            ws1.Cell(1, 1).Value = "*BATCH_NAME";
            ws1.Cell(1, 2).Value = "*OPERATION_CODE";
            ws1.Cell(1, 3).Value = "*SOURCE_DISCOUNT_LIST_ID";
            ws1.Cell(1, 4).Value = "*NAME";
            ws1.Cell(1, 5).Value = "DESCRIPTION";
            ws1.Cell(1, 6).Value = "**BUSINESS_UNIT_ID";
            ws1.Cell(1, 7).Value = "**BUSINESS_UNIT_NAME";
            ws1.Cell(1, 8).Value = "*CURRENCY_CODE";
            ws1.Cell(1, 9).Value = "*START_DATE";
            ws1.Cell(1, 10).Value = "END_DATE";
            ws1.Cell(1, 11).Value = "*STATUS_CODE";
            ws1.Row(1).Style.Font.Bold = true;

            var ws3 = wb.Worksheets.Add("DiscountListItemsInterface");
            ws3.Cell(1, 1).Value = "*OPERATION_CODE";
            ws3.Cell(1, 2).Value = "*SOURCE_DISCOUNT_LIST_ID";
            ws3.Cell(1, 3).Value = "*SOURCE_DISCOUNT_LIST_ITEM_ID";
            ws3.Cell(1, 4).Value = "*ITEM_LEVEL_CODE";
            ws3.Cell(1, 5).Value = "**ITEM_NUMBER";
            ws3.Cell(1, 6).Value = "**ITEM_ID";
            ws3.Cell(1, 7).Value = "**PRICING_UOM";
            ws3.Cell(1, 8).Value = "**PRICING_UOM_CODE";
            ws3.Cell(1, 9).Value = "*LINE_TYPE_CODE";
            ws3.Row(1).Style.Font.Bold = true;

            var ws4 = wb.Worksheets.Add("PricingTermsInterface");
            ws4.Cell(1, 1).Value = "*OPERATION_CODE";
            ws4.Cell(1, 2).Value = "*SOURCE_ROOT_PARENT_ID";
            ws4.Cell(1, 3).Value = "*SOURCE_PARENT_ID";
            ws4.Cell(1, 4).Value = "*SOURCE_TERM_ID";
            ws4.Cell(1, 5).Value = "*NAME";
            ws4.Cell(1, 6).Value = "*PRICING_RULE_TYPE_CODE";
            ws4.Cell(1, 7).Value = "*PRICE_TYPE_CODE";
            ws4.Cell(1, 8).Value = "*CHARGE_TYPE_CODE";
            ws4.Cell(1, 9).Value = "*CHARGE_SUBTYPE_CODE";
            ws4.Cell(1, 10).Value = "**PRICE_PERIODICITY";
            ws4.Cell(1, 11).Value = "**PRICE_PERIODICITY_CODE";
            ws4.Cell(1, 12).Value = "ADJUSTMENT_TYPE_CODE";
            ws4.Cell(1, 13).Value = "ADJUSTMENT_AMOUNT";
            ws4.Cell(1, 14).Value = "**ADJUSTMENT_BASIS";
            ws4.Cell(1, 15).Value = "**ADJUSTMENT_BASIS_ID";
            ws4.Cell(1, 16).Value = "APPLY_TO_ROLLUP_FLAG";
            ws4.Cell(1, 17).Value = "*START_DATE";
            ws4.Cell(1, 18).Value = "END_DATE";
            ws4.Row(1).Style.Font.Bold = true;

            var ws5 = wb.Worksheets.Add("MatrixDimensionsInterface");
            ws5.Cell(1, 1).Value = "*OPERATION_CODE";
            ws5.Cell(1, 2).Value = "*SOURCE_ROOT_PARENT_ID";
            ws5.Cell(1, 3).Value = "*SOURCE_PARENT_ID";
            ws5.Cell(1, 4).Value = "*SOURCE_MATRIX_ID";
            ws5.Cell(1, 5).Value = "*DIMENSION_NAME";
            ws5.Cell(1, 6).Value = "*DIMENSION_TYPE";
            ws5.Cell(1, 7).Value = "*MAP_TO_RULE_COLUMN";
            ws5.Row(1).Style.Font.Bold = true;

            var ws6 = wb.Worksheets.Add("MatrixRulesInterface");
            ws6.Cell(1, 1).Value = "*OPERATION_CODE";
            ws6.Cell(1, 2).Value = "*SOURCE_ROOT_PARENT_ID";
            ws6.Cell(1, 3).Value = "*SOURCE_MATRIX_ID";
            ws6.Cell(1, 4).Value = "*SOURCE_RULE_ID";
            ws6.Cell(1, 5).Value = "VALUE_STRING1";
            ws6.Cell(1, 6).Value = "VALUE_STRING2";
            ws6.Cell(1, 7).Value = "VALUE_STRING3";
            ws6.Cell(1, 8).Value = "VALUE_STRING4";
            ws6.Row(1).Style.Font.Bold = true;

            int r1 = 2, r3 = 2, r4 = 2, r5 = 2, r6 = 2;

            // =========================================================
            // A) DiscountListsInterface: NO-OP (con info coherente)
            // =========================================================
            ws1.Cell(r1, 1).Value = batchName;
            ws1.Cell(r1, 2).Value = "NO-OP";
            ws1.Cell(r1, 3).Value = sourceDiscountListId;
            ws1.Cell(r1, 4).Value = name;
            ws1.Cell(r1, 5).Value = description;
            ws1.Cell(r1, 6).Value = businessUnitId;
            ws1.Cell(r1, 7).Value = businessUnitName;
            ws1.Cell(r1, 8).Value = currencyCode;
            ws1.Cell(r1, 9).Value = dlStartStr; // basado en XXORA (min start)
            ws1.Cell(r1, 10).Value = "";        // NO-OP, no tocamos lista
            ws1.Cell(r1, 11).Value = statusCode;
            r1++;

            // =========================================================
            // B) DiscountListItemsInterface: NO-OP (1 fila)
            // =========================================================
            ws3.Cell(r3, 1).Value = "NO-OP";
            ws3.Cell(r3, 2).Value = sourceDiscountListId;
            ws3.Cell(r3, 3).Value = sdlid;
            ws3.Cell(r3, 4).Value = "ITEM";
            ws3.Cell(r3, 5).Value = itemKey;
            ws3.Cell(r3, 6).Value = "";
            ws3.Cell(r3, 7).Value = "";
            ws3.Cell(r3, 8).Value = baseUom; // UOM desde XXORA
            ws3.Cell(r3, 9).Value = "ORA_BUY";
            r3++;


            // =========================================================
            // C) PricingTerms: 1 fila por RuleName detectado (CLIENTE/PROMOCION)
            //    MatrixDimensions: 4 filas por cada uno de esos terms
            // =========================================================
            var ruleBuckets = rows
                .Select(x => RuleBucket(x.Rule))
                .Where(x => x == "CLIENTE" || x == "PROMOCION")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ruleBuckets.Count == 0)
                return Array.Empty<byte>(); // no hay CLIENTE ni PROMOCION detectables

            // Helper para IDs según bucket
            static string BuildStid(string itemKey, string bucket) =>
                string.Equals(bucket, "PROMOCION", StringComparison.OrdinalIgnoreCase)
                    ? $"STID_SDLID_{itemKey}_PROMOCION"
                    : $"STID_SDLID_{itemKey}";

            static string BuildSmid(string stid) => $"SMID_{stid}";

            // SRID:
            //  - FIJO:      SRID_{SMID}_{STID}_SDLID{ITEM}{PARTY}
            //  - PROMOCION: SRID_{SMID}_{STID}_SDLID{ITEM}_PROMOCION{PARTY}
            string BuildSrid(string smid, string stid, string itemKey, string partyNumber, string bucket)
            {
                var partyId = SafeId(partyNumber, 30); // mantiene tu sanitizado
                if (string.Equals(bucket, "PROMOCION", StringComparison.OrdinalIgnoreCase))
                    return $"SRID_{smid}_{stid}_SDLID{itemKey}_PROMOCION{partyId}";

                return $"SRID_{smid}_{stid}_SDLID{itemKey}{partyId}";
            }

            // Mapea RuleBucket -> (STID, SMID, TermStart, TermName/ConditionName)
            var termMap = new Dictionary<string, (string STID, string SMID, DateTime TermStart, string TermName)>(
                StringComparer.OrdinalIgnoreCase
            );

            foreach (var bucket in ruleBuckets)
            {
                // Start del term = el mínimo START_DATE entre las filas de ese bucket
                var termStart = rows
                    .Where(x => string.Equals(RuleBucket(x.Rule), bucket, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Start)
                    .DefaultIfEmpty(minStart)
                    .Min();

                var stid = BuildStid(itemKey, bucket);
                var smid = BuildSmid(stid);

                var termName = string.Equals(bucket, "PROMOCION", StringComparison.OrdinalIgnoreCase)
                    ? "PROMOCION"
                    : "CLIENTE";

                termMap[bucket] = (stid, smid, termStart, termName);

                // ---- PricingTermsInterface (UPDATE) 1 fila por bucket
                ws4.Cell(r4, 1).Value = "UPDATE";
                ws4.Cell(r4, 2).Value = sourceDiscountListId; // root parent
                ws4.Cell(r4, 3).Value = sdlid;                // parent id (igual)
                ws4.Cell(r4, 4).Value = stid;                 // term id (promo: _PROMOCION)
                ws4.Cell(r4, 5).Value = termName;             // CLIENTE o PROMOCION
                ws4.Cell(r4, 6).Value = "ATTRIBUTE_PRICING";
                ws4.Cell(r4, 7).Value = "ALL";
                ws4.Cell(r4, 8).Value = "ORA_SALE";
                ws4.Cell(r4, 9).Value = "ORA_PRICE";
                ws4.Cell(r4, 10).Value = "";
                ws4.Cell(r4, 11).Value = "ALL";
                ws4.Cell(r4, 12).Value = "";
                ws4.Cell(r4, 13).Value = "";
                ws4.Cell(r4, 14).Value = "";
                ws4.Cell(r4, 15).Value = "";
                ws4.Cell(r4, 16).Value = "";
                ws4.Cell(r4, 17).Value = FormatDateLocal(termStart);
                ws4.Cell(r4, 18).Value = endExpireStr; // HOY 23:59:59
                r4++;

                // ---- MatrixDimensionsInterface (NO-OP) 4 filas por cada term
                //     La dimensión condición debe ser CLIENTE o PROMOCION según el bucket
                var dims = new (string Name, string Type, string Column)[]
                {
        ("Adjustment Amount", "Result",    "VALUE_STRING3"),
        ("Adjustment Basis",  "Result",    "VALUE_STRING4"),
        ("Adjustment Type",   "Result",    "VALUE_STRING2"),
        (termName,            "Condition", "VALUE_STRING1"),
                };

                foreach (var d in dims)
                {
                    ws5.Cell(r5, 1).Value = "NO-OP";
                    ws5.Cell(r5, 2).Value = sourceDiscountListId; // root parent
                    ws5.Cell(r5, 3).Value = stid;                 // parent id = term
                    ws5.Cell(r5, 4).Value = smid;                 // matrix id (promo incluye _PROMOCION)
                    ws5.Cell(r5, 5).Value = d.Name;
                    ws5.Cell(r5, 6).Value = d.Type;
                    ws5.Cell(r5, 7).Value = d.Column;
                    r5++;
                }
            }

            // =========================================================
            // D) MatrixRules: UPDATE por cliente, apuntando al SMID del bucket correcto
            // =========================================================
            foreach (var rr in rows)
            {
                var partyCode = T(rr.PartyCode);
                if (string.IsNullOrWhiteSpace(partyCode)) continue;

                var bucket = RuleBucket(rr.Rule);
                if (!termMap.TryGetValue(bucket, out var ids)) continue; // ignora reglas fuera de CLIENTE/PROMOCION

                var startStr = FormatDateLocal(rr.Start);

                // ✅ SRID con el cambio pedido:
                // - FIJO:      ..._SDLID{ITEM}{PARTY}
                // - PROMOCION: ..._SDLID{ITEM}_PROMOCION{PARTY}
                var partyId = SafeId(partyCode, 30);

                var sridMR = string.Equals(bucket, "PROMOCION", StringComparison.OrdinalIgnoreCase)
                    ? $"SRID_SMID_STID_SDLID{itemKey}_PROMOCION{partyId}"
                    : $"SRID_SMID_STID_SDLID{itemKey}{partyId}";

                ws6.Cell(r6, 1).Value = "UPDATE";
                ws6.Cell(r6, 2).Value = sourceDiscountListId;
                ws6.Cell(r6, 3).Value = ids.SMID;   // promo: SMID_..._PROMOCION
                ws6.Cell(r6, 4).Value = sridMR;

                ws6.Cell(r6, 5).Value = rr.PartyName;         // VALUE_STRING1
                ws6.Cell(r6, 6).Value = "DISCOUNT_PERCENT";   // VALUE_STRING2
                ws6.Cell(r6, 7).Value = rr.Price;             // VALUE_STRING3
                ws6.Cell(r6, 8).Value = "Adjustment Basis";   // VALUE_STRING4

                // columnas extendidas (como ya venías haciendo)
                ws6.Cell(r6, 15).Value = startStr;
                ws6.Cell(r6, 16).Value = endExpireStr;
                r6++;
            }

            // Validación mínima: debe haber al menos 1 term y 1 regla
            if (r4 == 2 || r6 == 2)
                return Array.Empty<byte>();

            var sheets = new[]
            {
                    "DiscountListsInterface",
                    "DiscountListItemsInterface",
                    "PricingTermsInterface",
                    "MatrixDimensionsInterface",
                    "MatrixRulesInterface"
                };

            return ZipWorksheetsAsCsv(wb, sheets);
        }

        private async Task<byte[]> GenerarZipReactivarDesdeArtDetAsync(
            string bu,
            string org,
            string itemNumber,
            DateTime startDate,     // se mantiene firma
            DateTime? endDate,      // se mantiene firma
            decimal descuento       // se mantiene firma
        )
        {
            static string T(string? s) => (s ?? "").Trim();
            static string N(string? s) => (s ?? "").Trim().ToUpperInvariant();

            static string FormatDateLocal(DateTime? fecha) =>
                fecha.HasValue ? fecha.Value.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture) : string.Empty;

            static string FormatDateDiscountListsLocal(DateTime? fecha) =>
                fecha.HasValue ? fecha.Value.Date.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture) : string.Empty;

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

                if (outp.Length > maxLen) outp = outp.Substring(0, maxLen);
                return outp.Trim('_');
            }

            // Regla normalizada: SOLO CLIENTE o PROMOCION (igual al otro método)
            static string RuleBucket(string? rule)
            {
                var x = N(rule);
                if (string.IsNullOrWhiteSpace(x)) return "";

                if (x == "PROMOCION" || x.Contains("PROMOC")) return "PROMOCION";
                if (x == "CLIENTE" || x.Contains("CLIENT")) return "CLIENTE";

                return "";
            }

            bu = T(bu);
            org = T(org);
            itemNumber = T(itemNumber);

            if (string.IsNullOrWhiteSpace(bu) || string.IsNullOrWhiteSpace(org) || string.IsNullOrWhiteSpace(itemNumber))
                return Array.Empty<byte>();

            var buKey = N(bu);
            var orgKey = N(org);
            var itemKey = N(itemNumber);

            // Fallback UOM desde INV_ARTICULO por si ART_DET_NO_PROMO.PRICING_UOM_CODE viene vacío
            var invUom = await _OracleContext.INV_ARTICULOs
                .AsNoTracking()
                .Where(i => i.COD_ARTICULO != null && i.COD_ARTICULO.Trim().ToUpper() == itemKey)
                .Select(i => i.MEDIDA)
                .FirstOrDefaultAsync() ?? "";

            // 1) Fuente: ART_DET_NO_PROMO (con nombre cliente opcional)
            var det = await (
                from d in _OracleContext.ART_DET_NO_PROMOs.AsNoTracking()
                join c in _OracleContext.GEN_CLIENTEs.AsNoTracking()
                    on d.PARTY_NUMBER equals c.IDCLIENTE into gj
                from c in gj.DefaultIfEmpty()
                where d.BU_NAME != null
                   && d.ORGANIZATION_CODE != null
                   && d.ITEM_NUMBER != null
                   && d.PARTY_NUMBER != null
                   && d.BU_NAME.Trim().ToUpper() == buKey
                   && d.ORGANIZATION_CODE.Trim().ToUpper() == orgKey
                   && d.ITEM_NUMBER.Trim().ToUpper() == itemKey
                select new
                {
                    Rule = d.RULE_DISCOUNT_NAME,
                    PartyCode = d.PARTY_NUMBER,
                    PartyName = c.NOMBRE_CLIENTE,
                    Uom = d.PRICING_UOM_CODE,
                    Price = d.DISCOUNT_PRICE,
                    Start = d.START_DATE,
                    End = d.END_DATE
                }
            ).ToListAsync();

            if (det.Count == 0)
                return Array.Empty<byte>();

            // 2) Normalizar + dedup (igual que el otro): party + uom + rule + start + price
            var rows = det
                .Where(r => !string.IsNullOrWhiteSpace(r.PartyCode))
                .Where(r => !string.IsNullOrWhiteSpace(r.Rule))
                .Where(r => r.Start != default)
                .Select(r => new
                {
                    r.Rule,
                    r.PartyCode,
                    r.PartyName,
                    Uom = string.IsNullOrWhiteSpace(T(r.Uom)) ? invUom : T(r.Uom),
                    r.Price,
                    r.Start,
                    r.End
                })
                .GroupBy(r => $"{N(r.PartyCode)}|{N(r.Uom)}|{N(r.Rule)}|{r.Start:yyyyMMddHHmmss}|{(r.Price).ToString(CultureInfo.InvariantCulture)}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(r => N(r.PartyCode))
                .ThenBy(r => N(r.Rule))
                .ThenBy(r => N(r.Uom))
                .ToList();

            if (rows.Count == 0)
                return Array.Empty<byte>();

            // 3) Constantes EXACTAS (igual)
            const string batchName = "Descuentos_COSTARICA_ALL";
            const string sourceDiscountListId = "LP_001CR_CL";

            const string name = "Descuentos_CostaRica";
            const string description = "Descuentos_CostaRica";
            const string businessUnitId = "";
            const string businessUnitName = "LANCO_CR";
            const string currencyCode = "CRC";
            const string statusCode = "APPROVED";

            // 4) IDs base (igual)
            var sdlid = $"SDLID_{itemKey}";
            var baseUom = T(rows.Select(r => r.Uom).FirstOrDefault(u => !string.IsNullOrWhiteSpace(T(u)))) ?? "";

            var minStart = rows.Select(r => (DateTime?)r.Start).Min()?.Date ?? DateTime.Today;
            var dlStartStr = FormatDateDiscountListsLocal(minStart);

            // 5) Workbook + hojas (igual)
            using var wb = new XLWorkbook();

            var ws1 = wb.Worksheets.Add("DiscountListsInterface");
            ws1.Cell(1, 1).Value = "*BATCH_NAME";
            ws1.Cell(1, 2).Value = "*OPERATION_CODE";
            ws1.Cell(1, 3).Value = "*SOURCE_DISCOUNT_LIST_ID";
            ws1.Cell(1, 4).Value = "*NAME";
            ws1.Cell(1, 5).Value = "DESCRIPTION";
            ws1.Cell(1, 6).Value = "**BUSINESS_UNIT_ID";
            ws1.Cell(1, 7).Value = "**BUSINESS_UNIT_NAME";
            ws1.Cell(1, 8).Value = "*CURRENCY_CODE";
            ws1.Cell(1, 9).Value = "*START_DATE";
            ws1.Cell(1, 10).Value = "END_DATE";
            ws1.Cell(1, 11).Value = "*STATUS_CODE";
            ws1.Row(1).Style.Font.Bold = true;

            var ws3 = wb.Worksheets.Add("DiscountListItemsInterface");
            ws3.Cell(1, 1).Value = "*OPERATION_CODE";
            ws3.Cell(1, 2).Value = "*SOURCE_DISCOUNT_LIST_ID";
            ws3.Cell(1, 3).Value = "*SOURCE_DISCOUNT_LIST_ITEM_ID";
            ws3.Cell(1, 4).Value = "*ITEM_LEVEL_CODE";
            ws3.Cell(1, 5).Value = "**ITEM_NUMBER";
            ws3.Cell(1, 6).Value = "**ITEM_ID";
            ws3.Cell(1, 7).Value = "**PRICING_UOM";
            ws3.Cell(1, 8).Value = "**PRICING_UOM_CODE";
            ws3.Cell(1, 9).Value = "*LINE_TYPE_CODE";
            ws3.Row(1).Style.Font.Bold = true;

            var ws4 = wb.Worksheets.Add("PricingTermsInterface");
            ws4.Cell(1, 1).Value = "*OPERATION_CODE";
            ws4.Cell(1, 2).Value = "*SOURCE_ROOT_PARENT_ID";
            ws4.Cell(1, 3).Value = "*SOURCE_PARENT_ID";
            ws4.Cell(1, 4).Value = "*SOURCE_TERM_ID";
            ws4.Cell(1, 5).Value = "*NAME";
            ws4.Cell(1, 6).Value = "*PRICING_RULE_TYPE_CODE";
            ws4.Cell(1, 7).Value = "*PRICE_TYPE_CODE";
            ws4.Cell(1, 8).Value = "*CHARGE_TYPE_CODE";
            ws4.Cell(1, 9).Value = "*CHARGE_SUBTYPE_CODE";
            ws4.Cell(1, 10).Value = "**PRICE_PERIODICITY";
            ws4.Cell(1, 11).Value = "**PRICE_PERIODICITY_CODE";
            ws4.Cell(1, 12).Value = "ADJUSTMENT_TYPE_CODE";
            ws4.Cell(1, 13).Value = "ADJUSTMENT_AMOUNT";
            ws4.Cell(1, 14).Value = "**ADJUSTMENT_BASIS";
            ws4.Cell(1, 15).Value = "**ADJUSTMENT_BASIS_ID";
            ws4.Cell(1, 16).Value = "APPLY_TO_ROLLUP_FLAG";
            ws4.Cell(1, 17).Value = "*START_DATE";
            ws4.Cell(1, 18).Value = "END_DATE";
            ws4.Row(1).Style.Font.Bold = true;

            var ws5 = wb.Worksheets.Add("MatrixDimensionsInterface");
            ws5.Cell(1, 1).Value = "*OPERATION_CODE";
            ws5.Cell(1, 2).Value = "*SOURCE_ROOT_PARENT_ID";
            ws5.Cell(1, 3).Value = "*SOURCE_PARENT_ID";
            ws5.Cell(1, 4).Value = "*SOURCE_MATRIX_ID";
            ws5.Cell(1, 5).Value = "*DIMENSION_NAME";
            ws5.Cell(1, 6).Value = "*DIMENSION_TYPE";
            ws5.Cell(1, 7).Value = "*MAP_TO_RULE_COLUMN";
            ws5.Row(1).Style.Font.Bold = true;

            var ws6 = wb.Worksheets.Add("MatrixRulesInterface");
            ws6.Cell(1, 1).Value = "*OPERATION_CODE";
            ws6.Cell(1, 2).Value = "*SOURCE_ROOT_PARENT_ID";
            ws6.Cell(1, 3).Value = "*SOURCE_MATRIX_ID";
            ws6.Cell(1, 4).Value = "*SOURCE_RULE_ID";
            ws6.Cell(1, 5).Value = "VALUE_STRING1";
            ws6.Cell(1, 6).Value = "VALUE_STRING2";
            ws6.Cell(1, 7).Value = "VALUE_STRING3";
            ws6.Cell(1, 8).Value = "VALUE_STRING4";
            ws6.Row(1).Style.Font.Bold = true;

            int r1 = 2, r3 = 2, r4 = 2, r5 = 2, r6 = 2;

            // A) DiscountListsInterface: NO-OP (igual)
            ws1.Cell(r1, 1).Value = batchName;
            ws1.Cell(r1, 2).Value = "NO-OP";
            ws1.Cell(r1, 3).Value = sourceDiscountListId;
            ws1.Cell(r1, 4).Value = name;
            ws1.Cell(r1, 5).Value = description;
            ws1.Cell(r1, 6).Value = businessUnitId;
            ws1.Cell(r1, 7).Value = businessUnitName;
            ws1.Cell(r1, 8).Value = currencyCode;
            ws1.Cell(r1, 9).Value = dlStartStr;
            ws1.Cell(r1, 10).Value = "";
            ws1.Cell(r1, 11).Value = statusCode;
            r1++;

            // B) DiscountListItemsInterface: NO-OP (igual)
            ws3.Cell(r3, 1).Value = "NO-OP";
            ws3.Cell(r3, 2).Value = sourceDiscountListId;
            ws3.Cell(r3, 3).Value = sdlid;
            ws3.Cell(r3, 4).Value = "ITEM";
            ws3.Cell(r3, 5).Value = itemKey;
            ws3.Cell(r3, 6).Value = "";
            ws3.Cell(r3, 7).Value = "";
            ws3.Cell(r3, 8).Value = baseUom;
            ws3.Cell(r3, 9).Value = "ORA_BUY";
            r3++;

            // C) PricingTerms + MatrixDimensions: por bucket (CLIENTE/PROMOCION) igual al armado del otro
            var ruleBuckets = rows
                .Select(x => RuleBucket(x.Rule))
                .Where(x => x == "CLIENTE" || x == "PROMOCION")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ruleBuckets.Count == 0)
                return Array.Empty<byte>();

            static string BuildStid(string itemKey, string bucket) =>
                string.Equals(bucket, "PROMOCION", StringComparison.OrdinalIgnoreCase)
                    ? $"STID_SDLID_{itemKey}_PROMOCION"
                    : $"STID_SDLID_{itemKey}";

            static string BuildSmid(string stid) => $"SMID_{stid}";

            var termMap = new Dictionary<string, (string STID, string SMID, DateTime TermStart, DateTime? TermEnd, string TermName)>(
                StringComparer.OrdinalIgnoreCase
            );

            foreach (var bucket in ruleBuckets)
            {
                var termRows = rows.Where(x => string.Equals(RuleBucket(x.Rule), bucket, StringComparison.OrdinalIgnoreCase)).ToList();

                var termStart = termRows.Select(x => x.Start).DefaultIfEmpty(minStart).Min();

                // Si alguna regla del bucket tiene END null => termEnd vacío (abierto)
                DateTime? termEnd = null;
                var ends = termRows.Select(x => x.End).ToList();
                if (ends.Count > 0 && ends.All(e => e.HasValue))
                    termEnd = ends.Max()!.Value;

                var stid = BuildStid(itemKey, bucket);
                var smid = BuildSmid(stid);
                var termName = string.Equals(bucket, "PROMOCION", StringComparison.OrdinalIgnoreCase) ? "PROMOCION" : "CLIENTE";

                termMap[bucket] = (stid, smid, termStart, termEnd, termName);

                // PricingTermsInterface (UPDATE)
                ws4.Cell(r4, 1).Value = "UPDATE";
                ws4.Cell(r4, 2).Value = sourceDiscountListId; // root
                ws4.Cell(r4, 3).Value = sdlid;                // parent
                ws4.Cell(r4, 4).Value = stid;                 // term id (promo incluye _PROMOCION)
                ws4.Cell(r4, 5).Value = termName;             // CLIENTE o PROMOCION
                ws4.Cell(r4, 6).Value = "ATTRIBUTE_PRICING";
                ws4.Cell(r4, 7).Value = "ALL";
                ws4.Cell(r4, 8).Value = "ORA_SALE";
                ws4.Cell(r4, 9).Value = "ORA_PRICE";
                ws4.Cell(r4, 10).Value = "";
                ws4.Cell(r4, 11).Value = "ALL";
                ws4.Cell(r4, 12).Value = "";
                ws4.Cell(r4, 13).Value = "";
                ws4.Cell(r4, 14).Value = "";
                ws4.Cell(r4, 15).Value = "";
                ws4.Cell(r4, 16).Value = "";
                ws4.Cell(r4, 17).Value = FormatDateLocal(termStart);
                ws4.Cell(r4, 18).Value = FormatDateLocal(termEnd); // RESTAURADO (vacío si null)
                r4++;

                // MatrixDimensionsInterface (NO-OP) 4 filas por term (igual)
                var dims = new (string Name, string Type, string Column)[]
                {
            ("Adjustment Amount", "Result",    "VALUE_STRING3"),
            ("Adjustment Basis",  "Result",    "VALUE_STRING4"),
            ("Adjustment Type",   "Result",    "VALUE_STRING2"),
            (termName,            "Condition", "VALUE_STRING1"),
                };

                foreach (var d in dims)
                {
                    ws5.Cell(r5, 1).Value = "NO-OP";
                    ws5.Cell(r5, 2).Value = sourceDiscountListId; // root
                    ws5.Cell(r5, 3).Value = stid;                 // parent = term
                    ws5.Cell(r5, 4).Value = smid;                 // matrix id
                    ws5.Cell(r5, 5).Value = d.Name;
                    ws5.Cell(r5, 6).Value = d.Type;
                    ws5.Cell(r5, 7).Value = d.Column;
                    r5++;
                }
            }

            // D) MatrixRulesInterface: UPDATE por fila (igual, pero END restaurado)
            foreach (var rr in rows)
            {
                var partyCode = T(rr.PartyCode);
                if (string.IsNullOrWhiteSpace(partyCode)) continue;

                var bucket = RuleBucket(rr.Rule);
                if (!termMap.TryGetValue(bucket, out var ids)) continue;

                var partyId = SafeId(partyCode, 30);

                // SRID EXACTO (igual al otro método)
                var sridMR = string.Equals(bucket, "PROMOCION", StringComparison.OrdinalIgnoreCase)
                    ? $"SRID_SMID_STID_SDLID{itemKey}_PROMOCION{partyId}"
                    : $"SRID_SMID_STID_SDLID{itemKey}{partyId}";

                ws6.Cell(r6, 1).Value = "UPDATE";
                ws6.Cell(r6, 2).Value = sourceDiscountListId;
                ws6.Cell(r6, 3).Value = ids.SMID;
                ws6.Cell(r6, 4).Value = sridMR;

                ws6.Cell(r6, 5).Value = rr.PartyName;        // VALUE_STRING1
                ws6.Cell(r6, 6).Value = "DISCOUNT_PERCENT";  // VALUE_STRING2
                ws6.Cell(r6, 7).Value = rr.Price;            // VALUE_STRING3
                ws6.Cell(r6, 8).Value = "Adjustment Basis";  // VALUE_STRING4

                // columnas extendidas (igual), pero END real (vacío si null)
                ws6.Cell(r6, 15).Value = FormatDateLocal(rr.Start);
                ws6.Cell(r6, 16).Value = FormatDateLocal(rr.End);
                r6++;
            }

            // Validación mínima igual
            if (r4 == 2 || r6 == 2)
                return Array.Empty<byte>();

            var sheets = new[]
            {
                "DiscountListsInterface",
                "DiscountListItemsInterface",
                "PricingTermsInterface",
                "MatrixDimensionsInterface",
                "MatrixRulesInterface"
            };

            return ZipWorksheetsAsCsv(wb, sheets);
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

        // POST: Predescuentos/DescargarExcel  (múltiples seleccionados)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DescargarExcel(List<string> seleccionados, string tipoFiltro)
        {
            var tipo = NormalizeTipo(tipoFiltro);

            // si querés default:
            if (tipo == "") tipo = "promocional";
            // o si preferís estricto:
            // if (tipo == "") return BadRequest("tipoFiltro inválido.");

            if (seleccionados == null || seleccionados.Count == 0)
            {
                TempData["ErrorMessage"] = "Debe seleccionar al menos una solicitud para generar el Excel.";
                return RedirectToAction("Index", "Predescuentos");
            }

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
            {
                TempData["ErrorMessage"] = "No se recibieron solicitudes válidas.";
                return RedirectToAction("Index", "Predescuentos");
            }

            return await DescargarExcelInternoAsync(seleccionados, pares, tipo);
        }



        private async Task<IActionResult> DescargarExcelInternoAsync(
            IEnumerable<string>? seleccionados,
            List<(string CodCia, string Consecutivo)> pares,
            string tipoFiltro
        )
        {
            // =========================
            // Helpers (mismo estilo que Flujos)
            // =========================
            static string T(string? s) => (s ?? "").Trim();
            static string N(string? s) => (s ?? "").Trim().ToUpperInvariant();

            static string FormatDateLocal(DateTime? fecha) =>
                fecha.HasValue ? fecha.Value.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture) : string.Empty;

            static string FormatDateDiscountListsLocal(DateTime? fecha) =>
                fecha.HasValue ? fecha.Value.Date.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture) : string.Empty;

            static string SafeId(string? raw, int maxLen = 40)
            {
                var s = (raw ?? "").Trim();
                if (string.IsNullOrEmpty(s)) return "X";

                var sb = new StringBuilder(s.Length);
                foreach (var ch in s)
                    sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');

                var outp = sb.ToString();
                while (outp.Contains("__")) outp = outp.Replace("__", "_");

                if (outp.Length > maxLen) outp = outp.Substring(0, maxLen);
                return outp.Trim('_');
            }

            static string RuleBucket(string tipoNorm) =>
                string.Equals(tipoNorm, "promocional", StringComparison.OrdinalIgnoreCase) ? "PROMOCION" : "CLIENTE";

            // IDs como iniciar flujo/reactivar (STID/SMID por item + bucket)
            static string BuildStid(string itemKey, string bucket) =>
                string.Equals(bucket, "PROMOCION", StringComparison.OrdinalIgnoreCase)
                    ? $"STID_SDLID_{itemKey}_PROMOCION"
                    : $"STID_SDLID_{itemKey}";

            static string BuildSmid(string stid) => $"SMID_{stid}";

            // ✅ Regla de cadenas para MatrixRules: articulo + codCliente + (CLIENTE/PROMOCION) + medida
            // Mantiene prefijo estilo de flujos + incluye literal CLIENTE/PROMOCION + UOM
            string BuildSridMr(string itemKey, string partyCode, string bucket)
            {
                var partyId = SafeId(partyCode, 30);

                if (string.Equals(bucket, "PROMOCION", StringComparison.OrdinalIgnoreCase))
                    return $"SRID_SMID_STID_SDLID{itemKey}_PROMOCION{partyId}";

                return $"SRID_SMID_STID_SDLID{itemKey}_CLIENTE{partyId}";
            }

            // Acción estándar
            const string ACTION_CREATE = "CREATE";
            const string ACTION_UPDATE = "UPDATE";
            const string ACTION_NOOP = "NO-OP";

            // Constantes
            const string batchName = "Descuentos_COSTARICA_ALL";
            const string sourceDiscountListId = "LP_001CR_CL";

            const string name = "Descuentos_CostaRica";
            const string description = "Descuentos_CostaRica";
            const string businessUnitId = "";
            const string businessUnitName = "LANCO_CR";
            const string currencyCode = "CRC";
            const string statusCode = "APPROVED";

            // =========================
            // 1) Normalizar tipo
            // =========================
            var tipo = T(tipoFiltro).ToLowerInvariant();
            if (tipo != "promocional" && tipo != "fijo")
                return BadRequest("tipoFiltro inválido. Use 'promocional' o 'fijo'.");

            var bucketName = RuleBucket(tipo); // "PROMOCION" o "CLIENTE"

            if (pares == null || pares.Count == 0)
                return BadRequest("No hay solicitudes seleccionadas.");

            // =========================
            // 2) Cargar encabezados exactos (BU|CONSEC)
            // =========================
            var buSet = pares.Select(p => T(p.CodCia))
                .Where(x => x != "")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var consecSet = pares.Select(p => T(p.Consecutivo))
                .Where(x => x != "")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var headersRaw = await _OracleContext.PREDESCUENTOs
                .AsNoTracking()
                .Where(p => buSet.Contains(p.BU_NOMBRE) && consecSet.Contains(p.CONSECUTIVO))
                .ToListAsync();

            static bool Eq(string? a, string b) => string.Equals((a ?? "").Trim(), b, StringComparison.OrdinalIgnoreCase);

            var headers = headersRaw
                .Where(h => pares.Any(k => Eq(h.BU_NOMBRE, k.CodCia) && Eq(h.CONSECUTIVO, k.Consecutivo)))
                .ToList();

            if (headers.Count == 0)
                return NotFound("No se encontraron solicitudes con los consecutivos seleccionados.");

            // =========================
            // 3) Validaciones (estado, generado, anti-mezcla)
            // =========================
            if (headers.Any(h => !Eq(h.ESTADO, "Aprobado")))
                return BadRequest("Hay solicitudes que NO están en estado 'Aprobado'. No se puede generar.");

            if (headers.Any(h => !Eq(h.GENERADO, "N")))
                return BadRequest("Hay solicitudes que ya fueron generadas (GENERADO != 'N').");

            var mixed = headers
                .Where(h => NormalizeTipo(h.TIPODESCUENTO) != tipo)
                .Select(h => $"{T(h.BU_NOMBRE)}|{T(h.CONSECUTIVO)} ({T(h.TIPODESCUENTO)})")
                .ToList();

            if (mixed.Count > 0)
                return BadRequest("Anti-mezcla: seleccionaste solicitudes con tipo distinto al filtro. Ej: " + string.Join(", ", mixed));

            // =========================
            // 4) Construir filas exportables: (BU, Cliente, Item, Uom, Valor, Start, End)
            // =========================
            var exportRows = new List<(string BU, string PartyCode, string ItemNumber, string Uom, decimal Valor, DateTime? Start, DateTime? End)>();

            foreach (var h in headers)
            {
                var bu = T(h.BU_NOMBRE);
                var party = T(h.COD_CLIENTE);
                if (bu == "" || party == "") continue;

                DateTime? start = (tipo == "promocional")
                    ? (h.FECHAINICIO ?? h.FECHASOLICITUD)
                    : (h.FECHASOLICITUD);

                DateTime? end = (tipo == "promocional") ? h.FECHAFIN : null;

                var detalles = await _OracleContext.PREDETDESCUENTOs
                    .AsNoTracking()
                    .Where(d => d.BU_NOMBRE == bu && d.COD_CLIENTE == party && d.CONSECUTIVO == h.CONSECUTIVO)
                    .ToListAsync();

                if (detalles.Count == 0) continue;

                // Precedencia: Artículo > (Línea,Clase) > Línea
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

                // Expandir a artículos vía INV_ARTICULO
                var lineasNeeded = byLinea.Keys
                    .Concat(byLineaClase.Keys.Select(x => x.Linea))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var invItems = new List<(string CodArticulo, string Medida, string CodLinea, string CodClase)>();

                // a) artículos explícitos
                var artsExplicit = byArticulo.Keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (artsExplicit.Count > 0)
                {
                    foreach (var chunk in artsExplicit.Chunk(900))
                    {
                        var items = await _OracleContext.INV_ARTICULOs
                            .AsNoTracking()
                            .Where(i => i.COD_ARTICULO != null && chunk.Contains(i.COD_ARTICULO))
                            .Select(i => new { i.COD_ARTICULO, i.MEDIDA, i.COD_LINEA, COD_CLASE = i.COD_CLASE })
                            .ToListAsync();

                        invItems.AddRange(items.Select(x => (T(x.COD_ARTICULO), T(x.MEDIDA), T(x.COD_LINEA), T(x.COD_CLASE))));
                    }
                }

                // b) artículos por líneas (incluye línea-clase)
                if (lineasNeeded.Count > 0)
                {
                    foreach (var chunk in lineasNeeded.Chunk(200))
                    {
                        var items = await _OracleContext.INV_ARTICULOs
                            .AsNoTracking()
                            .Where(i => i.COD_LINEA != null && chunk.Contains(i.COD_LINEA))
                            .Select(i => new { i.COD_ARTICULO, i.MEDIDA, i.COD_LINEA, COD_CLASE = i.COD_CLASE })
                            .ToListAsync();

                        invItems.AddRange(items.Select(x => (T(x.COD_ARTICULO), T(x.MEDIDA), T(x.COD_LINEA), T(x.COD_CLASE))));
                    }
                }

                // Dedup por artículo
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

                    exportRows.Add((bu, party, item, uom, valorAplicable.Value, start, end));
                }
            }

            if (exportRows.Count == 0)
                return BadRequest("No se pudieron generar filas exportables (sin detalles o sin artículos coincidentes).");

            // Dedup por BU+cliente+item+uom
            static string Key4(string bu, string party, string item, string uom) =>
                $"{(bu ?? "").Trim().ToUpperInvariant()}|{(party ?? "").Trim().ToUpperInvariant()}|{(item ?? "").Trim().ToUpperInvariant()}|{(uom ?? "").Trim().ToUpperInvariant()}";

            exportRows = exportRows
                .GroupBy(r => Key4(r.BU, r.PartyCode, r.ItemNumber, r.Uom), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            // =========================
            // 5) XXORA: (A) existencia por BU+ITEM para el bucket (MD/PT)
            //     regla: si existe => NO-OP, si no => CREATE
            // =========================
            var itemsAll = exportRows
                .Select(r => T(r.ItemNumber))
                .Where(x => x != "")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var buAll = exportRows
                .Select(r => T(r.BU))
                .Where(x => x != "")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            static string KeyBUItem(string bu, string item) =>
                $"{(bu ?? "").Trim().ToUpperInvariant()}|{(item ?? "").Trim().ToUpperInvariant()}";

            var anyItemInXxora = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // key BU|ITEM bucket-filtrado

            foreach (var bu in buAll)
            {
                foreach (var chunk in itemsAll.Chunk(700))
                {
                    var q = _OracleContext.XXORA_DISCOUNT_LISTs
                        .AsNoTracking()
                        .Where(x => x.BU_NAME != null && x.ITEM_NUMBER != null && x.RULE_DISCOUNT_NAME != null)
                        .Where(x => x.BU_NAME.Trim().ToUpper() == bu.Trim().ToUpper())
                        .Where(x => chunk.Contains(x.ITEM_NUMBER))
                        .Where(x => x.RULE_DISCOUNT_NAME.Trim().ToUpper() == bucketName);

                    // según tipo
                    if (tipo == "promocional") q = q.Where(x => x.END_DATE != null);
                    else q = q.Where(x => x.END_DATE == null);

                    var rows = await q
                        .Select(x => x.ITEM_NUMBER)
                        .Distinct()
                        .ToListAsync();

                    foreach (var it in rows)
                    {
                        var item = T(it);
                        if (item == "") continue;
                        anyItemInXxora.Add(KeyBUItem(bu, item));
                    }
                }
            }

            string MdAction(string bu, string item) =>
                anyItemInXxora.Contains(KeyBUItem(bu, item)) ? ACTION_NOOP : ACTION_CREATE;

            // =========================
            // 5B) XXORA: mapa BU+cliente+item+uom para decidir CREATE/UPDATE/NO-OP en MR (y PT si hay updates)
            // =========================
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

                foreach (var chunk in items.Chunk(700))
                {
                    var q = _OracleContext.XXORA_DISCOUNT_LISTs
                        .AsNoTracking()
                        .Where(x => x.BU_NAME != null && x.PARTY_NUMBER != null && x.ITEM_NUMBER != null)
                        .Where(x => x.BU_NAME.Trim().ToUpper() == bu)
                        .Where(x => x.PARTY_NUMBER.Trim().ToUpper() == party)
                        .Where(x => chunk.Contains(x.ITEM_NUMBER))
                        .Where(x => x.RULE_DISCOUNT_NAME != null && x.RULE_DISCOUNT_NAME.Trim().ToUpper() == bucketName);

                    if (tipo == "promocional") q = q.Where(x => x.END_DATE != null);
                    else q = q.Where(x => x.END_DATE == null);

                    var xx = await q.Select(x => new
                    {
                        x.BU_NAME,
                        x.PARTY_NUMBER,
                        x.ITEM_NUMBER,
                        x.PRICING_UOM_CODE,
                        x.DISCOUNT_PRICE,
                        x.START_DATE,
                        x.END_DATE
                    }).ToListAsync();

                    foreach (var x in xx)
                    {
                        var k = Key4(T(x.BU_NAME), T(x.PARTY_NUMBER), T(x.ITEM_NUMBER), T(x.PRICING_UOM_CODE));
                        existingMap[k] = (x.DISCOUNT_PRICE, x.START_DATE, x.END_DATE);
                    }
                }
            }

            string DecideRuleAction(string bu, string party, string item, string uom, decimal newPrice, DateTime? newStart, DateTime? newEnd)
            {
                var k = Key4(bu, party, item, uom);

                if (!existingMap.TryGetValue(k, out var old))
                    return ACTION_CREATE;

                bool samePrice = decimal.Round(old.Price, 6) == decimal.Round(newPrice, 6);

                DateTime? os = old.Start?.Date;
                DateTime? ns = newStart?.Date;

                if (tipo == "fijo")
                {
                    bool sameStart = os == ns;
                    return (samePrice && sameStart) ? ACTION_NOOP : ACTION_UPDATE;
                }

                DateTime? oe = old.End?.Date;
                DateTime? ne = newEnd?.Date;
                bool sameDates = (os == ns) && (oe == ne);

                return (samePrice && sameDates) ? ACTION_NOOP : ACTION_UPDATE;
            }

            // items que requieren UPDATE en MR (cuando MD es NO-OP)
            var needsUpdateByBUItem = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rr in exportRows)
            {
                var bu = T(rr.BU);
                var itemKey = N(rr.ItemNumber);
                var party = T(rr.PartyCode);
                var uom = T(rr.Uom);

                if (bu == "" || itemKey == "" || party == "" || uom == "")
                    continue;

                var mdOp = MdAction(bu, itemKey);
                if (mdOp != ACTION_NOOP) continue;

                var act = DecideRuleAction(bu, party, itemKey, uom, rr.Valor, rr.Start, rr.End);
                if (act == ACTION_UPDATE)
                    needsUpdateByBUItem.Add(KeyBUItem(bu, itemKey));
            }

            // =========================
            // 6) Nombres clientes (VALUE_STRING1) SIN TRIM para no matar padding
            // =========================
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
                    .ToListAsync();

                foreach (var x in tmp)
                    if (!string.IsNullOrWhiteSpace(x.IDCLIENTE))
                        nombreClienteMap[T(x.IDCLIENTE)] = x.NOMBRE_CLIENTE ?? "";
            }

            // =========================
            // 7) Workbook (mismas hojas/headers/orden)
            // =========================
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

            int rDL = 2, rIt = 2, rPT = 2, rMD = 2, rMR = 2;

            // =========================
            // A) DiscountListsInterface: 1 línea NO-OP
            // =========================
            var globalStart = exportRows.Select(r => r.Start).Where(d => d.HasValue).Select(d => d.Value.Date).DefaultIfEmpty(DateTime.Today).Min();
            var dlStartStr = FormatDateDiscountListsLocal(globalStart);

            wsDL.Cell(rDL, 1).Value = batchName;
            wsDL.Cell(rDL, 2).Value = ACTION_NOOP;
            wsDL.Cell(rDL, 3).Value = sourceDiscountListId;
            wsDL.Cell(rDL, 4).Value = name;
            wsDL.Cell(rDL, 5).Value = description;
            wsDL.Cell(rDL, 6).Value = businessUnitId;
            wsDL.Cell(rDL, 7).Value = businessUnitName;
            wsDL.Cell(rDL, 8).Value = currencyCode;
            wsDL.Cell(rDL, 9).Value = dlStartStr;
            wsDL.Cell(rDL, 10).Value = "";
            wsDL.Cell(rDL, 11).Value = statusCode;
            rDL++;

            // =========================
            // B) DiscountListItemsInterface: 1 fila por artículo único, NO-OP (como flujos)
            // =========================
            var itemsUnique = exportRows
                .Select(r => N(r.ItemNumber))
                .Where(x => x != "")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            // UOM por item (primera encontrada)
            var uomByItem = exportRows
                .GroupBy(r => N(r.ItemNumber), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => T(g.Select(x => x.Uom).FirstOrDefault(u => !string.IsNullOrWhiteSpace(T(u))) ?? ""),
                    StringComparer.OrdinalIgnoreCase
                );

            foreach (var itemKey in itemsUnique)
            {
                var sdlid = $"SDLID_{itemKey}";
                var uom = uomByItem.TryGetValue(itemKey, out var uu) ? uu : "";

                wsItems.Cell(rIt, 1).Value = ACTION_NOOP;
                wsItems.Cell(rIt, 2).Value = sourceDiscountListId;
                wsItems.Cell(rIt, 3).Value = sdlid;
                wsItems.Cell(rIt, 4).Value = "ITEM";
                wsItems.Cell(rIt, 5).Value = itemKey;
                wsItems.Cell(rIt, 6).Value = "";
                wsItems.Cell(rIt, 7).Value = "";
                wsItems.Cell(rIt, 8).Value = uom;
                wsItems.Cell(rIt, 9).Value = "ORA_BUY";
                rIt++;
            }

            // =========================
            // C) PricingTerms + MatrixDimensions: por item (1 term por item para el bucket seleccionado)
            // =========================
            var rowsByItem = exportRows
                .GroupBy(r => N(r.ItemNumber), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var itemKey in itemsUnique)
            {
                var itemRows = rowsByItem.TryGetValue(itemKey, out var list) ? list : new List<(string BU, string PartyCode, string ItemNumber, string Uom, decimal Valor, DateTime? Start, DateTime? End)>();
                if (itemRows.Count == 0) continue;

                // asumimos BU único (LANCO_CR); si vinieran varios, tomamos el primero
                var bu = T(itemRows[0].BU);
                var sdlid = $"SDLID_{itemKey}";
                var stid = BuildStid(itemKey, bucketName);
                var smid = BuildSmid(stid);

                var mdOp = MdAction(bu, itemKey);

                // PT op: si el item no existe en XXORA(bucket) => CREATE
                // si existe y hay updates en MR => UPDATE
                // si no => NO-OP
                var ptOp =
                    mdOp == ACTION_CREATE
                        ? ACTION_CREATE
                        : (needsUpdateByBUItem.Contains(KeyBUItem(bu, itemKey)) ? ACTION_UPDATE : ACTION_NOOP);

                var termStart = itemRows.Select(x => x.Start).Where(d => d.HasValue).Select(d => d.Value).DefaultIfEmpty(DateTime.Today).Min();
                DateTime? termEnd = null;

                if (tipo == "promocional")
                {
                    // promo: end real (máximo end por item)
                    termEnd = itemRows
                            .Select(x => x.End)            // IEnumerable<DateTime?>
                            .Where(d => d.HasValue)        // IEnumerable<DateTime?>
                            .DefaultIfEmpty()              // mete null si quedó vacío
                            .Max();                        // devuelve DateTime?
                }
                // fijo: end vacío

                wsPT.Cell(rPT, 1).Value = ptOp;
                wsPT.Cell(rPT, 2).Value = sourceDiscountListId; // root
                wsPT.Cell(rPT, 3).Value = sdlid;                // parent
                wsPT.Cell(rPT, 4).Value = stid;                 // term id
                wsPT.Cell(rPT, 5).Value = bucketName;           // NAME = CLIENTE / PROMOCION
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
                wsPT.Cell(rPT, 17).Value = FormatDateLocal(termStart);
                wsPT.Cell(rPT, 18).Value = FormatDateLocal(termEnd);
                rPT++;

                // MatrixDimensions: 4 filas por term (bucket variable, NO hardcode CLIENTE)
                var dims = new (string Name, string Type, string Column)[]
                {
                    ("Adjustment Amount", "Result",    "VALUE_STRING3"),
                    ("Adjustment Basis",  "Result",    "VALUE_STRING4"),
                    ("Adjustment Type",   "Result",    "VALUE_STRING2"),
                    (bucketName,          "Condition", "VALUE_STRING1"),
                };

                foreach (var d in dims)
                {
                    wsMD.Cell(rMD, 1).Value = mdOp;              // CREATE o NO-OP
                    wsMD.Cell(rMD, 2).Value = sourceDiscountListId;
                    wsMD.Cell(rMD, 3).Value = stid;              // parent = term
                    wsMD.Cell(rMD, 4).Value = smid;              // matrix id
                    wsMD.Cell(rMD, 5).Value = d.Name;
                    wsMD.Cell(rMD, 6).Value = d.Type;
                    wsMD.Cell(rMD, 7).Value = d.Column;
                    rMD++;
                }
            }

            // =========================
            // D) MatrixRules: por fila exportable
            // =========================
            foreach (var rr in exportRows
                .OrderBy(x => N(x.ItemNumber))
                .ThenBy(x => N(x.PartyCode))
                .ThenBy(x => N(x.Uom)))
            {
                var bu = T(rr.BU);
                var itemKey = N(rr.ItemNumber);
                var partyCode = T(rr.PartyCode);
                var uom = T(rr.Uom);

                if (bu == "" || itemKey == "" || partyCode == "" || uom == "")
                    continue;

                var stid = BuildStid(itemKey, bucketName);
                var smid = BuildSmid(stid);

                var mdOp = MdAction(bu, itemKey);

                // MR op
                var mrOp =
                    mdOp == ACTION_CREATE
                        ? ACTION_CREATE
                        : DecideRuleAction(bu, partyCode, itemKey, uom, rr.Valor, rr.Start, rr.End);

                // SRID con regla: articulo + codCliente + (CLIENTE/PROMOCION) + medida
                var srid = BuildSridMr(itemKey, partyCode, bucketName);

                // VALUE_STRING1: nombre cliente (sin Trim para no matar padding)
                var partyName = nombreClienteMap.TryGetValue(partyCode, out var nm) ? (nm ?? "") : "";

                wsMR.Cell(rMR, 1).Value = mrOp;
                wsMR.Cell(rMR, 2).Value = sourceDiscountListId;
                wsMR.Cell(rMR, 3).Value = smid;
                wsMR.Cell(rMR, 4).Value = srid;

                wsMR.Cell(rMR, 5).Value = partyName; // VALUE_STRING1
                wsMR.Cell(rMR, 6).Value = "DISCOUNT_PERCENT";
                wsMR.Cell(rMR, 7).Value = rr.Valor.ToString(CultureInfo.InvariantCulture); // % con punto
                wsMR.Cell(rMR, 8).Value = "Adjustment Basis";

                // columnas extendidas (como flujos)
                wsMR.Cell(rMR, 15).Value = FormatDateLocal(rr.Start);
                wsMR.Cell(rMR, 16).Value = FormatDateLocal(rr.End);
                rMR++;
            }

            // Validación mínima: debe haber por lo menos una regla
            if (rMR == 2)
                return BadRequest("No se generaron reglas (MatrixRulesInterface) para exportar.");

            // =========================
            // 8) Marcar como generado (GENERADO='S') y retornar ZIP
            // =========================
            await using (var trx = await _OracleContext.Database.BeginTransactionAsync())
            {
                try
                {
                    foreach (var h in headers)
                    {
                        var db = await _OracleContext.PREDESCUENTOs
                            .FirstOrDefaultAsync(p =>
                                p.BU_NOMBRE == h.BU_NOMBRE &&
                                p.COD_CLIENTE == h.COD_CLIENTE &&
                                p.CONSECUTIVO == h.CONSECUTIVO);

                        if (db != null)
                        {
                            db.GENERADO = "S";
                            _OracleContext.PREDESCUENTOs.Update(db);
                        }
                    }

                    await _OracleContext.SaveChangesAsync();
                    await trx.CommitAsync();
                }
                catch
                {
                    await trx.RollbackAsync();
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
            var zipName = "Descuentos_COSTARICA_ALL.zip";
            return File(zipBytes, "application/zip", zipName);
        }


        static string NormalizeTipo(string? s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();

            // soporta: "promocional", "promocion", "descuento promocional", etc
            if (s.Contains("promo")) return "promocional";

            // soporta: "fijo", "descuento fijo", "activos", "activo"
            if (s.Contains("fijo") || s.Contains("activo")) return "fijo";

            return "";
        }

        private async Task<Dictionary<XxoraKey, XxoraSnap>> LoadXxoraMapFastAsync(
   string buName,
   IEnumerable<string> partyNumbers,
   IEnumerable<string> itemNumbers,
   IEnumerable<string> uoms,
   bool soloFijosEndNull // true => END_DATE == null
)
        {
            static string Norm(string? s) => (s ?? "").Trim().ToUpperInvariant();

            var buNorm = Norm(buName);

            var partyList = partyNumbers
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(Norm)
                .Distinct()
                .ToList();

            var itemList = itemNumbers
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(Norm)
                .Distinct()
                .ToList();

            // Si querés filtrar por UOM en SQL (recomendado si son pocas)
            var uomList = uoms
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(Norm)
                .Distinct()
                .ToList();

            var result = new Dictionary<XxoraKey, XxoraSnap>();

            foreach (var partyChunk in ChunkList(partyList, 900))
                foreach (var itemChunk in ChunkList(itemList, 900))
                {
                    // OJO: aquí NO usamos Norm(...) dentro del query (eso era el error).
                    // Usamos Trim().ToUpper() (traducible a SQL) y comparamos contra strings ya normalizados.
                    var q = _OracleContext.XXORA_DISCOUNT_LISTs
                        .AsNoTracking()
                        .Where(x => x.BU_NAME != null && x.BU_NAME.Trim().ToUpper() == buNorm)
                        .Where(x => x.PARTY_NUMBER != null && partyChunk.Contains(x.PARTY_NUMBER.Trim().ToUpper()))
                        .Where(x => x.ITEM_NUMBER != null && itemChunk.Contains(x.ITEM_NUMBER.Trim().ToUpper()));

                    q = soloFijosEndNull
                        ? q.Where(x => x.END_DATE == null)
                        : q.Where(x => x.END_DATE != null);

                    // Filtrado por UOM dentro del mismo query (NO multiplica queries)
                    if (uomList.Count > 0)
                    {
                        q = q.Where(x => x.PRICING_UOM_CODE != null
                                      && uomList.Contains(x.PRICING_UOM_CODE.Trim().ToUpper()));
                    }

                    var rows = await q
                        .Select(x => new
                        {
                            Party = x.PARTY_NUMBER,
                            Item = x.ITEM_NUMBER,
                            Uom = x.PRICING_UOM_CODE,
                            Start = x.START_DATE,
                            End = x.END_DATE,
                            Disc = x.DISCOUNT_PRICE
                        })
                        .ToListAsync();

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

                        // Solución al error del ??:
                        // Convertimos a DateTime? para poder usar ?? aunque el origen sea DateTime
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

        private static byte[] ZipWorksheetsAsCsv(XLWorkbook wb, IEnumerable<string> worksheetNames)
        {
            // Sufijos por hoja (solo donde ocupás el relleno + END)
            var suffixBySheet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // PROMO
                ["DiscountListItemsInterface"] = ",,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,END",

                ["DiscountListSetsInterface"] = ",END",

                ["DiscountListsInterface"] = ",,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,END",

                ["MatrixDimensionsInterface"] = ",END",

                ["MatrixRulesInterface"] = ",,,,,,,,,,,,,,,,END",

                ["PricingTermsInterface"] = ",,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,END",

            };

            using var outMs = new MemoryStream();
            using (var zip = new ZipArchive(outMs, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var sheetName in worksheetNames)
                {
                    var ws = wb.Worksheet(sheetName);

                    var entry = zip.CreateEntry($"{sheetName}.csv", CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                    var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
                    var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

                    if (lastRow < 2 || lastCol < 1)
                        continue;

                    // ✅ Quitar encabezados: empezamos en la fila 2
                    for (int r = 2; r <= lastRow; r++)
                    {
                        var fields = new string[lastCol];
                        bool hasAny = false;

                        for (int c = 1; c <= lastCol; c++)
                        {
                            var raw = ws.Cell(r, c).GetValue<string>() ?? "";
                            if (!string.IsNullOrEmpty(raw)) hasAny = true;
                            fields[c - 1] = CsvEscape(raw);
                        }

                        // opcional: si querés omitir filas totalmente vacías
                        if (!hasAny) continue;

                        var line = string.Join(",", fields);

                        // ✅ Agregar relleno + END solo en las hojas requeridas
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
            // CSV estándar: solo comillas si hace falta
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

            var sb = new StringBuilder(8192);

            for (int r = firstRow; r <= lastRow; r++)
            {
                for (int c = firstCol; c <= lastCol; c++)
                {
                    var cell = ws.Cell(r, c);
                    var field = GetCellCsvValue(cell);
                    sb.Append(EscapeCsv(field));

                    if (c < lastCol) sb.Append(',');
                }
                sb.Append("\r\n");
            }

            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(sb.ToString());
        }

        static string FormatDateDiscountListsLocal(DateTime? fecha) =>
       fecha.HasValue
           ? fecha.Value.Date.ToString("yyyy/MM/dd HH:mm:ss") // -> siempre 00:00:00
           : string.Empty;

        private sealed class XxoraSnap
        {
            public DateTime? Start { get; init; }
            public DateTime? End { get; init; }
            public decimal? DiscountValue { get; init; } // el valor que vas a comparar
        }


        // Normaliza a precisión de segundos (porque tus CSV van a "00:00:00")
        private static DateTime? NormalizeDt(DateTime? dt)
        {
            if (dt == null) return null;
            var v = dt.Value;
            return new DateTime(v.Year, v.Month, v.Day, v.Hour, v.Minute, v.Second);
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

        private static string CalcAction(XxoraSnap? existing, DateTime start, DateTime? end, decimal discountValue)
        {
            if (existing == null) return "CREATE";

            var s1 = NormalizeDt(existing.Start);
            var e1 = NormalizeDt(existing.End);
            var s2 = NormalizeDt(start);
            var e2 = NormalizeDt(end);

            var v1 = NormalizeDec(existing.DiscountValue);
            var v2 = NormalizeDec(discountValue);

            bool same =
                s1 == s2 &&
                e1 == e2 &&
                v1 == v2;

            return same ? "NO-OP" : "UPDATE";
        }
        private static decimal? NormalizeDec(decimal? d)
        {
            if (d == null) return null;
            return decimal.Round(d.Value, 6);
        }

        private static string GetCellCsvValue(IXLCell cell)
        {
            if (cell.IsEmpty())
                return "";

            var v = cell.Value;

            switch (v.Type)
            {
                case XLDataType.Number:
                    return v.GetNumber().ToString(CultureInfo.InvariantCulture);

                case XLDataType.DateTime:
                    return v.GetDateTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);

                case XLDataType.Boolean:
                    return v.GetBoolean() ? "TRUE" : "FALSE";

                default:
                    return v.GetText() ?? "";
            }
        }

        private static string EscapeCsv(string input)
        {
            input ??= "";

            // Si tiene coma, comillas o saltos de línea -> se entrecomilla y se escapan comillas
            bool mustQuote =
                input.Contains(',') ||
                input.Contains('"') ||
                input.Contains('\n') ||
                input.Contains('\r');

            if (!mustQuote) return input;

            return "\"" + input.Replace("\"", "\"\"") + "\"";
        }
    }
}
*/