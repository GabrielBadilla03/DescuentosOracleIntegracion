using DocumentFormat.OpenXml.Office2013.Drawing.ChartStyle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Oracle.ManagedDataAccess.Client;
using SolicitudesDescuentos.Data;
using SolicitudesDescuentos.ModelsOracle;
using SolicitudesDescuentos.Services;
using System.Data;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;


namespace SolicitudesDescuentos.Controllers
{
    [Authorize]
    public class PredescuentosController : Controller
    {
        private readonly OracleContext _OracleContext;
        private readonly record struct XxoraKey(string PartyNumber, string ItemNumber, string Uom);


        public PredescuentosController(OracleContext oracleContext)
        {
            _OracleContext = oracleContext;
        }

        // =========================================================
        // REGLA GLOBAL: ART_NO_PROMO ACTIVO = artículo bloqueado
        // =========================================================
        private async Task<HashSet<string>> ObtenerArticulosNoPromoActivosAsync(
            IEnumerable<string> itemNumbers,
            string? buNombre = "LANCO_CR",
            string organizationCode = "CR_3",
            CancellationToken ct = default)
        {
            static string N(string? s) => (s ?? "").Trim().ToUpperInvariant();

            var buKey = N(buNombre);
            var orgKey = N(organizationCode);

            var items = itemNumbers
                .Select(N)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var bloqueados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            const int chunkSize = 900;

            for (int i = 0; i < items.Count; i += chunkSize)
            {
                var chunk = items.Skip(i).Take(chunkSize).ToList();

                var rows = await _OracleContext.ART_NO_PROMOs
                    .AsNoTracking()
                    .Where(a =>
                        a.BU_NAME != null &&
                        a.ORGANIZATION_CODE != null &&
                        a.ITEM_NUMBER != null &&
                        a.ESTADO != null &&
                        a.BU_NAME.Trim().ToUpper() == buKey &&
                        a.ORGANIZATION_CODE.Trim().ToUpper() == orgKey &&
                        a.ESTADO.Trim().ToUpper() == "ACTIVO" &&
                        chunk.Contains(a.ITEM_NUMBER.Trim().ToUpper()))
                    .Select(a => a.ITEM_NUMBER)
                    .Distinct()
                    .ToListAsync(ct);

                foreach (var item in rows)
                {
                    var key = N(item);
                    if (!string.IsNullOrWhiteSpace(key))
                        bloqueados.Add(key);
                }
            }

            return bloqueados;
        }


        // =========================================================
        // REGLA GLOBAL 2: ACCEPTADESCUENTO = S es obligatorio.
        // XXORA_ITEM_MASTER continúa usando LCR_3.
        // =========================================================
        private async Task<HashSet<string>> ObtenerArticulosNoAceptanDescuentoAsync(
            IEnumerable<string> itemNumbers,
            string? buNombre = "LANCO_CR",
            string organizationCode = "LCR_3",
            CancellationToken ct = default)
        {
            static string N(string? s) => (s ?? "").Trim().ToUpperInvariant();

            var buKey = N(buNombre);
            var orgKey = N(organizationCode);

            var items = itemNumbers
                .Select(N)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var aceptados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            const int chunkSize = 900;

            for (int i = 0; i < items.Count; i += chunkSize)
            {
                var chunk = items.Skip(i).Take(chunkSize).ToList();

                var rows = await _OracleContext.XXORA_ITEM_MASTERs
                    .AsNoTracking()
                    .Where(x =>
                        x.BU_NAME != null &&
                        x.ORGANIZATION_CODE != null &&
                        x.ITEM_NUMBER != null &&
                        x.ACCEPTADESCUENTO != null &&
                        x.BU_NAME.Trim().ToUpper() == buKey &&
                        x.ORGANIZATION_CODE.Trim().ToUpper() == orgKey &&
                        x.ACCEPTADESCUENTO.Trim().ToUpper() == "S" &&
                        chunk.Contains(x.ITEM_NUMBER.Trim().ToUpper()))
                    .Select(x => x.ITEM_NUMBER)
                    .Distinct()
                    .ToListAsync(ct);

                foreach (var item in rows)
                {
                    var key = N(item);
                    if (!string.IsNullOrWhiteSpace(key))
                        aceptados.Add(key);
                }
            }

            return items
                .Where(x => !aceptados.Contains(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        // =========================================================
        // PROMOCIONAL: el artículo debe tener descuento CLIENTE
        // vigente hoy.
        // =========================================================
        private async Task<HashSet<string>> ObtenerArticulosSinDescuentoClienteVigenteAsync(
            IEnumerable<string> itemNumbers,
            string? codCliente,
            string? buNombre = "LANCO_CR",
            CancellationToken ct = default)
        {
            static string N(string? s) => (s ?? "").Trim().ToUpperInvariant();

            var items = itemNumbers
                .Select(N)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (items.Count == 0)
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var clienteKey = N(codCliente);
            var buKey = N(buNombre);

            if (string.IsNullOrWhiteSpace(clienteKey))
                return items.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var vigentes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hoy = DateTime.Today;
            const int chunkSize = 900;

            for (int i = 0; i < items.Count; i += chunkSize)
            {
                var chunk = items.Skip(i).Take(chunkSize).ToList();

                var rows = await _OracleContext.XXORA_DISCOUNT_LISTs
                    .AsNoTracking()
                    .Where(x =>
                        x.BU_NAME != null &&
                        x.PARTY_NUMBER != null &&
                        x.ITEM_NUMBER != null &&
                        x.RULE_DISCOUNT_NAME != null &&
                        x.BU_NAME.Trim().ToUpper() == buKey &&
                        x.PARTY_NUMBER.Trim().ToUpper() == clienteKey &&
                        x.RULE_DISCOUNT_NAME.Trim().ToUpper() == "CLIENTE" &&
                        x.START_DATE <= hoy &&
                        (x.END_DATE == null || x.END_DATE >= hoy) &&
                        chunk.Contains(x.ITEM_NUMBER.Trim().ToUpper()))
                    .Select(x => x.ITEM_NUMBER)
                    .Distinct()
                    .ToListAsync(ct);

                foreach (var item in rows)
                {
                    var key = N(item);
                    if (!string.IsNullOrWhiteSpace(key))
                        vigentes.Add(key);
                }
            }

            return items
                .Where(x => !vigentes.Contains(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        // =========================================================
        // Línea/clase solo es válida si contiene al menos un
        // artículo elegible hoy.
        // =========================================================
        private async Task<bool> ExisteArticuloElegibleEnScopeAsync(
            string? codLinea,
            string? codClase,
            string? buNombre,
            string? codCliente,
            string? tipoDescuento,
            CancellationToken ct = default)
        {
            static string N(string? s) => (s ?? "").Trim().ToUpperInvariant();

            var lineaKey = N(codLinea);
            var claseKey = N(codClase);
            var buKey = N(buNombre);

            if (string.IsNullOrWhiteSpace(lineaKey))
                return false;

            const string orgItemMaster = "LCR_3";
            const string orgNoPromo = "CR_3";

            bool esPromo =
                !string.IsNullOrWhiteSpace(tipoDescuento) &&
                tipoDescuento.Contains("promocional", StringComparison.OrdinalIgnoreCase);

            var query = _OracleContext.INV_ARTICULOs
                .AsNoTracking()
                .Where(a =>
                    a.COD_ARTICULO != null &&
                    a.COD_LINEA != null &&
                    a.COD_LINEA.Trim().ToUpper() == lineaKey &&
                    (string.IsNullOrWhiteSpace(claseKey) ||
                     (a.COD_CLASE != null && a.COD_CLASE.Trim().ToUpper() == claseKey)) &&
                    _OracleContext.XXORA_ITEM_MASTERs.AsNoTracking().Any(x =>
                        x.BU_NAME != null &&
                        x.ORGANIZATION_CODE != null &&
                        x.ITEM_NUMBER != null &&
                        x.ACCEPTADESCUENTO != null &&
                        x.BU_NAME.Trim().ToUpper() == buKey &&
                        x.ORGANIZATION_CODE.Trim().ToUpper() == orgItemMaster &&
                        x.ITEM_NUMBER.Trim().ToUpper() == a.COD_ARTICULO.Trim().ToUpper() &&
                        x.ACCEPTADESCUENTO.Trim().ToUpper() == "S") &&
                    !_OracleContext.ART_NO_PROMOs.AsNoTracking().Any(np =>
                        np.BU_NAME != null &&
                        np.ORGANIZATION_CODE != null &&
                        np.ITEM_NUMBER != null &&
                        np.ESTADO != null &&
                        np.BU_NAME.Trim().ToUpper() == buKey &&
                        np.ORGANIZATION_CODE.Trim().ToUpper() == orgNoPromo &&
                        np.ITEM_NUMBER.Trim().ToUpper() == a.COD_ARTICULO.Trim().ToUpper() &&
                        np.ESTADO.Trim().ToUpper() == "ACTIVO")
                );

            if (esPromo)
            {
                var clienteKey = N(codCliente);
                if (string.IsNullOrWhiteSpace(clienteKey))
                    return false;

                var hoy = DateTime.Today;

                query = query.Where(a =>
                    _OracleContext.XXORA_DISCOUNT_LISTs.AsNoTracking().Any(x =>
                        x.BU_NAME != null &&
                        x.PARTY_NUMBER != null &&
                        x.ITEM_NUMBER != null &&
                        x.RULE_DISCOUNT_NAME != null &&
                        x.BU_NAME.Trim().ToUpper() == buKey &&
                        x.PARTY_NUMBER.Trim().ToUpper() == clienteKey &&
                        x.ITEM_NUMBER.Trim().ToUpper() == a.COD_ARTICULO.Trim().ToUpper() &&
                        x.RULE_DISCOUNT_NAME.Trim().ToUpper() == "CLIENTE" &&
                        x.START_DATE <= hoy &&
                        (x.END_DATE == null || x.END_DATE >= hoy)
                    ));
            }

            return await query.AnyAsync(ct);
        }

        private async Task EliminarPredesclaseOracleClienteAsync(
            string codCliente,
            CancellationToken ct = default)
        {
            const string organizationCode = "CR_3";
            codCliente = (codCliente ?? "").Trim();

            if (string.IsNullOrWhiteSpace(codCliente))
                return;

            await _OracleContext.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM PREDESCLASEORACLE
                 WHERE TRIM(ORGANIZATION_CODE) = {organizationCode}
                   AND TRIM(IDCLIENTE) = {codCliente}
            ", ct);
        }

        // GET: Predescuentos
        // GET: Predescuentos
        [HttpGet]
        public async Task<IActionResult> Index(string? codCliente, DateTime? fechaInicio, DateTime? fechaFin)
        {
            try
            {
                codCliente = (codCliente ?? "").Trim();

                var usuarioActual = (User.Identity?.Name ?? "").Trim();
                var esPriceEditor = User.IsInRole("PRICE_EDITOR");

                var predescuentosQuery = _OracleContext.PREDESCUENTOs
                    .AsNoTracking()
                    .AsQueryable();

                // Si NO es PRICE_EDITOR, solo puede ver las solicitudes creadas por él
                if (!esPriceEditor)
                {
                    var usuarioUpper = usuarioActual.ToUpper();

                    predescuentosQuery = predescuentosQuery.Where(p =>
                        p.INGRESADO_POR != null &&
                        p.INGRESADO_POR.Trim().ToUpper() == usuarioUpper
                    );
                }

                // Este filtro por cliente solo se aplica sobre lo que ya puede ver el usuario
                if (!string.IsNullOrWhiteSpace(codCliente))
                {
                    predescuentosQuery = predescuentosQuery.Where(p => p.COD_CLIENTE == codCliente);
                }

                // Filtrar por FECHASOLICITUD dentro del rango
                if (fechaInicio.HasValue)
                {
                    var inicio = fechaInicio.Value.Date;

                    predescuentosQuery = predescuentosQuery.Where(p =>
                        p.FECHASOLICITUD >= inicio);
                }

                if (fechaFin.HasValue)
                {
                    var finExclusivo = fechaFin.Value.Date.AddDays(1);

                    predescuentosQuery = predescuentosQuery.Where(p =>
                        p.FECHASOLICITUD < finExclusivo);
                }

                var predescuentos = await predescuentosQuery.ToListAsync();

                predescuentos = predescuentos
                    .OrderByDescending(p => int.TryParse(p.CONSECUTIVO, out var consec) ? consec : 0)
                    .ToList();

                var codClientes = predescuentos
                    .Select(p => p.COD_CLIENTE)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();

                var nombresClientes = await _OracleContext.GEN_CLIENTEs
                    .AsNoTracking()
                    .Where(c => codClientes.Contains(c.IDCLIENTE))
                    .ToDictionaryAsync(c => c.IDCLIENTE, c => c.NOMBRE_CLIENTE);

                ViewBag.NombresClientes = nombresClientes;
                ViewBag.FiltroCodCliente = codCliente;
                ViewBag.FiltroFechaInicio = fechaInicio?.ToString("yyyy-MM-dd");
                ViewBag.FiltroFechaFin = fechaFin?.ToString("yyyy-MM-dd");
                ViewBag.EsPriceEditor = esPriceEditor;

                return View(predescuentos);
            }
            catch (OracleException)
            {
                TempData["ErrorMessage"] = "Hubo un problema al comunicarse con la base de datos.";
                return RedirectToAction("DatabaseError", "Home");
            }
            catch (Exception)
            {
                TempData["ErrorMessage"] = "Ocurrió un error inesperado.";
                return RedirectToAction("Error", "Home");
            }
        }

        [HttpGet]
        public async Task<IActionResult> ListarArticulosExcluidos(string? filtro)
        {
            const string bu = "LANCO_CR";
            const string org = "LCR_3";

            static string T(string? s) => (s ?? "").Trim();

            filtro = T(filtro);
            var filtroKey = filtro.ToUpperInvariant();

            var query = _OracleContext.XXORA_ITEM_MASTERs
                .AsNoTracking()
                .Where(x =>
                    x.BU_NAME != null &&
                    x.ORGANIZATION_CODE != null &&
                    x.ITEM_NUMBER != null &&
                    x.ACCEPTADESCUENTO != null &&
                    x.BU_NAME.Trim().ToUpper() == bu &&
                    x.ORGANIZATION_CODE.Trim().ToUpper() == org &&
                    x.ACCEPTADESCUENTO.Trim().ToUpper() == "N"
                );

            if (!string.IsNullOrWhiteSpace(filtro))
            {
                query = query.Where(x =>
                    ((x.ITEM_NUMBER ?? "").Trim().ToUpper().Contains(filtroKey)) ||
                    ((x.DESCRIPTION ?? "").Trim().ToUpper().Contains(filtroKey))
                );
            }

            var data = await query
                .Select(x => new
                {
                    itemNumber = (x.ITEM_NUMBER ?? "").Trim(),
                    descripcion = (x.DESCRIPTION ?? "").Trim(),
                    codLinea = (x.CATEGORY_CODE ?? "").Trim(),
                    desLinea = (x.CATEGORY_NAME ?? "").Trim(),
                    codClase = (x.SUBCATEGORY_CODE ?? "").Trim(),
                    desClase = (x.SUBCATEGORY_NAME ?? "").Trim(),
                    medida = (x.PRIMARY_UOM_CODE ?? "").Trim(),
                    aceptaDescuento = (x.ACCEPTADESCUENTO ?? "").Trim()
                })
                .OrderBy(x => x.itemNumber)
                .Take(500)
                .ToListAsync();

            return Json(data);
        }

        [HttpGet]
        public async Task<IActionResult> BuscarArticulosParaExcluir(
            string? codLinea,
            string? codClase,
            string? filtro,
            string? medida)
        {
            const string bu = "LANCO_CR";
            const string org = "LCR_3";

            static string T(string? s) => (s ?? "").Trim();

            codLinea = T(codLinea);
            codClase = T(codClase);
            filtro = T(filtro);
            medida = T(medida);

            bool hayLinea = !string.IsNullOrWhiteSpace(codLinea);
            bool hayClase = !string.IsNullOrWhiteSpace(codClase);
            bool hayMedida = !string.IsNullOrWhiteSpace(medida);
            bool hayFiltro = !string.IsNullOrWhiteSpace(filtro) && filtro.Length >= 2;

            if (!hayLinea && !hayClase && !hayMedida && !hayFiltro)
                return Json(Array.Empty<object>());

            var lineaKey = codLinea.ToUpperInvariant();
            var claseKey = codClase.ToUpperInvariant();
            var medidaKey = medida.ToUpperInvariant();
            var filtroKey = filtro.ToUpperInvariant();

            var query = _OracleContext.XXORA_ITEM_MASTERs
                .AsNoTracking()
                .Where(x =>
                    x.BU_NAME != null &&
                    x.ORGANIZATION_CODE != null &&
                    x.ITEM_NUMBER != null &&
                    x.ACCEPTADESCUENTO != null &&
                    x.BU_NAME.Trim().ToUpper() == bu &&
                    x.ORGANIZATION_CODE.Trim().ToUpper() == org &&
                    x.ACCEPTADESCUENTO.Trim().ToUpper() == "S"
                );

            if (hayLinea)
            {
                query = query.Where(x =>
                    x.CATEGORY_CODE != null &&
                    x.CATEGORY_CODE.Trim().ToUpper() == lineaKey);
            }

            if (hayClase)
            {
                query = query.Where(x =>
                    x.SUBCATEGORY_CODE != null &&
                    x.SUBCATEGORY_CODE.Trim().ToUpper() == claseKey);
            }

            if (hayMedida)
            {
                query = query.Where(x =>
                    x.PRIMARY_UOM_CODE != null &&
                    x.PRIMARY_UOM_CODE.Trim().ToUpper() == medidaKey);
            }

            if (hayFiltro)
            {
                query = query.Where(x =>
                    ((x.ITEM_NUMBER ?? "").Trim().ToUpper().Contains(filtroKey)) ||
                    ((x.DESCRIPTION ?? "").Trim().ToUpper().Contains(filtroKey)));
            }

            var data = await query
                .Select(x => new
                {
                    itemNumber = (x.ITEM_NUMBER ?? "").Trim(),
                    descripcion = (x.DESCRIPTION ?? "").Trim(),
                    codLinea = (x.CATEGORY_CODE ?? "").Trim(),
                    desLinea = (x.CATEGORY_NAME ?? "").Trim(),
                    codClase = (x.SUBCATEGORY_CODE ?? "").Trim(),
                    desClase = (x.SUBCATEGORY_NAME ?? "").Trim(),
                    medida = (x.PRIMARY_UOM_CODE ?? "").Trim(),
                    aceptaDescuento = (x.ACCEPTADESCUENTO ?? "").Trim()
                })
                .OrderBy(x => x.itemNumber)
                .Take(500)
                .ToListAsync();

            return Json(data);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AgregarArticuloExcluido(string itemNumber)
        {
            const string bu = "LANCO_CR";
            const string org = "CR_3";

            itemNumber = (itemNumber ?? "").Trim();

            if (string.IsNullOrWhiteSpace(itemNumber))
            {
                Response.StatusCode = 400;
                return Json(new { ok = false, mensaje = "Debe indicar un artículo." });
            }

            var itemKey = itemNumber.ToUpperInvariant();

            var existe = await _OracleContext.XXORA_ITEM_MASTERs
                .AsNoTracking()
                .AnyAsync(x =>
                    x.BU_NAME != null &&
                    x.ORGANIZATION_CODE != null &&
                    x.ITEM_NUMBER != null &&
                    x.ACCEPTADESCUENTO != null &&
                    x.BU_NAME.Trim().ToUpper() == bu &&
                    x.ORGANIZATION_CODE.Trim().ToUpper() == org &&
                    x.ITEM_NUMBER.Trim().ToUpper() == itemKey &&
                    x.ACCEPTADESCUENTO.Trim().ToUpper() == "S"
                );

            if (!existe)
            {
                Response.StatusCode = 404;
                return Json(new
                {
                    ok = false,
                    mensaje = "No se encontró el artículo con ACEPTADESCUENTO = S."
                });
            }

            await _OracleContext.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE XXORA_ITEM_MASTER
                   SET ACCEPTADESCUENTO = 'N',
                       LAST_UPDATED_BY = {(User.Identity != null ? User.Identity.Name : "SYSTEM")},
        
                               LAST_UPDATE_DATE = SYSDATE
        
                         WHERE TRIM(UPPER(BU_NAME)) = {bu}
                    AND TRIM(UPPER(ORGANIZATION_CODE)) = {org}
                    AND TRIM(UPPER(ITEM_NUMBER)) = {itemKey}
                    AND TRIM(UPPER(ACCEPTADESCUENTO)) = 'S'
            ");

            return Json(new { ok = true, mensaje = "Artículo agregado a excluidos." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuitarArticuloExcluido(string itemNumber)
        {
            const string bu = "LANCO_CR";
            const string org = "CR_3";

            itemNumber = (itemNumber ?? "").Trim();

            if (string.IsNullOrWhiteSpace(itemNumber))
            {
                Response.StatusCode = 400;
                return Json(new { ok = false, mensaje = "Debe indicar un artículo." });
            }

            var itemKey = itemNumber.ToUpperInvariant();

            await _OracleContext.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE XXORA_ITEM_MASTER
                   SET ACCEPTADESCUENTO = 'S',
                       LAST_UPDATED_BY = {(User.Identity != null ? User.Identity.Name : "SYSTEM")},
        
                               LAST_UPDATE_DATE = SYSDATE
        
                         WHERE TRIM(UPPER(BU_NAME)) = {bu}
                    AND TRIM(UPPER(ORGANIZATION_CODE)) = {org}
                    AND TRIM(UPPER(ITEM_NUMBER)) = {itemKey}
                    AND TRIM(UPPER(ACCEPTADESCUENTO)) = 'N'
            ");

            return Json(new { ok = true, mensaje = "Artículo quitado de excluidos." });
        }

        [HttpGet]
        public async Task<IActionResult> CatalogoLineasArticulos()
        {
            var data = await _OracleContext.INV_LINEAs
                .AsNoTracking()
                .Where(x => x.CATEGORY_CODE != null && x.CATEGORY_CODE.Trim() != "")
                .Select(x => new
                {
                    codigo = x.CATEGORY_CODE!.Trim(),
                    descripcion = (x.CATEGORY_NAME ?? "").Trim()
                })
                .Distinct()
                .OrderBy(x => x.codigo)
                .ToListAsync();

            return Json(data);
        }

        [HttpGet]
        public async Task<IActionResult> CatalogoClasesArticulos(string? codLinea)
        {
            codLinea = (codLinea ?? "").Trim();

            var query = _OracleContext.INV_CLASEs
                .AsNoTracking()
                .Where(x => x.SUBCATEGORY_CODE != null && x.SUBCATEGORY_CODE.Trim() != "");

            if (!string.IsNullOrWhiteSpace(codLinea))
            {
                var lineaKey = codLinea.ToUpperInvariant();

                query = query.Where(x =>
                    x.CATEGORY_CODE != null &&
                    x.CATEGORY_CODE.Trim().ToUpper() == lineaKey);
            }

            var data = await query
                .Select(x => new
                {
                    codigo = x.SUBCATEGORY_CODE!.Trim(),
                    descripcion = (x.SUBCATEGORY_NAME ?? "").Trim()
                })
                .Distinct()
                .OrderBy(x => x.codigo)
                .ToListAsync();

            return Json(data);
        }

        [HttpGet]
        public async Task<IActionResult> CatalogoMedidasArticulos()
        {
            var data = await _OracleContext.INV_MEDIDAs
                .AsNoTracking()
                .Where(x => x.PRIMARY_UOM_CODE != null && x.PRIMARY_UOM_CODE.Trim() != "")
                .Select(x => new
                {
                    codigo = x.PRIMARY_UOM_CODE.Trim(),
                    descripcion = x.PRIMARY_UOM_CODE.Trim()
                })
                .Distinct()
                .OrderBy(x => x.codigo)
                .ToListAsync();

            return Json(data);
        }

        private async Task<(bool Ok, string Mensaje, int Procesados)> ActivarArticulosNoPromoAsync(
            IEnumerable<string>? itemNumbers,
            CancellationToken ct = default)
        {
            const string bu = "LANCO_CR";
            const string org = "CR_3";

            static string N(string? value) =>
                (value ?? string.Empty).Trim().ToUpperInvariant();

            var items = (itemNumbers ?? Enumerable.Empty<string>())
                .Select(N)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (items.Count == 0)
                return (false, "Debe seleccionar al menos un artículo para desactivar descuentos.", 0);

            // Validación defensiva: la vista usa INV_ARTICULO, pero el POST puede ser manipulado.
            var encontrados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            const int chunkSize = 900;

            for (var i = 0; i < items.Count; i += chunkSize)
            {
                var chunk = items.Skip(i).Take(chunkSize).ToList();

                var rows = await _OracleContext.INV_ARTICULOs
                    .AsNoTracking()
                    .Where(a =>
                        a.COD_ARTICULO != null &&
                        chunk.Contains(a.COD_ARTICULO.Trim().ToUpper()))
                    .Select(a => a.COD_ARTICULO)
                    .Distinct()
                    .ToListAsync(ct);

                foreach (var row in rows)
                {
                    var key = N(row);
                    if (!string.IsNullOrWhiteSpace(key))
                        encontrados.Add(key);
                }
            }

            var inexistentes = items
                .Where(x => !encontrados.Contains(x))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (inexistentes.Count > 0)
            {
                return (
                    false,
                    "No se encontraron en INV_ARTICULO los siguientes artículos: " +
                    string.Join(", ", inexistentes),
                    0);
            }

            await using var trx = await _OracleContext.Database.BeginTransactionAsync(ct);

            try
            {
                foreach (var itemKey in items)
                {
                    ct.ThrowIfCancellationRequested();

                    var header = await _OracleContext.ART_NO_PROMOs
                        .FirstOrDefaultAsync(a =>
                            a.BU_NAME != null &&
                            a.ORGANIZATION_CODE != null &&
                            a.ITEM_NUMBER != null &&
                            a.BU_NAME.Trim().ToUpper() == bu &&
                            a.ORGANIZATION_CODE.Trim().ToUpper() == org &&
                            a.ITEM_NUMBER.Trim().ToUpper() == itemKey,
                            ct);

                    if (header != null)
                    {
                        // Cada nueva activación de NO PROMO empieza con un respaldo limpio.
                        // El JOB volverá a copiar los descuentos actuales desde XXORA.
                        await _OracleContext.Database.ExecuteSqlInterpolatedAsync($@"
                            DELETE FROM ART_DET_NO_PROMO
                             WHERE TRIM(UPPER(BU_NAME)) = {bu}
                               AND TRIM(UPPER(ORGANIZATION_CODE)) = {org}
                               AND TRIM(UPPER(ITEM_NUMBER)) = {itemKey}
                        ", ct);

                        header.ESTADO = "Activo";
                        header.GENERADO = "N";
                        _OracleContext.ART_NO_PROMOs.Update(header);
                    }
                    else
                    {
                        _OracleContext.ART_NO_PROMOs.Add(new ART_NO_PROMO
                        {
                            BU_NAME = bu,
                            ORGANIZATION_CODE = org,
                            ITEM_NUMBER = itemKey,
                            ESTADO = "Activo",
                            GENERADO = "N"
                        });
                    }
                }

                await _OracleContext.SaveChangesAsync(ct);
                await trx.CommitAsync(ct);

                return (
                    true,
                    $"Se marcaron {items.Count} artículo(s) como Activo / N. " +
                    "El job copiará los descuentos actuales a ART_DET_NO_PROMO antes de generar los archivos.",
                    items.Count);
            }
            catch (Exception ex)
            {
                await trx.RollbackAsync(ct);
                _OracleContext.ChangeTracker.Clear();

                return (
                    false,
                    $"Error marcando artículos en ART_NO_PROMO: {ex.Message}",
                    0);
            }
        }

        private async Task<(bool Ok, string Mensaje, int Procesados)> InactivarArticulosNoPromoAsync(
            IEnumerable<string>? itemNumbers,
            CancellationToken ct = default)
        {
            const string bu = "LANCO_CR";
            const string org = "CR_3";

            static string N(string? value) =>
                (value ?? string.Empty).Trim().ToUpperInvariant();

            var items = (itemNumbers ?? Enumerable.Empty<string>())
                .Select(N)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (items.Count == 0)
                return (false, "Debe seleccionar al menos un artículo para reactivar descuentos.", 0);

            await using var trx = await _OracleContext.Database.BeginTransactionAsync(ct);

            try
            {
                var headers = new List<ART_NO_PROMO>();
                var sinHeader = new List<string>();
                var sinRespaldo = new List<string>();

                foreach (var itemKey in items)
                {
                    ct.ThrowIfCancellationRequested();

                    var header = await _OracleContext.ART_NO_PROMOs
                        .FirstOrDefaultAsync(a =>
                            a.BU_NAME != null &&
                            a.ORGANIZATION_CODE != null &&
                            a.ITEM_NUMBER != null &&
                            a.BU_NAME.Trim().ToUpper() == bu &&
                            a.ORGANIZATION_CODE.Trim().ToUpper() == org &&
                            a.ITEM_NUMBER.Trim().ToUpper() == itemKey,
                            ct);

                    if (header == null)
                    {
                        sinHeader.Add(itemKey);
                        continue;
                    }

                    var existeRespaldo = await _OracleContext.ART_DET_NO_PROMOs
                        .AsNoTracking()
                        .AnyAsync(d =>
                            d.BU_NAME != null &&
                            d.ORGANIZATION_CODE != null &&
                            d.ITEM_NUMBER != null &&
                            d.BU_NAME.Trim().ToUpper() == bu &&
                            d.ORGANIZATION_CODE.Trim().ToUpper() == org &&
                            d.ITEM_NUMBER.Trim().ToUpper() == itemKey,
                            ct);

                    if (!existeRespaldo)
                    {
                        sinRespaldo.Add(itemKey);
                        continue;
                    }

                    headers.Add(header);
                }

                if (sinHeader.Count > 0 || sinRespaldo.Count > 0)
                {
                    await trx.RollbackAsync(ct);
                    _OracleContext.ChangeTracker.Clear();

                    var errores = new List<string>();

                    if (sinHeader.Count > 0)
                        errores.Add("sin ART_NO_PROMO: " + string.Join(", ", sinHeader));

                    if (sinRespaldo.Count > 0)
                        errores.Add("sin respaldo en ART_DET_NO_PROMO: " + string.Join(", ", sinRespaldo));

                    return (
                        false,
                        "No se realizó ningún cambio porque hay artículos " + string.Join("; ", errores) + ".",
                        0);
                }

                foreach (var header in headers)
                {
                    // Al pasar a Inactivo NO se toca ART_DET_NO_PROMO.
                    // Ese respaldo será utilizado por el job para reactivar en Fusion.
                    header.ESTADO = "Inactivo";
                    header.GENERADO = "N";
                    _OracleContext.ART_NO_PROMOs.Update(header);
                }

                await _OracleContext.SaveChangesAsync(ct);
                await trx.CommitAsync(ct);

                return (
                    true,
                    $"Se marcaron {headers.Count} artículo(s) como Inactivo / N. " +
                    "El respaldo de ART_DET_NO_PROMO se conserva para la reactivación.",
                    headers.Count);
            }
            catch (Exception ex)
            {
                await trx.RollbackAsync(ct);
                _OracleContext.ChangeTracker.Clear();

                return (
                    false,
                    $"Error marcando artículos como Inactivo: {ex.Message}",
                    0);
            }
        }


        private async Task<(bool Ok, string Mensaje, int Detalles, int Clientes)> CopiarDescuentosArticuloAsync(
            string itemNuevo,
            string itemOrigen,
            CancellationToken ct = default)
        {
            const string bu = "LANCO_CR";
            const string org = "CR_3";
            const string estadoNuevo = "Nuevo";
            const string generadoN = "N";

            static string T(string? value) => (value ?? "").Trim();
            static string N(string? value) => T(value).ToUpperInvariant();

            itemNuevo = T(itemNuevo);
            itemOrigen = T(itemOrigen);

            if (string.IsNullOrWhiteSpace(itemNuevo) || string.IsNullOrWhiteSpace(itemOrigen))
            {
                return (
                    false,
                    "Debe seleccionar el artículo nuevo y el artículo que servirá como origen de los descuentos.",
                    0,
                    0
                );
            }

            var itemNuevoKey = N(itemNuevo);
            var itemOrigenKey = N(itemOrigen);

            if (itemNuevoKey == itemOrigenKey)
            {
                return (
                    false,
                    "El artículo nuevo y el artículo origen deben ser diferentes.",
                    0,
                    0
                );
            }

            // Ambos buscadores usan INV_ARTICULO. Esta validación evita recibir
            // códigos alterados manualmente desde el navegador.
            var articulosEncontrados = await _OracleContext.INV_ARTICULOs
                .AsNoTracking()
                .Where(a =>
                    a.COD_ARTICULO != null &&
                    (
                        a.COD_ARTICULO.Trim().ToUpper() == itemNuevoKey ||
                        a.COD_ARTICULO.Trim().ToUpper() == itemOrigenKey
                    ))
                .Select(a => a.COD_ARTICULO.Trim().ToUpper())
                .Distinct()
                .ToListAsync(ct);

            if (!articulosEncontrados.Contains(itemNuevoKey))
            {
                return (false, $"No se encontró el artículo nuevo {itemNuevoKey} en INV_ARTICULO.", 0, 0);
            }

            if (!articulosEncontrados.Contains(itemOrigenKey))
            {
                return (false, $"No se encontró el artículo origen {itemOrigenKey} en INV_ARTICULO.", 0, 0);
            }

            // Se copian descuentos CLIENTE y PROMOCION sin modificar porcentaje,
            // fechas ni UOM. Únicamente cambia ITEM_NUMBER por el artículo nuevo.
            var descuentosOrigenRaw = await _OracleContext.XXORA_DISCOUNT_LISTs
                .AsNoTracking()
                .Where(x =>
                    x.BU_NAME != null &&
                    x.ITEM_NUMBER != null &&
                    x.BU_NAME.Trim().ToUpper() == bu &&
                    x.ITEM_NUMBER.Trim().ToUpper() == itemOrigenKey)
                .Select(x => new
                {
                    x.RULE_DISCOUNT_NAME,
                    x.PARTY_NUMBER,
                    x.DISCOUNT_PRICE,
                    x.START_DATE,
                    x.END_DATE,
                    x.PRICING_UOM_CODE
                })
                .ToListAsync(ct);

            var descuentosOrigen = descuentosOrigenRaw
                .Select(x => new
                {
                    RuleDiscountName = T(x.RULE_DISCOUNT_NAME),
                    PartyNumber = T(x.PARTY_NUMBER),
                    x.DISCOUNT_PRICE,
                    x.START_DATE,
                    x.END_DATE,
                    PricingUomCode = string.IsNullOrWhiteSpace(x.PRICING_UOM_CODE)
                        ? null
                        : T(x.PRICING_UOM_CODE)
                })
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.RuleDiscountName) &&
                    !string.IsNullOrWhiteSpace(x.PartyNumber))
                // La restricción única de ART_DET_NO_PROMO usa estos campos.
                // Si XXORA contiene la misma llave repetida, se inserta una sola vez.
                .GroupBy(x => new
                {
                    Rule = N(x.RuleDiscountName),
                    Party = N(x.PartyNumber),
                    x.START_DATE
                })
                .Select(g => g.First())
                .ToList();

            if (descuentosOrigen.Count == 0)
            {
                return (
                    false,
                    $"El artículo origen {itemOrigenKey} no tiene descuentos válidos en XXORA_DISCOUNT_LIST.",
                    0,
                    0
                );
            }

            var cantidadClientes = descuentosOrigen
                .Select(x => N(x.PartyNumber))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            await using var trx = await _OracleContext.Database.BeginTransactionAsync(ct);

            try
            {
                // No se elimina información previa del artículo nuevo. Si ya existe
                // una gestión pendiente, se obliga al usuario a revisarla primero.
                var existeEncabezado = await _OracleContext.ART_NO_PROMOs
                    .AsNoTracking()
                    .AnyAsync(x =>
                        x.BU_NAME != null &&
                        x.ORGANIZATION_CODE != null &&
                        x.ITEM_NUMBER != null &&
                        x.BU_NAME.Trim().ToUpper() == bu &&
                        x.ORGANIZATION_CODE.Trim().ToUpper() == org &&
                        x.ITEM_NUMBER.Trim().ToUpper() == itemNuevoKey,
                        ct);

                if (existeEncabezado)
                {
                    await trx.RollbackAsync(ct);

                    return (
                        false,
                        $"El artículo nuevo {itemNuevoKey} ya existe en ART_NO_PROMO. Revise o procese primero esa gestión.",
                        0,
                        0
                    );
                }

                await _OracleContext.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO ART_NO_PROMO
                    (
                        BU_NAME,
                        ORGANIZATION_CODE,
                        ITEM_NUMBER,
                        ESTADO,
                        GENERADO
                    )
                    VALUES
                    (
                        {bu},
                        {org},
                        {itemNuevoKey},
                        {estadoNuevo},
                        {generadoN}
                    )
                ", ct);

                foreach (var descuento in descuentosOrigen)
                {
                    var ruleDiscountName = descuento.RuleDiscountName;
                    var partyNumber = descuento.PartyNumber;
                    var discountPrice = descuento.DISCOUNT_PRICE;
                    var startDate = descuento.START_DATE;
                    var endDate = descuento.END_DATE;
                    var pricingUomCode = descuento.PricingUomCode;

                    await _OracleContext.Database.ExecuteSqlInterpolatedAsync($@"
                        INSERT INTO ART_DET_NO_PROMO
                        (
                            BU_NAME,
                            ORGANIZATION_CODE,
                            ITEM_NUMBER,
                            RULE_DISCOUNT_NAME,
                            PARTY_NUMBER,
                            DISCOUNT_PRICE,
                            START_DATE,
                            END_DATE,
                            PRICING_UOM_CODE
                        )
                        VALUES
                        (
                            {bu},
                            {org},
                            {itemNuevoKey},
                            {ruleDiscountName},
                            {partyNumber},
                            {discountPrice},
                            {startDate},
                            {endDate},
                            {pricingUomCode}
                        )
                    ", ct);
                }

                await trx.CommitAsync(ct);

                return (
                    true,
                    $"Se copiaron los descuentos de {itemOrigenKey} al artículo nuevo {itemNuevoKey}.",
                    descuentosOrigen.Count,
                    cantidadClientes
                );
            }
            catch (Exception ex)
            {
                await trx.RollbackAsync(ct);

                return (
                    false,
                    $"Error copiando descuentos de {itemOrigenKey} a {itemNuevoKey}: {ex.Message}",
                    0,
                    0
                );
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

            const string bu = "LANCO_CR";
            const string org = "CR_3";

            var itemKey = itemNumber.ToUpperInvariant();

            await using var trx = await _OracleContext.Database.BeginTransactionAsync(ct);

            try
            {
                var header = await _OracleContext.ART_NO_PROMOs
                    .FirstOrDefaultAsync(a =>
                        (a.BU_NAME ?? "").Trim().ToUpper() == bu &&
                        (a.ORGANIZATION_CODE ?? "").Trim().ToUpper() == org &&
                        (a.ITEM_NUMBER ?? "").Trim().ToUpper() == itemKey,
                        ct);

                if (header == null)
                {
                    await trx.RollbackAsync(ct);

                    return ArchivoProcesoResult.Fallo(
                        $"No existe ART_NO_PROMO para BU={bu}, ORG={org}, ITEM={itemKey}.");
                }

                header.ESTADO = "Inactivo";
                header.GENERADO = "N";

                _OracleContext.ART_NO_PROMOs.Update(header);

                await _OracleContext.SaveChangesAsync(ct);
                await trx.CommitAsync(ct);

                return ArchivoProcesoResult.Exito(
                    Array.Empty<byte>(),
                    $"ART_NO_PROMO_REACTIVAR_{itemKey}.txt",
                    "text/plain");
            }
            catch (Exception ex)
            {
                await trx.RollbackAsync(ct);

                return ArchivoProcesoResult.Fallo(
                    $"Error marcando reactivación para ITEM={itemKey}: {ex.Message}");
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "PRICE_EDITOR")]
        public async Task<IActionResult> CopiarDescuentosAArticuloNuevo(
            string itemNuevo,
            string itemOrigen)
        {
            var result = await CopiarDescuentosArticuloAsync(
                itemNuevo,
                itemOrigen,
                HttpContext.RequestAborted);

            if (!result.Ok)
            {
                TempData["ErrorMessage"] = result.Mensaje;
                return RedirectToAction(nameof(Index));
            }

            TempData["InfoFlujo"] =
                $"{result.Mensaje} ART_NO_PROMO quedó en estado Nuevo / N. " +
                $"Detalles copiados: {result.Detalles}. Clientes encontrados: {result.Clientes}.";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DesactivarArticuloNoPromo(
            List<string>? itemNumbers,
            string? itemNumber = null)
        {
            var items = itemNumbers ?? new List<string>();

            // Compatibilidad con el POST individual anterior por si el navegador
            // todavía tiene una versión cacheada de la vista/JavaScript.
            if (!string.IsNullOrWhiteSpace(itemNumber))
                items.Add(itemNumber);

            var result = await ActivarArticulosNoPromoAsync(
                items,
                HttpContext.RequestAborted);

            if (!result.Ok)
            {
                TempData["ErrorMessage"] = result.Mensaje;
                return RedirectToAction(nameof(Index));
            }

            TempData["InfoFlujo"] = result.Mensaje;
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReactivarArticuloNoPromo(
            List<string>? itemNumbers,
            string? itemNumber = null)
        {
            var items = itemNumbers ?? new List<string>();

            if (!string.IsNullOrWhiteSpace(itemNumber))
                items.Add(itemNumber);

            var result = await InactivarArticulosNoPromoAsync(
                items,
                HttpContext.RequestAborted);

            if (!result.Ok)
            {
                TempData["ErrorMessage"] = result.Mensaje;
                return RedirectToAction(nameof(Index));
            }

            TempData["InfoFlujo"] = result.Mensaje;
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<JsonResult> BuscarItems(string filtro)
        {
            filtro = (filtro ?? "").Trim();
            if (filtro.Length < 2)
                return Json(new List<object>());

            var f = filtro.ToUpperInvariant();

            // item_number = COD_ARTICULO
            var items = await _OracleContext.INV_ARTICULOs
                .AsNoTracking()
                .Where(a => a.COD_ARTICULO != null)
                .Where(a =>
                    (a.COD_ARTICULO ?? "").ToUpper().Contains(f) ||
                    (a.DES_ARTICULO ?? "").ToUpper().Contains(f)
                )
                .Select(a => new
                {
                    ItemNumber = a.COD_ARTICULO,
                    Description = a.DES_ARTICULO
                })
                .OrderBy(x => x.ItemNumber)
                .Take(50)
                .ToListAsync();

            return Json(items);
        }

        [HttpGet]
        public async Task<IActionResult> BuscarItemsNoPromo(string? filtro)
        {
            const string bu = "LANCO_CR";
            const string org = "CR_3";

            filtro = (filtro ?? "").Trim();
            var q = filtro.ToUpperInvariant();

            var query =
                from a in _OracleContext.ART_NO_PROMOs.AsNoTracking()
                join articulo in _OracleContext.INV_ARTICULOs.AsNoTracking()
                    on a.ITEM_NUMBER equals articulo.COD_ARTICULO into articulos
                from articulo in articulos.DefaultIfEmpty()
                where a.BU_NAME != null
                   && a.ORGANIZATION_CODE != null
                   && a.ITEM_NUMBER != null
                   && a.ESTADO != null
                   && a.BU_NAME.Trim().ToUpper() == bu
                   && a.ORGANIZATION_CODE.Trim().ToUpper() == org
                   && a.ESTADO.Trim().ToUpper() == "ACTIVO"
                select new
                {
                    itemNumber = a.ITEM_NUMBER,
                    description = articulo.DES_ARTICULO
                };

            // Sin filtro muestra todos.
            // Con filtro muestra solamente las coincidencias.
            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(x =>
                    x.itemNumber.Trim().ToUpper().Contains(q) ||
                    (
                        x.description != null &&
                        x.description.Trim().ToUpper().Contains(q)
                    )
                );
            }

            var data = await query
                .OrderBy(x => x.itemNumber)
                .Select(x => new
                {
                    x.itemNumber,
                    description = x.description ?? ""
                })
                .Take(500)
                .ToListAsync();

            return Json(data);
        }

        [HttpGet]
        public JsonResult BuscarClientes(string filtro)
        {
            if (string.IsNullOrWhiteSpace(filtro))
                return Json(new List<object>());

            // Normalizamos el filtro
            var filtroUpper = filtro.ToUpper().Trim();

            // Ahora buscamos en Oracle: GEN_CLIENTE (IDCLIENTE, NOMBRE_CLIENTE)
            var coincidencias = _OracleContext.GEN_CLIENTEs
                .AsNoTracking()
                .Where(c =>
                    (c.IDCLIENTE ?? string.Empty).ToUpper().Contains(filtroUpper) ||
                    (c.NOMBRE_CLIENTE ?? string.Empty).ToUpper().Contains(filtroUpper)
                )
                .Select(c => new
                {
                    // Mantengo los mismos nombres que devolvía antes el JSON
                    CodCliente = c.IDCLIENTE,
                    CodCia = "",          // En Oracle no tenemos COD_CIA, lo dejamos vacío
                    NomCliente = c.NOMBRE_CLIENTE,
                    Lugar = ""            // Tampoco existe campo equivalente en Oracle
                })
                .Take(50)                 // Limite de seguridad para no traer demasiados
                .ToList();

            return Json(coincidencias);
        }



        [HttpGet]
        public async Task<IActionResult> GetDescuentosCliente(
            string codCliente,
            string? codCia,
            string? tipoDescuento)
        {
            if (string.IsNullOrWhiteSpace(codCliente))
                return Json(Array.Empty<object>());

            const string buKey = "LANCO_CR";
            const string orgKey = "CR_3";
            const string orgItemMaster = "LCR_3";

            static string T(string? s) => (s ?? "").Trim();
            static string N(string? s) => (s ?? "").Trim().ToUpperInvariant();
            static string ScopeKey(string? linea, string? clase) => $"{N(linea)}|{N(clase)}";

            var clienteTrim = codCliente.Trim();
            var clienteKey = N(codCliente);
            var hoy = DateTime.Today;

            bool esPromo =
                !string.IsNullOrWhiteSpace(tipoDescuento) &&
                tipoDescuento.Contains("promocional", StringComparison.OrdinalIgnoreCase);

            try
            {
                var baseRows = await _OracleContext.PREDESCLASEORACLEs
                    .AsNoTracking()
                    .Where(d =>
                        d.ORGANIZATION_CODE != null &&
                        d.ORGANIZATION_CODE.Trim().ToUpper() == orgKey &&
                        d.IDCLIENTE != null &&
                        d.IDCLIENTE.Trim().ToUpper() == clienteKey &&
                        (d.FECHA_INICIO == null || d.FECHA_INICIO <= hoy) &&
                        (d.FECHA_FIN == null || d.FECHA_FIN >= hoy) &&
                        (
                            d.ITEM_NUMBER == null ||
                            d.ITEM_NUMBER.Trim() == "" ||
                            (
                                _OracleContext.XXORA_ITEM_MASTERs.AsNoTracking().Any(x =>
                                    x.BU_NAME != null &&
                                    x.ORGANIZATION_CODE != null &&
                                    x.ITEM_NUMBER != null &&
                                    x.ACCEPTADESCUENTO != null &&
                                    x.BU_NAME.Trim().ToUpper() == buKey &&
                                    x.ORGANIZATION_CODE.Trim().ToUpper() == orgItemMaster &&
                                    x.ITEM_NUMBER.Trim().ToUpper() == d.ITEM_NUMBER.Trim().ToUpper() &&
                                    x.ACCEPTADESCUENTO.Trim().ToUpper() == "S"
                                )
                                &&
                                !_OracleContext.ART_NO_PROMOs.AsNoTracking().Any(np =>
                                    np.BU_NAME != null &&
                                    np.ORGANIZATION_CODE != null &&
                                    np.ITEM_NUMBER != null &&
                                    np.ESTADO != null &&
                                    np.BU_NAME.Trim().ToUpper() == buKey &&
                                    np.ORGANIZATION_CODE.Trim().ToUpper() == orgKey &&
                                    np.ITEM_NUMBER.Trim().ToUpper() == d.ITEM_NUMBER.Trim().ToUpper() &&
                                    np.ESTADO.Trim().ToUpper() == "ACTIVO"
                                )
                            )
                        )
                    )
                    .Select(d => new
                    {
                        CodLinea = d.CATEGORY_CODE,
                        CodArticulo = d.ITEM_NUMBER,
                        Valor = d.PORCENTAJE,
                        Clase = d.SUBCATEGORY_CODE
                    })
                    .OrderBy(x => x.CodLinea)
                    .ToListAsync();

                if (baseRows.Count == 0)
                    return Json(Array.Empty<object>());

                if (esPromo)
                {
                    var explicitos = baseRows
                        .Select(x => T(x.CodArticulo))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var sinFijoVigente = await ObtenerArticulosSinDescuentoClienteVigenteAsync(
                        explicitos,
                        clienteTrim,
                        buKey,
                        HttpContext.RequestAborted);

                    baseRows = baseRows
                        .Where(x =>
                            string.IsNullOrWhiteSpace(T(x.CodArticulo)) ||
                            !sinFijoVigente.Contains(T(x.CodArticulo)))
                        .ToList();
                }

                var scopesValidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var row in baseRows.Where(x => string.IsNullOrWhiteSpace(T(x.CodArticulo))))
                {
                    var key = ScopeKey(row.CodLinea, row.Clase);
                    if (scopesValidos.Contains(key))
                        continue;

                    if (await ExisteArticuloElegibleEnScopeAsync(
                        row.CodLinea,
                        row.Clase,
                        buKey,
                        clienteTrim,
                        tipoDescuento,
                        HttpContext.RequestAborted))
                    {
                        scopesValidos.Add(key);
                    }
                }

                baseRows = baseRows
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(T(x.CodArticulo)) ||
                        scopesValidos.Contains(ScopeKey(x.CodLinea, x.Clase)))
                    .ToList();

                if (baseRows.Count == 0)
                    return Json(Array.Empty<object>());

                var codLineas = baseRows
                    .Select(x => T(x.CodLinea))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var dictLineas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                const int chunkSize = 900;

                for (int i = 0; i < codLineas.Count; i += chunkSize)
                {
                    var chunk = codLineas.Skip(i).Take(chunkSize).ToList();

                    var lineasChunk = await _OracleContext.INV_LINEAs.AsNoTracking()
                        .Where(l => l.CATEGORY_CODE != null && chunk.Contains(l.CATEGORY_CODE.Trim()))
                        .Select(l => new { Cod = l.CATEGORY_CODE, Nombre = l.CATEGORY_NAME })
                        .ToListAsync();

                    foreach (var l in lineasChunk)
                    {
                        var key = N(l.Cod);
                        if (!dictLineas.ContainsKey(key))
                            dictLineas[key] = T(l.Nombre);
                    }
                }

                var descuentos = baseRows.Select(d =>
                {
                    var codLinea = T(d.CodLinea);
                    dictLineas.TryGetValue(N(codLinea), out var desLinea);

                    return new
                    {
                        codLinea,
                        desLinea = desLinea ?? "",
                        codArticulo = T(d.CodArticulo),
                        tipo = "P",
                        valor = d.Valor,
                        claseart = T(d.Clase)
                    };
                }).ToList();

                return Json(descuentos);
            }
            catch
            {
                Response.StatusCode = 500;
                return Json(new
                {
                    error = true,
                    message = "No fue posible obtener los descuentos vigentes del cliente."
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDescuentosCombinados(
            string clienteOrigen,
            string clienteDestino,
            string? tipoDescuento)
        {
            if (string.IsNullOrWhiteSpace(clienteOrigen) ||
                string.IsNullOrWhiteSpace(clienteDestino))
            {
                return Json(Array.Empty<object>());
            }

            const string buKey = "LANCO_CR";
            const string orgKey = "CR_3";
            const string orgItemMaster = "LCR_3";

            static string T(string? s) => (s ?? "").Trim();
            static string N(string? s) => (s ?? "").Trim().ToUpperInvariant();
            static string ScopeKey(string? linea, string? clase) => $"{N(linea)}|{N(clase)}";

            var origenKey = N(clienteOrigen);
            var destinoKey = N(clienteDestino);
            var hoy = DateTime.Today;

            bool esPromo =
                !string.IsNullOrWhiteSpace(tipoDescuento) &&
                tipoDescuento.Contains("promocional", StringComparison.OrdinalIgnoreCase);

            var rows = await (
                from d in _OracleContext.PREDESCLASEORACLEs.AsNoTracking()
                join l in _OracleContext.INV_LINEAs.AsNoTracking()
                    on d.CATEGORY_CODE equals l.CATEGORY_CODE into gj
                from l in gj.DefaultIfEmpty()
                where d.IDCLIENTE != null
                   && d.IDCLIENTE.Trim().ToUpper() == origenKey
                   && d.ORGANIZATION_CODE != null
                   && d.ORGANIZATION_CODE.Trim().ToUpper() == orgKey
                   && (d.FECHA_INICIO == null || d.FECHA_INICIO <= hoy)
                   && (d.FECHA_FIN == null || d.FECHA_FIN >= hoy)
                   && (
                        d.ITEM_NUMBER == null ||
                        d.ITEM_NUMBER.Trim() == "" ||
                        (
                            _OracleContext.XXORA_ITEM_MASTERs.AsNoTracking().Any(x =>
                                x.BU_NAME != null &&
                                x.ORGANIZATION_CODE != null &&
                                x.ITEM_NUMBER != null &&
                                x.ACCEPTADESCUENTO != null &&
                                x.BU_NAME.Trim().ToUpper() == buKey &&
                                x.ORGANIZATION_CODE.Trim().ToUpper() == orgItemMaster &&
                                x.ITEM_NUMBER.Trim().ToUpper() == d.ITEM_NUMBER.Trim().ToUpper() &&
                                x.ACCEPTADESCUENTO.Trim().ToUpper() == "S"
                            )
                            &&
                            !_OracleContext.ART_NO_PROMOs.AsNoTracking().Any(np =>
                                np.BU_NAME != null &&
                                np.ORGANIZATION_CODE != null &&
                                np.ITEM_NUMBER != null &&
                                np.ESTADO != null &&
                                np.BU_NAME.Trim().ToUpper() == buKey &&
                                np.ORGANIZATION_CODE.Trim().ToUpper() == orgKey &&
                                np.ITEM_NUMBER.Trim().ToUpper() == d.ITEM_NUMBER.Trim().ToUpper() &&
                                np.ESTADO.Trim().ToUpper() == "ACTIVO"
                            )
                        )
                   )
                select new
                {
                    d.CATEGORY_CODE,
                    d.SUBCATEGORY_CODE,
                    d.ITEM_NUMBER,
                    d.PORCENTAJE,
                    DesLinea = l != null ? l.CATEGORY_NAME : ""
                }
            ).ToListAsync();

            if (rows.Count == 0)
                return Json(Array.Empty<object>());

            if (esPromo)
            {
                var explicitos = rows
                    .Select(x => T(x.ITEM_NUMBER))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var sinFijoDestino = await ObtenerArticulosSinDescuentoClienteVigenteAsync(
                    explicitos,
                    destinoKey,
                    buKey,
                    HttpContext.RequestAborted);

                rows = rows
                    .Where(x =>
                        string.IsNullOrWhiteSpace(T(x.ITEM_NUMBER)) ||
                        !sinFijoDestino.Contains(T(x.ITEM_NUMBER)))
                    .ToList();
            }

            var scopesValidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows.Where(x => string.IsNullOrWhiteSpace(T(x.ITEM_NUMBER))))
            {
                var key = ScopeKey(row.CATEGORY_CODE, row.SUBCATEGORY_CODE);
                if (scopesValidos.Contains(key))
                    continue;

                if (await ExisteArticuloElegibleEnScopeAsync(
                    row.CATEGORY_CODE,
                    row.SUBCATEGORY_CODE,
                    buKey,
                    destinoKey,
                    tipoDescuento,
                    HttpContext.RequestAborted))
                {
                    scopesValidos.Add(key);
                }
            }

            rows = rows
                .Where(x =>
                    !string.IsNullOrWhiteSpace(T(x.ITEM_NUMBER)) ||
                    scopesValidos.Contains(ScopeKey(x.CATEGORY_CODE, x.SUBCATEGORY_CODE)))
                .ToList();

            var descuentosOrigen = rows
                .Select(x => new
                {
                    codLinea = T(x.CATEGORY_CODE),
                    desLinea = T(x.DesLinea),
                    codArticulo = T(x.ITEM_NUMBER),
                    tipo = "P",
                    valor = x.PORCENTAJE,
                    claseart = T(x.SUBCATEGORY_CODE)
                })
                .OrderBy(d => int.TryParse(d.codLinea, out var num) ? num : int.MaxValue)
                .ToList();

            return Json(descuentosOrigen);
        }

        [HttpGet]
        public async Task<JsonResult> GetDetalleArticulo(string? codArticulo, string? codLinea, string? codClase)
        {
            // Normaliza fuera del LINQ (esto SÍ se puede)
            codArticulo = (codArticulo ?? "").Trim();
            codLinea = (codLinea ?? "").Trim();
            codClase = (codClase ?? "").Trim();

            if (codArticulo.Equals("NULL", StringComparison.OrdinalIgnoreCase)) codArticulo = "";
            if (codClase.Equals("NULL", StringComparison.OrdinalIgnoreCase)) codClase = "";

            if (string.IsNullOrWhiteSpace(codLinea))
            {
                return Json(new
                {
                    success = false,
                    message = "Falta la línea.",
                    data = new { codLinea = "", desLinea = "", codClase = "", desClase = "", codArticulo = "", desArticulo = "" }
                });
            }

            // Keys normalizadas (FUERA del query)
            var codArtKey = string.IsNullOrWhiteSpace(codArticulo) ? "" : codArticulo.ToUpper();
            var codLineaKey = codLinea.ToUpper();
            var codClaseKey = string.IsNullOrWhiteSpace(codClase) ? "" : codClase.ToUpper();

            string outCodLinea = codLinea;
            string outCodClase = codClase;
            string outCodArticulo = codArticulo;

            string desLinea = "";
            string desClase = "";
            string desArticulo = "";

            // 1) Si viene ARTÍCULO
            if (!string.IsNullOrWhiteSpace(codArtKey))
            {
                var art = await _OracleContext.INV_ARTICULOs
                    .AsNoTracking()
                    .Where(a =>
                        a.COD_ARTICULO != null &&
                        a.COD_ARTICULO.Trim().ToUpper() == codArtKey
                    )
                    .Select(a => new
                    {
                        codArticulo = (a.COD_ARTICULO ?? "").Trim(),
                        desArticulo = (a.DES_ARTICULO ?? "").Trim(),
                        codLinea = (a.COD_LINEA ?? "").Trim(),
                        desLinea = (a.DES_LINEA ?? "").Trim(),
                        codClase = (a.COD_CLASE ?? "").Trim(),
                        desClase = (a.DES_CLASE ?? "").Trim()
                    })
                    .FirstOrDefaultAsync();

                if (art != null)
                {
                    outCodArticulo = art.codArticulo;
                    desArticulo = art.desArticulo;

                    if (string.IsNullOrWhiteSpace(desLinea) && !string.IsNullOrWhiteSpace(art.desLinea))
                        desLinea = art.desLinea;

                    if (string.IsNullOrWhiteSpace(outCodClase) && !string.IsNullOrWhiteSpace(art.codClase))
                        outCodClase = art.codClase;

                    if (string.IsNullOrWhiteSpace(desClase) && !string.IsNullOrWhiteSpace(art.desClase))
                        desClase = art.desClase;

                    // si el artículo trae línea distinta (por seguridad)
                    if (!string.IsNullOrWhiteSpace(art.codLinea))
                        outCodLinea = art.codLinea;
                }
            }

            // Recalcular keys si cambió algo (por ejemplo outCodLinea/outCodClase)
            codLineaKey = (outCodLinea ?? "").Trim().ToUpper();
            codClaseKey = string.IsNullOrWhiteSpace(outCodClase) ? "" : outCodClase.Trim().ToUpper();

            // 2) Si viene CLASE y no tenemos descripción
            if (!string.IsNullOrWhiteSpace(codClaseKey) && string.IsNullOrWhiteSpace(desClase))
            {
                var cls = await _OracleContext.INV_ARTICULOs
                    .AsNoTracking()
                    .Where(a =>
                        a.COD_LINEA != null && a.COD_LINEA.Trim().ToUpper() == codLineaKey &&
                        a.COD_CLASE != null && a.COD_CLASE.Trim().ToUpper() == codClaseKey
                    )
                    .Select(a => new
                    {
                        codClase = (a.COD_CLASE ?? "").Trim(),
                        desClase = (a.DES_CLASE ?? "").Trim()
                    })
                    .FirstOrDefaultAsync();

                if (cls != null)
                {
                    outCodClase = cls.codClase;
                    desClase = cls.desClase;
                }
            }

            // 3) Resolver LÍNEA desde INV_LINEA si no tenemos desLinea
            if (string.IsNullOrWhiteSpace(desLinea) && !string.IsNullOrWhiteSpace(codLineaKey))
            {
                var lin = await _OracleContext.INV_LINEAs
                    .AsNoTracking()
                    .Where(l =>
                        l.CATEGORY_CODE != null &&
                        l.CATEGORY_CODE.Trim().ToUpper() == codLineaKey
                    )
                    .Select(l => new
                    {
                        codLinea = (l.CATEGORY_CODE ?? "").Trim(),
                        desLinea = (l.CATEGORY_NAME ?? "").Trim()
                    })
                    .FirstOrDefaultAsync();

                if (lin != null)
                {
                    outCodLinea = lin.codLinea;
                    desLinea = lin.desLinea;
                }
            }

            // Siempre success=true para que el modal muestre algo aunque falten descripciones
            return Json(new
            {
                success = true,
                data = new
                {
                    codLinea = outCodLinea ?? "",
                    desLinea = desLinea ?? "",
                    codClase = outCodClase ?? "",
                    desClase = desClase ?? "",
                    codArticulo = outCodArticulo ?? "",
                    desArticulo = desArticulo ?? ""
                }
            });
        }


        [HttpGet]
        public async Task<JsonResult> BuscarLineas(
            string? filtro,
            string? codCliente,
            string? buNombre,
            string? tipoDescuento)
        {
            static string N(string? s) => (s ?? "").Trim().ToUpperInvariant();

            var buKey = N(string.IsNullOrWhiteSpace(buNombre) ? "LANCO_CR" : buNombre);
            var clienteKey = N(codCliente);
            var filtroKey = N(filtro);

            const string orgItemMaster = "LCR_3";
            const string orgNoPromo = "CR_3";

            bool esPromo =
                !string.IsNullOrWhiteSpace(tipoDescuento) &&
                tipoDescuento.Contains("promocional", StringComparison.OrdinalIgnoreCase);

            if (esPromo && string.IsNullOrWhiteSpace(clienteKey))
                return Json(Array.Empty<object>());

            var articulosElegibles = _OracleContext.INV_ARTICULOs
                .AsNoTracking()
                .Where(a =>
                    a.COD_ARTICULO != null &&
                    a.COD_LINEA != null &&
                    _OracleContext.XXORA_ITEM_MASTERs.AsNoTracking().Any(x =>
                        x.BU_NAME != null &&
                        x.ORGANIZATION_CODE != null &&
                        x.ITEM_NUMBER != null &&
                        x.ACCEPTADESCUENTO != null &&
                        x.BU_NAME.Trim().ToUpper() == buKey &&
                        x.ORGANIZATION_CODE.Trim().ToUpper() == orgItemMaster &&
                        x.ITEM_NUMBER.Trim().ToUpper() == a.COD_ARTICULO.Trim().ToUpper() &&
                        x.ACCEPTADESCUENTO.Trim().ToUpper() == "S"
                    ) &&
                    !_OracleContext.ART_NO_PROMOs.AsNoTracking().Any(np =>
                        np.BU_NAME != null &&
                        np.ORGANIZATION_CODE != null &&
                        np.ITEM_NUMBER != null &&
                        np.ESTADO != null &&
                        np.BU_NAME.Trim().ToUpper() == buKey &&
                        np.ORGANIZATION_CODE.Trim().ToUpper() == orgNoPromo &&
                        np.ITEM_NUMBER.Trim().ToUpper() == a.COD_ARTICULO.Trim().ToUpper() &&
                        np.ESTADO.Trim().ToUpper() == "ACTIVO"
                    )
                );

            if (esPromo)
            {
                var hoy = DateTime.Today;

                articulosElegibles = articulosElegibles.Where(a =>
                    _OracleContext.XXORA_DISCOUNT_LISTs.AsNoTracking().Any(x =>
                        x.BU_NAME != null &&
                        x.PARTY_NUMBER != null &&
                        x.ITEM_NUMBER != null &&
                        x.RULE_DISCOUNT_NAME != null &&
                        x.BU_NAME.Trim().ToUpper() == buKey &&
                        x.PARTY_NUMBER.Trim().ToUpper() == clienteKey &&
                        x.ITEM_NUMBER.Trim().ToUpper() == a.COD_ARTICULO.Trim().ToUpper() &&
                        x.RULE_DISCOUNT_NAME.Trim().ToUpper() == "CLIENTE" &&
                        x.START_DATE <= hoy &&
                        (x.END_DATE == null || x.END_DATE >= hoy)
                    ));
            }

            var lineasElegibles = await articulosElegibles
                .Select(a => a.COD_LINEA!.Trim())
                .Distinct()
                .ToListAsync();

            if (lineasElegibles.Count == 0)
                return Json(Array.Empty<object>());

            var query = _OracleContext.INV_LINEAs
                .AsNoTracking()
                .Where(l =>
                    l.CATEGORY_CODE != null &&
                    lineasElegibles.Contains(l.CATEGORY_CODE.Trim()));

            if (!string.IsNullOrWhiteSpace(filtroKey))
            {
                query = query.Where(l =>
                    (l.CATEGORY_CODE ?? "").Trim().ToUpper().Contains(filtroKey) ||
                    (l.CATEGORY_NAME ?? "").Trim().ToUpper().Contains(filtroKey));
            }

            var lineas = await query
                .Select(l => new
                {
                    codLinea = (l.CATEGORY_CODE ?? "").Trim(),
                    desLinea = (l.CATEGORY_NAME ?? "").Trim()
                })
                .Distinct()
                .OrderBy(l => l.codLinea)
                .ToListAsync();

            return Json(lineas);
        }

        [HttpGet]
        public async Task<JsonResult> BuscarArticulosPorLinea(
      string? codLinea,
      string? filtro,
      string? medida,
      string? codCliente,
      string? buNombre,
      string? tipoDescuento)
        {
            codLinea = (codLinea ?? "").Trim();
            filtro = (filtro ?? "").Trim();
            medida = (medida ?? "").Trim();

            var bu = string.IsNullOrWhiteSpace(buNombre)
                ? "LANCO_CR"
                : buNombre.Trim();

            codCliente = (codCliente ?? "").Trim();

            // Organización utilizada en XXORA_ITEM_MASTER
            const string org = "LCR_3";

            static bool EsPromocional(string? t)
                => !string.IsNullOrWhiteSpace(t) &&
                   t.Contains("promocional", StringComparison.OrdinalIgnoreCase);

            bool esPromo = EsPromocional(tipoDescuento);

            // Debug headers
            Response.Headers["X-Debug-TipoDescuento"] = tipoDescuento ?? "";
            Response.Headers["X-Debug-EsPromo"] = esPromo ? "1" : "0";
            Response.Headers["X-Debug-BU"] = bu;
            Response.Headers["X-Debug-Cliente"] = codCliente;
            Response.Headers["X-Debug-Linea"] = codLinea;
            Response.Headers["X-Debug-Medida"] = medida;
            Response.Headers["X-Debug-Filtro"] = filtro;

            bool hayFiltroTexto =
                !string.IsNullOrWhiteSpace(filtro) &&
                filtro.Length >= 2;

            bool hayLinea = !string.IsNullOrWhiteSpace(codLinea);
            bool hayMedida = !string.IsNullOrWhiteSpace(medida);

            if (!hayLinea && !hayMedida && !hayFiltroTexto)
                return Json(Array.Empty<object>());

            // ================================
            // NORMALIZAR FILTROS
            // ================================
            var lineaKey = codLinea.ToUpperInvariant();
            var medidaKey = medida.ToUpperInvariant();
            var fKey = filtro.ToUpperInvariant();

            var buKey = bu.Trim().ToUpperInvariant();
            var orgKey = org.Trim().ToUpperInvariant();
            var orgNoPromoKey = "CR_3";

            // ==========================================================
            // BASE DE ARTÍCULOS
            // SOLO ARTÍCULOS QUE TENGAN ACCEPTADESCUENTO = 'S'
            // ==========================================================
            var inv = _OracleContext.INV_ARTICULOs
                .AsNoTracking()
                .Where(a =>
                    a.COD_ARTICULO != null &&

                    _OracleContext.XXORA_ITEM_MASTERs
                        .AsNoTracking()
                        .Any(x =>
                            x.BU_NAME != null &&
                            x.ORGANIZATION_CODE != null &&
                            x.ITEM_NUMBER != null &&
                            x.ACCEPTADESCUENTO != null &&

                            x.BU_NAME.Trim().ToUpper() == buKey &&
                            x.ORGANIZATION_CODE.Trim().ToUpper() == orgKey &&

                            x.ITEM_NUMBER.Trim().ToUpper() ==
                                a.COD_ARTICULO.Trim().ToUpper() &&

                            x.ACCEPTADESCUENTO.Trim().ToUpper() == "S"
                        ) &&

                    // REGLA GLOBAL:
                    // si el artículo está ACTIVO en ART_NO_PROMO no puede aparecer
                    !_OracleContext.ART_NO_PROMOs
                        .AsNoTracking()
                        .Any(np =>
                            np.BU_NAME != null &&
                            np.ORGANIZATION_CODE != null &&
                            np.ITEM_NUMBER != null &&
                            np.ESTADO != null &&
                            np.BU_NAME.Trim().ToUpper() == buKey &&
                            np.ORGANIZATION_CODE.Trim().ToUpper() == orgNoPromoKey &&
                            np.ITEM_NUMBER.Trim().ToUpper() ==
                                a.COD_ARTICULO.Trim().ToUpper() &&
                            np.ESTADO.Trim().ToUpper() == "ACTIVO"
                        )
                );

            // ================================
            // FILTRO POR LÍNEA
            // ================================
            if (hayLinea)
            {
                inv = inv.Where(a =>
                    a.COD_LINEA != null &&
                    a.COD_LINEA.Trim().ToUpper() == lineaKey
                );
            }

            // ================================
            // FILTRO POR MEDIDA
            // ================================
            if (hayMedida)
            {
                inv = inv.Where(a =>
                    a.MEDIDA != null &&
                    a.MEDIDA.Trim().ToUpper() == medidaKey
                );
            }

            // ================================
            // FILTRO TEXTO
            // ================================
            if (hayFiltroTexto)
            {
                inv = inv.Where(a =>
                    ((a.COD_ARTICULO ?? "")
                        .Trim()
                        .ToUpper()
                        .Contains(fKey))
                    ||
                    ((a.DES_ARTICULO ?? "")
                        .Trim()
                        .ToUpper()
                        .Contains(fKey))
                );
            }

            // ==========================================================
            // DESCUENTO PROMOCIONAL
            // ==========================================================
            if (esPromo)
            {
                if (string.IsNullOrWhiteSpace(codCliente))
                    return Json(Array.Empty<object>());

                var clienteKey = codCliente
                    .Trim()
                    .ToUpperInvariant();

                var hoy = DateTime.Today;

                var xxora = _OracleContext.XXORA_DISCOUNT_LISTs
                    .AsNoTracking()
                    .Where(x =>
                        x.BU_NAME != null &&
                        x.PARTY_NUMBER != null &&
                        x.ITEM_NUMBER != null &&
                        x.RULE_DISCOUNT_NAME != null &&

                        x.BU_NAME.Trim().ToUpper() == buKey &&

                        x.PARTY_NUMBER.Trim().ToUpper() ==
                            clienteKey &&

                        x.RULE_DISCOUNT_NAME.Trim().ToUpper() ==
                            "CLIENTE" &&

                        // Descuento vigente: ya inició y todavía no terminó.
                        x.START_DATE <= hoy &&
                        (
                            x.END_DATE == null ||
                            x.END_DATE >= hoy
                        )
                    );

                var queryPromo =
                    from a in inv
                    join x in xxora
                        on (a.COD_ARTICULO ?? "")
                            .Trim()
                            .ToUpper()
                        equals
                           (x.ITEM_NUMBER ?? "")
                            .Trim()
                            .ToUpper()

                    select new
                    {
                        codArticulo =
                            (a.COD_ARTICULO ?? "").Trim(),

                        desArticulo =
                            (a.DES_ARTICULO ?? "").Trim(),

                        codLinea =
                            (a.COD_LINEA ?? "").Trim(),

                        desLinea =
                            (a.DES_LINEA ?? "").Trim(),

                        medida =
                            (a.MEDIDA ?? "").Trim()
                    };

                var articulosPromo = await queryPromo
                    .Distinct()
                    .OrderBy(a => a.codArticulo)
                    .Take(500)
                    .ToListAsync();

                return Json(articulosPromo);
            }

            // ==========================================================
            // DESCUENTO FIJO
            // También ya viene filtrado por ACCEPTADESCUENTO = 'S'
            // ==========================================================
            var articulosFijo = await inv
                .Select(a => new
                {
                    codArticulo =
                        (a.COD_ARTICULO ?? "").Trim(),

                    desArticulo =
                        (a.DES_ARTICULO ?? "").Trim(),

                    codLinea =
                        (a.COD_LINEA ?? "").Trim(),

                    desLinea =
                        (a.DES_LINEA ?? "").Trim(),

                    medida =
                        (a.MEDIDA ?? "").Trim()
                })
                .Distinct()
                .OrderBy(a => a.codArticulo)
                .Take(500)
                .ToListAsync();

            return Json(articulosFijo);
        }

        [HttpGet]
        public async Task<JsonResult> BuscarClaseartsPorlinea(
            string codLinea,
            string? filtro,
            string? codCliente,
            string? buNombre,
            string? tipoDescuento)
        {
            static string N(string? s) => (s ?? "").Trim().ToUpperInvariant();

            var lineaKey = N(codLinea);
            var filtroKey = N(filtro);
            var buKey = N(string.IsNullOrWhiteSpace(buNombre) ? "LANCO_CR" : buNombre);
            var clienteKey = N(codCliente);

            if (string.IsNullOrWhiteSpace(lineaKey))
                return Json(Array.Empty<object>());

            const string orgItemMaster = "LCR_3";
            const string orgNoPromo = "CR_3";

            bool esPromo =
                !string.IsNullOrWhiteSpace(tipoDescuento) &&
                tipoDescuento.Contains("promocional", StringComparison.OrdinalIgnoreCase);

            if (esPromo && string.IsNullOrWhiteSpace(clienteKey))
                return Json(Array.Empty<object>());

            var query = _OracleContext.INV_ARTICULOs
                .AsNoTracking()
                .Where(a =>
                    a.COD_ARTICULO != null &&
                    a.COD_LINEA != null &&
                    a.COD_CLASE != null &&
                    a.COD_LINEA.Trim().ToUpper() == lineaKey &&
                    _OracleContext.XXORA_ITEM_MASTERs.AsNoTracking().Any(x =>
                        x.BU_NAME != null &&
                        x.ORGANIZATION_CODE != null &&
                        x.ITEM_NUMBER != null &&
                        x.ACCEPTADESCUENTO != null &&
                        x.BU_NAME.Trim().ToUpper() == buKey &&
                        x.ORGANIZATION_CODE.Trim().ToUpper() == orgItemMaster &&
                        x.ITEM_NUMBER.Trim().ToUpper() == a.COD_ARTICULO.Trim().ToUpper() &&
                        x.ACCEPTADESCUENTO.Trim().ToUpper() == "S"
                    ) &&
                    !_OracleContext.ART_NO_PROMOs.AsNoTracking().Any(np =>
                        np.BU_NAME != null &&
                        np.ORGANIZATION_CODE != null &&
                        np.ITEM_NUMBER != null &&
                        np.ESTADO != null &&
                        np.BU_NAME.Trim().ToUpper() == buKey &&
                        np.ORGANIZATION_CODE.Trim().ToUpper() == orgNoPromo &&
                        np.ITEM_NUMBER.Trim().ToUpper() == a.COD_ARTICULO.Trim().ToUpper() &&
                        np.ESTADO.Trim().ToUpper() == "ACTIVO"
                    )
                );

            if (esPromo)
            {
                var hoy = DateTime.Today;

                query = query.Where(a =>
                    _OracleContext.XXORA_DISCOUNT_LISTs.AsNoTracking().Any(x =>
                        x.BU_NAME != null &&
                        x.PARTY_NUMBER != null &&
                        x.ITEM_NUMBER != null &&
                        x.RULE_DISCOUNT_NAME != null &&
                        x.BU_NAME.Trim().ToUpper() == buKey &&
                        x.PARTY_NUMBER.Trim().ToUpper() == clienteKey &&
                        x.ITEM_NUMBER.Trim().ToUpper() == a.COD_ARTICULO.Trim().ToUpper() &&
                        x.RULE_DISCOUNT_NAME.Trim().ToUpper() == "CLIENTE" &&
                        x.START_DATE <= hoy &&
                        (x.END_DATE == null || x.END_DATE >= hoy)
                    ));
            }

            if (!string.IsNullOrWhiteSpace(filtroKey))
            {
                query = query.Where(a =>
                    (a.COD_CLASE ?? "").Trim().ToUpper().Contains(filtroKey) ||
                    (a.DES_CLASE ?? "").Trim().ToUpper().Contains(filtroKey));
            }

            var clases = await query
                .GroupBy(a => new { a.COD_CLASE, a.DES_CLASE })
                .Select(g => new
                {
                    codigo = (g.Key.COD_CLASE ?? "").Trim(),
                    descripcion = (g.Key.DES_CLASE ?? "").Trim()
                })
                .OrderBy(x => x.codigo)
                .ToListAsync();

            return Json(clases);
        }

        private class DetalleDto
        {
            public long? Consecutivodetalle { get; set; }  // si tu columna es decimal, cambiá a decimal?
            public string? codLinea { get; set; }
            public string? codArticulo { get; set; }
            public string? claseart { get; set; }
            public string? tipo { get; set; }
            public decimal? valor { get; set; }
        }

        // GET: Predescuentos/Details/5
        // GET: Predescuentos/Details/5
        [HttpGet]
        public async Task<IActionResult> Details(string id, string buNombre, string codCliente)
        {
            id = (id ?? "").Trim();
            buNombre = (buNombre ?? "").Trim();
            codCliente = (codCliente ?? "").Trim();

            if (string.IsNullOrWhiteSpace(id))
                return NotFound();

            try
            {
                static string T(string? s) => (s ?? "").Trim();

                // ===========================
                // 1) ENCABEZADO (LLAVE COMPLETA)
                // ===========================
                var encabezado = await _OracleContext.PREDESCUENTOs
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p =>
                        p.CONSECUTIVO == id &&
                        p.BU_NOMBRE == buNombre &&
                        p.COD_CLIENTE == codCliente);

                if (encabezado == null)
                    return NotFound();

                // ===========================
                // 2) NOMBRE CLIENTE
                // ===========================
                ViewBag.NombreCliente = await _OracleContext.GEN_CLIENTEs
                    .AsNoTracking()
                    .Where(c => c.IDCLIENTE == encabezado.COD_CLIENTE)
                    .Select(c => c.NOMBRE_CLIENTE)
                    .FirstOrDefaultAsync() ?? string.Empty;

                // ===========================
                // 3) DETALLES
                // ===========================
                var detalles = await _OracleContext.PREDETDESCUENTOs
                    .AsNoTracking()
                    .Where(d =>
                        d.BU_NOMBRE == encabezado.BU_NOMBRE &&
                        d.COD_CLIENTE == encabezado.COD_CLIENTE &&
                        d.CONSECUTIVO == encabezado.CONSECUTIVO)
                    .OrderBy(d => d.COD_ARTICULO)
                    .ToListAsync();

                // 👇 Esto te ayuda a confirmar si realmente viene vacío
                // (si querés, dejalo temporalmente)
                // TempData["DebugDetallesCount"] = $"Detalles: {detalles.Count}";

                // helper chunk
                static IEnumerable<List<TItem>> Chunk<TItem>(IEnumerable<TItem> src, int size)
                {
                    var batch = new List<TItem>(size);
                    foreach (var x in src)
                    {
                        batch.Add(x);
                        if (batch.Count == size)
                        {
                            yield return batch;
                            batch = new List<TItem>(size);
                        }
                    }
                    if (batch.Count > 0) yield return batch;
                }

                // ===========================
                // 4) DESCRIPCIONES LÍNEAS
                // ===========================
                var codLineas = detalles
                    .Select(d => T(d.COD_LINEA))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var desLineas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (codLineas.Count > 0)
                {
                    var rows = new List<(string Code, string Name)>();

                    foreach (var chunk in Chunk(codLineas, 900))
                    {
                        var part = await _OracleContext.INV_LINEAs
                            .AsNoTracking()
                            .Where(l => chunk.Contains(l.CATEGORY_CODE))
                            .Select(l => new { l.CATEGORY_CODE, l.CATEGORY_NAME })
                            .ToListAsync();

                        rows.AddRange(part.Select(x => (T(x.CATEGORY_CODE), x.CATEGORY_NAME ?? "")));
                    }

                    desLineas = rows
                        .Where(r => !string.IsNullOrWhiteSpace(r.Code))
                        .GroupBy(r => r.Code, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Select(x => x.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "",
                            StringComparer.OrdinalIgnoreCase
                        );
                }

                ViewBag.DesLineas = desLineas;

                // ===========================
                // 5) DESCRIPCIONES ARTÍCULOS
                // ===========================
                var codArts = detalles
                    .Select(d => T(d.COD_ARTICULO))
                    .Where(x => !string.IsNullOrWhiteSpace(x) && !x.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var desArticulos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (codArts.Count > 0)
                {
                    var rows = new List<(string Code, string Name)>();

                    foreach (var chunk in Chunk(codArts, 900))
                    {
                        var part = await _OracleContext.INV_ARTICULOs
                            .AsNoTracking()
                            .Where(a => chunk.Contains(a.COD_ARTICULO))
                            .Select(a => new { a.COD_ARTICULO, a.DES_ARTICULO })
                            .ToListAsync();

                        rows.AddRange(part.Select(x => (T(x.COD_ARTICULO), x.DES_ARTICULO ?? "")));
                    }

                    desArticulos = rows
                        .Where(r => !string.IsNullOrWhiteSpace(r.Code))
                        .GroupBy(r => r.Code, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Select(x => x.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "",
                            StringComparer.OrdinalIgnoreCase
                        );
                }

                ViewBag.DesArticulos = desArticulos;

                // ===========================
                // 6) PASAR DETALLES AL MODELO (SERVER RENDER)
                // ===========================
                encabezado.PREDETDESCUENTOs = detalles;

                // ===========================
                // 7) (OPCIONAL) JSON PARA JS SI TU TABLA SE PINTA CON JAVASCRIPT
                // ===========================
                var detallesUi = detalles.Select(d => new
                {
                    consecutivodetalle = d.CONSECUTIVODETALLE,
                    codLinea = T(d.COD_LINEA),
                    desLinea = (!string.IsNullOrWhiteSpace(T(d.COD_LINEA)) && desLineas.TryGetValue(T(d.COD_LINEA), out var dl)) ? dl : "",
                    codArticulo = string.IsNullOrWhiteSpace(T(d.COD_ARTICULO)) ? " " : T(d.COD_ARTICULO),
                    desArticulo = (!string.IsNullOrWhiteSpace(T(d.COD_ARTICULO)) && desArticulos.TryGetValue(T(d.COD_ARTICULO), out var da)) ? da : "",
                    tipo = T(d.TIPO),
                    valor = d.VALOR,
                    claseart = string.IsNullOrWhiteSpace(T(d.COD_CLASE)) ? " " : T(d.COD_CLASE)
                }).ToList();

                ViewBag.DetallesJson = JsonConvert.SerializeObject(detallesUi);

                // ===========================
                // 8) ARTÍCULOS + MEDIDAS (lo tuyo)
                // ===========================
                var articulos = await _OracleContext.INV_ARTICULOs
                    .AsNoTracking()
                    .Select(a => new
                    {
                        codLinea = a.COD_LINEA,
                        codArticulo = a.COD_ARTICULO,
                        desArticulo = a.DES_ARTICULO,
                        medida = a.MEDIDA
                    })
                    .ToListAsync();

                ViewBag.ArticulosJson = JsonConvert.SerializeObject(articulos);

                var medidas = await _OracleContext.INV_ARTICULOs
                    .AsNoTracking()
                    .Select(a => a.MEDIDA)
                    .Where(m => m != null && m != "")
                    .Distinct()
                    .OrderBy(m => m)
                    .ToListAsync();

                ViewData["Medidas"] = new SelectList(medidas);

                return View(encabezado);
            }
            catch (OracleException)
            {
                TempData["ErrorMessage"] = "Hubo un problema al comunicarse con la base de datos Oracle.";
                return RedirectToAction("DatabaseError", "Home");
            }
            catch (Exception)
            {
                TempData["ErrorMessage"] = "Ocurrió un error inesperado.";
                return RedirectToAction("Error", "Home");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReversarSolicitud(string CodCia, string Consecutivo)
        {
            if (string.IsNullOrWhiteSpace(CodCia) ||
                string.IsNullOrWhiteSpace(Consecutivo))
                return NotFound();

            static string? Normalizar(string? valor)
            {
                if (string.IsNullOrWhiteSpace(valor))
                    return null;

                valor = valor.Trim();
                return valor.Equals("NULL", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : valor;
            }

            static bool MismaLlave(
                string? linea1,
                string? clase1,
                string? item1,
                string? linea2,
                string? clase2,
                string? item2)
            {
                static string N(string? s) => (s ?? "").Trim().ToUpperInvariant();

                return N(linea1) == N(linea2) &&
                       N(clase1) == N(clase2) &&
                       N(item1) == N(item2);
            }

            const string organizationCode = "CR_3";
            var hoy = DateTime.Today;

            await using var trx = await _OracleContext.Database.BeginTransactionAsync();

            try
            {
                var solicitud = await _OracleContext.PREDESCUENTOs
                    .FirstOrDefaultAsync(p =>
                        p.BU_NOMBRE == CodCia &&
                        p.CONSECUTIVO == Consecutivo);

                if (solicitud == null)
                {
                    await trx.RollbackAsync();
                    return NotFound();
                }

                var estadoActual = (solicitud.ESTADO ?? "").Trim();

                if (!estadoActual.Equals("Aprobado", StringComparison.OrdinalIgnoreCase))
                {
                    await trx.RollbackAsync();

                    TempData["ErrorMessage"] =
                        "Solo se pueden reversar solicitudes aprobadas.";

                    return RedirectToAction(nameof(Details), new
                    {
                        id = solicitud.CONSECUTIVO,
                        buNombre = solicitud.BU_NOMBRE,
                        codCliente = solicitud.COD_CLIENTE
                    });
                }

                var detallesReversa = await _OracleContext.PREDETDESCUENTOs
                    .AsNoTracking()
                    .Where(d =>
                        d.BU_NOMBRE == solicitud.BU_NOMBRE &&
                        d.COD_CLIENTE == solicitud.COD_CLIENTE &&
                        d.CONSECUTIVO == solicitud.CONSECUTIVO)
                    .Select(d => new
                    {
                        d.COD_LINEA,
                        d.COD_CLASE,
                        d.COD_ARTICULO
                    })
                    .ToListAsync();

                var llavesReversa = detallesReversa
                    .Select(d => (
                        Linea: Normalizar(d.COD_LINEA),
                        Clase: Normalizar(d.COD_CLASE),
                        Item: Normalizar(d.COD_ARTICULO)
                    ))
                    .Where(x => !string.IsNullOrWhiteSpace(x.Linea))
                    .Distinct()
                    .ToList();

                // Cargar una sola vez los detalles de solicitudes aprobadas anteriores.
                // La reversa solo tocará las llaves afectadas por la solicitud reversada.
                var anteriores = await (
                    from p in _OracleContext.PREDESCUENTOs.AsNoTracking()
                    join d in _OracleContext.PREDETDESCUENTOs.AsNoTracking()
                        on new
                        {
                            p.BU_NOMBRE,
                            p.COD_CLIENTE,
                            p.CONSECUTIVO
                        }
                        equals new
                        {
                            d.BU_NOMBRE,
                            d.COD_CLIENTE,
                            d.CONSECUTIVO
                        }
                    where p.BU_NOMBRE == solicitud.BU_NOMBRE
                       && p.COD_CLIENTE == solicitud.COD_CLIENTE
                       && p.CONSECUTIVO != solicitud.CONSECUTIVO
                       && p.ESTADO != null
                       && p.ESTADO.Trim().ToUpper() == "APROBADO"
                    select new
                    {
                        p.CONSECUTIVO,
                        p.TIPODESCUENTO,
                        p.FECHAINICIO,
                        p.FECHAFIN,
                        p.FECHA_APLICACION,
                        p.FECHASOLICITUD,
                        d.COD_LINEA,
                        d.COD_CLASE,
                        d.COD_ARTICULO,
                        d.VALOR
                    }
                ).ToListAsync();

                solicitud.ESTADO = "Reversado";
                solicitud.GENERADO = "N";

                _OracleContext.PREDESCUENTOs.Update(solicitud);
                await _OracleContext.SaveChangesAsync();

                foreach (var llave in llavesReversa)
                {
                    // Quitar la versión que estaba vigente para esa llave.
                    await _OracleContext.Database.ExecuteSqlInterpolatedAsync($@"
                        DELETE FROM PREDESCLASEORACLE
                         WHERE TRIM(ORGANIZATION_CODE) = {organizationCode}
                           AND TRIM(IDCLIENTE) = {solicitud.COD_CLIENTE}
                           AND NVL(TRIM(CATEGORY_CODE), '##NULL##') =
                               NVL({llave.Linea}, '##NULL##')
                           AND NVL(TRIM(SUBCATEGORY_CODE), '##NULL##') =
                               NVL({llave.Clase}, '##NULL##')
                           AND NVL(TRIM(ITEM_NUMBER), '##NULL##') =
                               NVL({llave.Item}, '##NULL##')
                    ");

                    // Buscar hacia atrás la última versión APROBADA que siga
                    // siendo válida hoy y restaurarla.
                    var candidatos = anteriores
                        .Where(x => MismaLlave(
                            x.COD_LINEA,
                            x.COD_CLASE,
                            x.COD_ARTICULO,
                            llave.Linea,
                            llave.Clase,
                            llave.Item))
                        .OrderByDescending(x => x.FECHA_APLICACION ?? DateTime.MinValue)
                        .ThenByDescending(x => x.FECHASOLICITUD)
                        .ThenByDescending(x => x.CONSECUTIVO)
                        .ToList();

                    foreach (var candidato in candidatos)
                    {
                        DateTime? fechaInicio = candidato.FECHAINICIO;

                        if (!fechaInicio.HasValue || fechaInicio.Value == default)
                            fechaInicio = candidato.FECHA_APLICACION;

                        if (!fechaInicio.HasValue || fechaInicio.Value == default)
                            fechaInicio = candidato.FECHASOLICITUD;

                        DateTime? fechaFin = candidato.FECHAFIN;

                        if (fechaFin.HasValue && fechaFin.Value == default)
                            fechaFin = null;

                        if (fechaInicio.HasValue && fechaInicio.Value.Date > hoy)
                            continue;

                        if (fechaFin.HasValue && fechaFin.Value.Date < hoy)
                            continue;

                        var item = Normalizar(candidato.COD_ARTICULO);

                        if (!string.IsNullOrWhiteSpace(item))
                        {
                            var bloqueado = await ObtenerArticulosNoPromoActivosAsync(
                                new[] { item },
                                solicitud.BU_NOMBRE,
                                "CR_3",
                                HttpContext.RequestAborted);

                            var noAcepta = await ObtenerArticulosNoAceptanDescuentoAsync(
                                new[] { item },
                                solicitud.BU_NOMBRE,
                                "LCR_3",
                                HttpContext.RequestAborted);

                            if (bloqueado.Count > 0 || noAcepta.Count > 0)
                                continue;

                            bool candidatoPromo =
                                !string.IsNullOrWhiteSpace(candidato.TIPODESCUENTO) &&
                                candidato.TIPODESCUENTO.Contains(
                                    "promocional",
                                    StringComparison.OrdinalIgnoreCase);

                            if (candidatoPromo)
                            {
                                var sinFijo = await ObtenerArticulosSinDescuentoClienteVigenteAsync(
                                    new[] { item },
                                    solicitud.COD_CLIENTE,
                                    solicitud.BU_NOMBRE,
                                    HttpContext.RequestAborted);

                                if (sinFijo.Count > 0)
                                    continue;
                            }
                        }
                        else
                        {
                            if (!await ExisteArticuloElegibleEnScopeAsync(
                                candidato.COD_LINEA,
                                candidato.COD_CLASE,
                                solicitud.BU_NOMBRE,
                                solicitud.COD_CLIENTE,
                                candidato.TIPODESCUENTO,
                                HttpContext.RequestAborted))
                            {
                                continue;
                            }
                        }

                        await _OracleContext.Database.ExecuteSqlInterpolatedAsync($@"
                            INSERT INTO PREDESCLASEORACLE
                            (
                                ORGANIZATION_CODE,
                                IDCLIENTE,
                                CATEGORY_CODE,
                                SUBCATEGORY_CODE,
                                ITEM_NUMBER,
                                PORCENTAJE,
                                FECHA_INICIO,
                                FECHA_FIN
                            )
                            VALUES
                            (
                                {organizationCode},
                                {solicitud.COD_CLIENTE},
                                {Normalizar(candidato.COD_LINEA)},
                                {Normalizar(candidato.COD_CLASE)},
                                {Normalizar(candidato.COD_ARTICULO)},
                                {candidato.VALOR},
                                {fechaInicio},
                                {fechaFin}
                            )
                        ");

                        break;
                    }
                }

                await trx.CommitAsync();

                TempData["InfoFlujo"] =
                    $"La solicitud {solicitud.CONSECUTIVO} fue reversada correctamente.";

                return RedirectToAction(nameof(Details), new
                {
                    id = solicitud.CONSECUTIVO,
                    buNombre = solicitud.BU_NOMBRE,
                    codCliente = solicitud.COD_CLIENTE
                });
            }
            catch
            {
                await trx.RollbackAsync();
                throw;
            }
        }

        // GET: Predescuentos/Create
        [HttpGet]
        public async Task<IActionResult> Create(string? copiarDeConsecutivo, string? copiarDeBu)
        {
            // ✅ Defaults para que la vista nunca truene
            ViewData["ArticulosJson"] = "[]";
            ViewData["DetallesCopiaJson"] = "[]";
            ViewData["CopiaOrigenInfo"] = null; // ✅ IMPORTANTE


            await CargarCombosCreateAsync();

            copiarDeConsecutivo = (copiarDeConsecutivo ?? "").Trim();
            copiarDeBu = (copiarDeBu ?? "").Trim();

            // Si NO viene origen, se comporta igual que siempre
            if (string.IsNullOrWhiteSpace(copiarDeConsecutivo))
                return View(new PREDESCUENTO
                {
                    FECHASOLICITUD = DateTime.Today,
                    BU_NOMBRE = "LANCO_CR"
                });

            // 1) Traer encabezado origen (solo para validar/usar tipodescuento si querés)
            var origen = await _OracleContext.PREDESCUENTOs
                .AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.CONSECUTIVO == copiarDeConsecutivo &&
                    (string.IsNullOrWhiteSpace(copiarDeBu) || p.BU_NOMBRE == copiarDeBu));

            if (origen == null)
            {
                TempData["ErrorMessage"] = "No se encontró la solicitud origen para copiar descuentos.";
                return RedirectToAction(nameof(Index));
            }

            if (!string.Equals(
                (origen.ESTADO ?? "").Trim(),
                "Aprobado",
                StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] =
                    "Solo se pueden copiar descuentos desde una solicitud aprobada.";
                return RedirectToAction(nameof(Index));
            }

            // 2) Traer detalles origen
            var detalles = await _OracleContext.PREDETDESCUENTOs
                .AsNoTracking()
                .Where(d =>
                    d.CONSECUTIVO == origen.CONSECUTIVO &&
                    d.BU_NOMBRE == origen.BU_NOMBRE &&
                    d.COD_CLIENTE == origen.COD_CLIENTE)
                .OrderBy(d => d.COD_LINEA)
                .ToListAsync();

            // Copia inicial: solo artículos que SIGUEN siendo elegibles.
            var articulosCopia = detalles
                .Select(d => NormalizeBlankOrNull(d.COD_ARTICULO))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var bloqueadosCopia = await ObtenerArticulosNoPromoActivosAsync(
                articulosCopia,
                origen.BU_NOMBRE,
                "CR_3",
                HttpContext.RequestAborted);

            var noAceptanCopia = await ObtenerArticulosNoAceptanDescuentoAsync(
                articulosCopia,
                origen.BU_NOMBRE,
                "LCR_3",
                HttpContext.RequestAborted);

            detalles = detalles
                .Where(d =>
                {
                    var item = NormalizeBlankOrNull(d.COD_ARTICULO);
                    return string.IsNullOrWhiteSpace(item) ||
                           (!bloqueadosCopia.Contains(item) && !noAceptanCopia.Contains(item));
                })
                .ToList();

            // 3) (Opcional) Descripción de línea para pintar bonito en la tabla
            static string T(string? s) => (s ?? "").Trim();

            var codLineas = detalles
                .Select(d => T(d.COD_LINEA))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();


            static IEnumerable<List<string>> Chunk(List<string> src, int size)
            {
                for (int i = 0; i < src.Count; i += size)
                    yield return src.GetRange(i, Math.Min(size, src.Count - i));
            }

            var lineasDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var chunk in Chunk(codLineas, 900))
            {
                var part = await _OracleContext.INV_LINEAs.AsNoTracking()
                    .Where(l => chunk.Contains(l.CATEGORY_CODE))
                    .Select(l => new { l.CATEGORY_CODE, l.CATEGORY_NAME })
                    .ToListAsync();

                foreach (var x in part)
                    lineasDict[(x.CATEGORY_CODE ?? "").Trim()] = x.CATEGORY_NAME ?? "";
            }

            // 4) Proyección al “shape” que tu JS pueda consumir
            //    (si tu JS usa otros nombres, ajustalos aquí)
            var detallesCopia = detalles.Select(d => new
            {
                codLinea = T(d.COD_LINEA),
                desLinea = (!string.IsNullOrWhiteSpace(d.COD_LINEA) && lineasDict.TryGetValue(T(d.COD_LINEA), out var dl)) ? dl : "",

                codArticulo = string.IsNullOrWhiteSpace(T(d.COD_ARTICULO)) ? " " : T(d.COD_ARTICULO),
                tipo = string.IsNullOrWhiteSpace(T(d.TIPO)) ? "P" : T(d.TIPO),
                valor = d.VALOR,
                claseart = string.IsNullOrWhiteSpace(T(d.COD_CLASE)) ? " " : T(d.COD_CLASE)
            }).ToList();

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            ViewData["DetallesCopiaJson"] = System.Text.Json.JsonSerializer.Serialize(detallesCopia, jsonOptions);
            ViewBag.DetallesCopiaJson = ViewData["DetallesCopiaJson"];

            ViewData["CopiaOrigenInfo"] = $"{origen.CONSECUTIVO} ({origen.COD_CLIENTE})"; // ✅

            // 5) Modelo base del Create (NO seteamos cliente, porque el usuario lo va a elegir)
            var model = new PREDESCUENTO
            {
                BU_NOMBRE = "LANCO_CR",
                FECHASOLICITUD = DateTime.Today,

                // Si querés copiar también el tipo:
                TIPODESCUENTO = origen.TIPODESCUENTO,

                // Si querés copiar observaciones:
                // OBSERVACIONES = $"Copiado de solicitud {origen.CONSECUTIVO}"
            };

            ViewBag.CopiaOrigenInfo = $"{origen.CONSECUTIVO} ({origen.COD_CLIENTE})";
            return View(model);
        }
        private async Task CargarCombosCreateAsync(string? selectedCliente = null)
        {
            // ✅ Si tu vista realmente NO usa este SelectList (porque llenás por AJAX),
            // podés comentar todo el bloque de clientes para mejorar performance.
            var clientes = await _OracleContext.GEN_CLIENTEs
                .AsNoTracking()
                .OrderBy(c => c.NOMBRE_CLIENTE)
                .Select(c => new { c.IDCLIENTE, c.NOMBRE_CLIENTE })
                .ToListAsync();

            ViewData["Cliente"] = new SelectList(clientes, "IDCLIENTE", "NOMBRE_CLIENTE", selectedCliente);

            // ✅ Medidas (sí se usan en el modal)
            var medidas = await GetCatalogoMedidasAsync();
            ViewData["Medidas"] = new SelectList(medidas);

            // ✅ IMPORTANTÍSIMO: NO precargar artículos
            ViewData["ArticulosJson"] = "[]";
            ViewBag.ArticulosJson = ViewData["ArticulosJson"];
        }


        // POST: Predescuentos/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        // POST: Predescuentos/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PREDESCUENTO model)
        {
            if (!User.Identity.IsAuthenticated)
                return Unauthorized();

            bool tieneErrores = false;

            // =========================
            // Validaciones explícitas
            // =========================

            if (string.IsNullOrEmpty(model.COD_CLIENTE))
            {
                ModelState.AddModelError(nameof(model.COD_CLIENTE), "Cliente es obligatorio.");
                tieneErrores = true;
            }

            if (model.FECHASOLICITUD == default)
            {
                ModelState.AddModelError(nameof(model.FECHASOLICITUD), "Fecha de solicitud es obligatoria.");
                tieneErrores = true;
            }

            if (string.IsNullOrEmpty(model.TIPODESCUENTO))
            {
                ModelState.AddModelError(nameof(model.TIPODESCUENTO), "Tipo de descuento es obligatorio.");
                tieneErrores = true;
            }

            var tip = (model.TIPODESCUENTO ?? "").Trim();
            var esPromo = tip.Equals("Descuento Promocional", StringComparison.OrdinalIgnoreCase)
                       || tip.Equals("promocional", StringComparison.OrdinalIgnoreCase);

            if (esPromo)
            {
                if (model.FECHAINICIO == default)
                {
                    ModelState.AddModelError(nameof(model.FECHAINICIO), "Fecha inicio es obligatoria para descuento promocional.");
                    tieneErrores = true;
                }
                if (model.FECHAFIN == default)
                {
                    ModelState.AddModelError(nameof(model.FECHAFIN), "Fecha fin es obligatoria para descuento promocional.");
                    tieneErrores = true;
                }
                if (model.FECHAINICIO > model.FECHAFIN)
                {
                    ModelState.AddModelError(nameof(model.FECHAFIN), "Fecha fin debe ser mayor o igual a fecha inicio.");
                    tieneErrores = true;
                }
            }
            else
            {
                // Si tu entidad permite null, dejalas en null.
                // Si son DateTime no-nullable, al menos NO las uses.
                model.FECHAINICIO = default;
                model.FECHAFIN = default;
            }


            if (string.IsNullOrEmpty(model.ESTADO))
            {
                ModelState.AddModelError(nameof(model.ESTADO), "Estado es obligatorio.");
                tieneErrores = true;
            }

            model.BU_NOMBRE = "LANCO_CR";

            if (tieneErrores)
            {
                // Volvemos a llenar el combo de clientes para que la vista no reviente
                var clientes = _OracleContext.GEN_CLIENTEs
                    .AsNoTracking()
                    .OrderBy(c => c.NOMBRE_CLIENTE)
                    .ToList();

                ViewData["Cliente"] = new SelectList(clientes, "IDCLIENTE", "NOMBRE_CLIENTE");
                await CargarCombosCreateAsync(model.COD_CLIENTE);
                return View(model);
            }

            // =========================================================
            // Validar si ya existe una solicitud pendiente en Oracle
            // =========================================================
            bool existeSolicitudPendiente = await _OracleContext.PREDESCUENTOs.AnyAsync(p =>
                p.BU_NOMBRE == model.BU_NOMBRE &&
                p.COD_CLIENTE == model.COD_CLIENTE &&
                (p.ESTADO == "Pendiente" || p.ESTADO == "Pendiente Aprobacion")
            );

            if (existeSolicitudPendiente)
            {
                TempData["AdvertenciaCliente"] = "Este cliente ya tiene una solicitud pendiente o en aprobación.";

                var clientes = _OracleContext.GEN_CLIENTEs
                    .AsNoTracking()
                    .OrderBy(c => c.NOMBRE_CLIENTE)
                    .ToList();

                ViewData["Cliente"] = new SelectList(clientes, "IDCLIENTE", "NOMBRE_CLIENTE");
                await CargarCombosCreateAsync(model.COD_CLIENTE);
                return View(model);
            }

            // Defensa de servidor: aunque manipulen el POST, un artículo ACTIVO en
            // ART_NO_PROMO no puede guardarse como detalle explícito.
            var articulosPost = (model.PREDETDESCUENTOs ?? new List<PREDETDESCUENTO>())
                .Select(d => NormalizeBlankOrNull(d.COD_ARTICULO))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var bloqueadosPost = await ObtenerArticulosNoPromoActivosAsync(
                articulosPost,
                model.BU_NOMBRE,
                "CR_3",
                HttpContext.RequestAborted);

            var noAceptanPost = await ObtenerArticulosNoAceptanDescuentoAsync(
                articulosPost,
                model.BU_NOMBRE,
                "LCR_3",
                HttpContext.RequestAborted);

            if (bloqueadosPost.Count > 0 || noAceptanPost.Count > 0)
            {
                if (bloqueadosPost.Count > 0)
                {
                    ModelState.AddModelError(
                        "",
                        "No se pueden incluir artículos activos en ART_NO_PROMO: " +
                        string.Join(", ", bloqueadosPost.OrderBy(x => x)));
                }

                if (noAceptanPost.Count > 0)
                {
                    ModelState.AddModelError(
                        "",
                        "No se pueden incluir artículos con ACCEPTADESCUENTO distinto de S: " +
                        string.Join(", ", noAceptanPost.OrderBy(x => x)));
                }

                await CargarCombosCreateAsync(model.COD_CLIENTE);
                return View(model);
            }

            if (esPromo && articulosPost.Count > 0)
            {
                var sinFijoVigente = await ObtenerArticulosSinDescuentoClienteVigenteAsync(
                    articulosPost,
                    model.COD_CLIENTE,
                    model.BU_NOMBRE,
                    HttpContext.RequestAborted);

                if (sinFijoVigente.Count > 0)
                {
                    ModelState.AddModelError(
                        "",
                        "En descuento promocional solo se permiten artículos con descuento CLIENTE vigente: " +
                        string.Join(", ", sinFijoVigente.OrderBy(x => x)));

                    await CargarCombosCreateAsync(model.COD_CLIENTE);
                    return View(model);
                }
            }

            var maxConsec = await GetMaxConsecutivoAsync(model.BU_NOMBRE);
            var nuevoConsecutivo = (maxConsec + 1).ToString("D3");

            // =========================
            // Encabezado PREDESCUENTO
            // =========================
            var predescuento = new SolicitudesDescuentos.ModelsOracle.PREDESCUENTO
            {
                BU_NOMBRE = model.BU_NOMBRE,
                COD_CLIENTE = model.COD_CLIENTE,
                CONSECUTIVO = nuevoConsecutivo,
                FECHASOLICITUD = model.FECHASOLICITUD,
                TIPODESCUENTO = model.TIPODESCUENTO,
                FECHAINICIO = model.FECHAINICIO,
                FECHAFIN = model.FECHAFIN,
                OBSERVACIONES = model.OBSERVACIONES,
                ESTADO = model.ESTADO,
                AUTORIZADO_POR = model.AUTORIZADO_POR,
                FECHAREGISTRO = DateTime.Now,
                INGRESADO_POR = User.Identity?.Name ?? string.Empty,
                FECHA_APLICACION = null
            };

            _OracleContext.PREDESCUENTOs.Add(predescuento);

            // =========================
            // Detalles PREDETDESCUENTO
            // =========================

            // Máximo consecutivo de detalle por compañía (BU_NOMBRE)
            var maxConsecDetalle = await _OracleContext.PREDETDESCUENTOs
                .Where(d => d.BU_NOMBRE == model.BU_NOMBRE)
                .MaxAsync(d => (int?)d.CONSECUTIVODETALLE) ?? 0;

            int contadorDetalle = maxConsecDetalle;

            foreach (var detalle in model.PREDETDESCUENTOs ?? new List<PREDETDESCUENTO>())
            {
                var codLinea = NormalizeBlankOrNull(detalle.COD_LINEA);
                if (codLinea == null) continue; // no metas detalles sin línea

                var codArticulo = NormalizeBlankOrNull(detalle.COD_ARTICULO);
                var clase = NormalizeBlankOrNull(detalle.COD_CLASE);

                var tipo = NormalizeBlankOrNull(detalle.TIPO) ?? "P";

                if (detalle.VALOR < 0)
                {
                    ModelState.AddModelError("", "No se permiten valores negativos en detalles.");
                    await CargarCombosCreateAsync(model.COD_CLIENTE);
                    return View(model);
                }

                contadorDetalle++;

                _OracleContext.PREDETDESCUENTOs.Add(new PREDETDESCUENTO
                {
                    BU_NOMBRE = model.BU_NOMBRE,
                    COD_CLIENTE = model.COD_CLIENTE,
                    CONSECUTIVO = nuevoConsecutivo,
                    FECHASOLICITUD = model.FECHASOLICITUD,
                    COD_LINEA = codLinea,
                    COD_ARTICULO = codArticulo,
                    COD_CLASE = clase,
                    TIPO = tipo,
                    VALOR = detalle.VALOR,
                    CONSECUTIVODETALLE = contadorDetalle
                });
            }

            await _OracleContext.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        private static string? NormalizeBlankOrNull(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            return s.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? null : s;
        }

        private async Task<int> GetMaxConsecutivoAsync(string buNombre)
        {
            var conn = _OracleContext.Database.GetDbConnection();

            // Solo cerrar si yo la abrí
            var shouldClose = conn.State != ConnectionState.Open;

            if (shouldClose)
                await _OracleContext.Database.OpenConnectionAsync();

            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
            SELECT NVL(MAX(TO_NUMBER(CONSECUTIVO)),0)
            FROM PREDESCUENTO
            WHERE BU_NOMBRE = :bu";

                var p = cmd.CreateParameter();
                p.ParameterName = "bu";
                p.Value = buNombre;
                cmd.Parameters.Add(p);

                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            finally
            {
                if (shouldClose)
                    await _OracleContext.Database.CloseConnectionAsync();
            }
        }
        private async Task<List<string>> GetCatalogoMedidasAsync()
        {
            // 👉 Cambiá INV_MEDIDAS por tu DbSet real y el campo por el real
            return await _OracleContext.INV_MEDIDAs
                .AsNoTracking()
                .Where(x => x.PRIMARY_UOM_CODE != null && x.PRIMARY_UOM_CODE != "")
                .Select(x => x.PRIMARY_UOM_CODE)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();
        }

        // GET: Predescuentos/Edit/5
        [HttpGet]
        public async Task<IActionResult> Edit(string consecutivo, string buNombre, string codCliente)
        {
            consecutivo = (consecutivo ?? "").Trim();
            buNombre = (buNombre ?? "").Trim();
            codCliente = (codCliente ?? "").Trim();

            if (string.IsNullOrWhiteSpace(consecutivo))
                return NotFound();

            // ✅ Buscar por llave completa si viene
            SolicitudesDescuentos.ModelsOracle.PREDESCUENTO? predescuento = null;

            if (!string.IsNullOrWhiteSpace(buNombre) && !string.IsNullOrWhiteSpace(codCliente))
            {
                predescuento = await _OracleContext.PREDESCUENTOs
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p =>
                        p.CONSECUTIVO == consecutivo &&
                        p.BU_NOMBRE == buNombre &&
                        p.COD_CLIENTE == codCliente);
            }

            // fallback por si te siguen entrando enlaces viejos solo con consecutivo
            predescuento ??= await _OracleContext.PREDESCUENTOs
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.CONSECUTIVO == consecutivo);

            if (predescuento == null)
                return NotFound();

            // Cargar detalles SIEMPRE desde la tabla (más confiable que Include)
            var detallesDb = await _OracleContext.PREDETDESCUENTOs
                .AsNoTracking()
                .Where(d =>
                    d.BU_NOMBRE == predescuento.BU_NOMBRE &&
                    d.COD_CLIENTE == predescuento.COD_CLIENTE &&
                    d.CONSECUTIVO == predescuento.CONSECUTIVO)
                .OrderBy(d => d.COD_LINEA)
                .ToListAsync();

            // Si la elegibilidad cambió después de crear la solicitud,
            // no volver a mostrar artículos explícitos que ya no pueden usarse.
            var articulosEditGet = detallesDb
                .Select(d => NormalizeBlankOrNull(d.COD_ARTICULO))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var bloqueadosEditGet = await ObtenerArticulosNoPromoActivosAsync(
                articulosEditGet,
                predescuento.BU_NOMBRE,
                "CR_3",
                HttpContext.RequestAborted);

            var noAceptanEditGet = await ObtenerArticulosNoAceptanDescuentoAsync(
                articulosEditGet,
                predescuento.BU_NOMBRE,
                "LCR_3",
                HttpContext.RequestAborted);

            var esPromoEditGet =
                !string.IsNullOrWhiteSpace(predescuento.TIPODESCUENTO) &&
                predescuento.TIPODESCUENTO.Contains("promocional", StringComparison.OrdinalIgnoreCase);

            var sinFijoEditGet = esPromoEditGet
                ? await ObtenerArticulosSinDescuentoClienteVigenteAsync(
                    articulosEditGet,
                    predescuento.COD_CLIENTE,
                    predescuento.BU_NOMBRE,
                    HttpContext.RequestAborted)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            detallesDb = detallesDb
                .Where(d =>
                {
                    var item = NormalizeBlankOrNull(d.COD_ARTICULO);
                    return string.IsNullOrWhiteSpace(item) ||
                           (!bloqueadosEditGet.Contains(item) &&
                            !noAceptanEditGet.Contains(item) &&
                            !sinFijoEditGet.Contains(item));
                })
                .ToList();

            // Diccionarios de descripciones (opcional, pero ayuda a pintar bonito)
            string T(string? s) => (s ?? "").Trim();

            var codLineas = detallesDb.Select(d => T(d.COD_LINEA))
                                      .Where(x => x != "")
                                      .Distinct()
                                      .ToList();

            var lineasDict = await _OracleContext.INV_LINEAs.AsNoTracking()
                .Where(l => codLineas.Contains(l.CATEGORY_CODE))
                .Select(l => new { l.CATEGORY_CODE, l.CATEGORY_NAME })
                .ToDictionaryAsync(x => T(x.CATEGORY_CODE), x => x.CATEGORY_NAME ?? "");

            var codArts = detallesDb.Select(d => T(d.COD_ARTICULO))
                                    .Where(x => x != "" && !x.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                                    .Distinct()
                                    .ToList();

            var artsDict = await _OracleContext.INV_ARTICULOs.AsNoTracking()
                .Where(a => codArts.Contains(a.COD_ARTICULO))
                .Select(a => new { a.COD_ARTICULO, a.DES_ARTICULO })
                .ToDictionaryAsync(x => T(x.COD_ARTICULO), x => x.DES_ARTICULO ?? "");

            // ✅ Mandar JSON en camelCase como el JS lo pinta
            var detallesUi = detallesDb.Select(d => new
            {
                consecutivodetalle = d.CONSECUTIVODETALLE,
                codLinea = T(d.COD_LINEA),
                desLinea = (T(d.COD_LINEA) != "" && lineasDict.TryGetValue(T(d.COD_LINEA), out var dl)) ? dl : "",
                codArticulo = T(d.COD_ARTICULO),
                desArticulo = (T(d.COD_ARTICULO) != "" && artsDict.TryGetValue(T(d.COD_ARTICULO), out var da)) ? da : "",
                tipo = T(d.TIPO) == "" ? "P" : T(d.TIPO),
                valor = d.VALOR,
                claseart = T(d.COD_CLASE)
            }).ToList();

            ViewData["DetallesJson"] = JsonConvert.SerializeObject(detallesUi);

            var medidas = await _OracleContext.INV_ARTICULOs.AsNoTracking()
            .Where(a => a.MEDIDA != null && a.MEDIDA.Trim() != "")
            .Select(a => a.MEDIDA.Trim())
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

            ViewData["Medidas"] = new SelectList(medidas);

            var articulosAll = await _OracleContext.INV_ARTICULOs.AsNoTracking()
                .Select(a => new {
                    codArticulo = (a.COD_ARTICULO ?? "").Trim(),
                    desArticulo = (a.DES_ARTICULO ?? "").Trim(),
                    medida = (a.MEDIDA ?? "").Trim(),
                    codLinea = (a.COD_LINEA ?? "").Trim()
                })
                .ToListAsync();

            ViewBag.ArticulosJson = JsonConvert.SerializeObject(articulosAll);

            // Cliente label
            var cliente = await _OracleContext.GEN_CLIENTEs.AsNoTracking()
                .FirstOrDefaultAsync(c => c.IDCLIENTE == predescuento.COD_CLIENTE);

            ViewData["ClienteCod"] = predescuento.COD_CLIENTE ?? "";
            ViewData["ClienteNom"] = cliente?.NOMBRE_CLIENTE ?? "";

            return View(predescuento);
        }

        // POST: Predescuentos/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(SolicitudesDescuentos.ModelsOracle.PREDESCUENTO model)
        {
            if (!User.Identity.IsAuthenticated)
                return Unauthorized();

            bool tieneErrores = false;

            // =========================
            // Validaciones (equivalentes)
            // =========================
            if (string.IsNullOrEmpty(model.COD_CLIENTE))
            {
                ModelState.AddModelError(nameof(model.COD_CLIENTE), "Cliente es obligatorio.");
                tieneErrores = true;
            }

            if (string.IsNullOrEmpty(model.BU_NOMBRE))
            {
                ModelState.AddModelError(nameof(model.BU_NOMBRE), "Compañía es obligatoria.");
                tieneErrores = true;
            }

            if (model.FECHASOLICITUD == default)
            {
                ModelState.AddModelError(nameof(model.FECHASOLICITUD), "Fecha de solicitud es obligatoria.");
                tieneErrores = true;
            }

            if (string.IsNullOrEmpty(model.TIPODESCUENTO))
            {
                ModelState.AddModelError(nameof(model.TIPODESCUENTO), "Tipo de descuento es obligatorio.");
                tieneErrores = true;
            }

            if (model.FECHAINICIO > model.FECHAFIN)
            {
                ModelState.AddModelError(nameof(model.FECHAFIN), "Fecha fin debe ser mayor o igual a fecha inicio.");
                tieneErrores = true;
            }

            if (string.IsNullOrEmpty(model.ESTADO))
            {
                ModelState.AddModelError(nameof(model.ESTADO), "Estado es obligatorio.");
                tieneErrores = true;
            }

            // JSON con los detalles
            var detallesJson = Request.Form["DetallesJson"];
            if (string.IsNullOrEmpty(detallesJson))
            {
                ModelState.AddModelError("", "Debe agregar al menos un detalle.");
                tieneErrores = true;
            }

            var detallesNuevos = new List<SolicitudesDescuentos.ModelsOracle.PREDETDESCUENTO>();

            if (!tieneErrores)
            {
                try
                {
                    // Deserializar con System.Text.Json
                    var jsonDoc = System.Text.Json.JsonDocument.Parse(detallesJson);
                    var jsonArray = jsonDoc.RootElement;

                    int maxConsecutivo = await _OracleContext.PREDETDESCUENTOs
                        .Where(d => d.BU_NOMBRE == model.BU_NOMBRE)
                        .MaxAsync(d => (int?)d.CONSECUTIVODETALLE) ?? 0;

                    foreach (var item in jsonArray.EnumerateArray())
                    {
                        // Leer el consecutivodetalle del JSON
                        int consecutivodetalle = 0;
                        if (item.TryGetProperty("Consecutivodetalle", out var propConsec))
                        {
                            if (propConsec.ValueKind == System.Text.Json.JsonValueKind.Number)
                            {
                                consecutivodetalle = propConsec.GetInt32();
                            }
                            else if (propConsec.ValueKind == System.Text.Json.JsonValueKind.String &&
                                     int.TryParse(propConsec.GetString(), out int valor))
                            {
                                consecutivodetalle = valor;
                            }
                        }

                        if (consecutivodetalle == 0)
                        {
                            maxConsecutivo++;
                            consecutivodetalle = maxConsecutivo;
                        }

                        // Leer propiedades desde el JSON
                        string codLinea = item.TryGetProperty("CodLinea", out var pCodLinea)
                            ? pCodLinea.GetString()
                            : null;

                        string codArticuloRaw = item.TryGetProperty("CodArticulo", out var pCodArticulo)
                            ? pCodArticulo.GetString()
                            : null;

                        string tipo = item.TryGetProperty("Tipo", out var pTipo)
                            ? pTipo.GetString()
                            : null;

                        decimal valorDec = 0;
                        if (item.TryGetProperty("Valor", out var pValor))
                        {
                            if (pValor.ValueKind == System.Text.Json.JsonValueKind.Number)
                            {
                                valorDec = pValor.GetDecimal();
                            }
                            else if (pValor.ValueKind == System.Text.Json.JsonValueKind.String &&
                                     decimal.TryParse(pValor.GetString(), out var val))
                            {
                                valorDec = val;
                            }
                        }

                        string claseRaw = item.TryGetProperty("Claseart", out var pClaseart)
                            ? pClaseart.GetString()
                            : null;

                        // Normalizar "NULL" a null real
                        string codArticulo = NormalizeNullString(codArticuloRaw);
                        string clase = NormalizeNullString(claseRaw);

                        var detalle = new SolicitudesDescuentos.ModelsOracle.PREDETDESCUENTO
                        {
                            CONSECUTIVODETALLE = consecutivodetalle,
                            BU_NOMBRE = model.BU_NOMBRE,
                            COD_CLIENTE = model.COD_CLIENTE,
                            CONSECUTIVO = model.CONSECUTIVO,
                            FECHASOLICITUD = model.FECHASOLICITUD,
                            COD_LINEA = codLinea,
                            COD_ARTICULO = codArticulo,
                            TIPO = tipo,
                            VALOR = valorDec,
                            COD_CLASE = clase
                        };

                        detallesNuevos.Add(detalle);
                    }
                }
                catch
                {
                    ModelState.AddModelError("", "Error al procesar los detalles.");
                    tieneErrores = true;
                }
            }

            if (!tieneErrores)
            {
                // Defensa de servidor para edición: tampoco aceptar por POST
                // artículos explícitos ACTIVO en ART_NO_PROMO.
                var articulosEdit = detallesNuevos
                    .Select(d => d.COD_ARTICULO)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var bloqueadosEdit = await ObtenerArticulosNoPromoActivosAsync(
                    articulosEdit,
                    model.BU_NOMBRE,
                    "CR_3",
                    HttpContext.RequestAborted);

                var noAceptanEdit = await ObtenerArticulosNoAceptanDescuentoAsync(
                    articulosEdit,
                    model.BU_NOMBRE,
                    "LCR_3",
                    HttpContext.RequestAborted);

                if (bloqueadosEdit.Count > 0)
                {
                    ModelState.AddModelError(
                        "",
                        "No se pueden incluir artículos activos en ART_NO_PROMO: " +
                        string.Join(", ", bloqueadosEdit.OrderBy(x => x)));
                    tieneErrores = true;
                }

                if (noAceptanEdit.Count > 0)
                {
                    ModelState.AddModelError(
                        "",
                        "No se pueden incluir artículos con ACCEPTADESCUENTO distinto de S: " +
                        string.Join(", ", noAceptanEdit.OrderBy(x => x)));
                    tieneErrores = true;
                }

                var esPromoEdit =
                    !string.IsNullOrWhiteSpace(model.TIPODESCUENTO) &&
                    model.TIPODESCUENTO.Contains("promocional", StringComparison.OrdinalIgnoreCase);

                if (esPromoEdit && articulosEdit.Count > 0)
                {
                    var sinFijoVigenteEdit = await ObtenerArticulosSinDescuentoClienteVigenteAsync(
                        articulosEdit,
                        model.COD_CLIENTE,
                        model.BU_NOMBRE,
                        HttpContext.RequestAborted);

                    if (sinFijoVigenteEdit.Count > 0)
                    {
                        ModelState.AddModelError(
                            "",
                            "En descuento promocional solo se permiten artículos con descuento CLIENTE vigente: " +
                            string.Join(", ", sinFijoVigenteEdit.OrderBy(x => x)));
                        tieneErrores = true;
                    }
                }
            }

            if (tieneErrores)
            {
                // Reenviar JSON y nombre de cliente a la vista
                var cliente = await _OracleContext.GEN_CLIENTEs
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.IDCLIENTE == model.COD_CLIENTE);

                ViewData["ClienteCod"] = model.COD_CLIENTE ?? "";
                ViewData["ClienteNom"] = cliente?.NOMBRE_CLIENTE ?? "";
                ViewBag.NombreCliente = cliente?.NOMBRE_CLIENTE ?? "Cliente no encontrado";

                // y además:
                ViewData["DetallesJson"] = detallesJson;
                return View(model);
            }

            // =========================
            // Transacción en Oracle
            // =========================
            using var transaction = await _OracleContext.Database.BeginTransactionAsync();

            try
            {
                // Actualizar encabezado (PREDESCUENTO)
                var predescuentoDb = await _OracleContext.PREDESCUENTOs
                    .FirstOrDefaultAsync(p =>
                        p.BU_NOMBRE == model.BU_NOMBRE &&
                        p.COD_CLIENTE == model.COD_CLIENTE &&
                        p.CONSECUTIVO == model.CONSECUTIVO
                    );

                if (predescuentoDb == null)
                {
                    await transaction.RollbackAsync();
                    return NotFound();
                }

                predescuentoDb.FECHASOLICITUD = model.FECHASOLICITUD;
                predescuentoDb.TIPODESCUENTO = model.TIPODESCUENTO;
                predescuentoDb.FECHAINICIO = model.FECHAINICIO;
                predescuentoDb.FECHAFIN = model.FECHAFIN;
                predescuentoDb.OBSERVACIONES = model.OBSERVACIONES;
                predescuentoDb.ESTADO = model.ESTADO;
                predescuentoDb.AUTORIZADO_POR = model.AUTORIZADO_POR;
                predescuentoDb.FECHAREGISTRO = DateTime.Now;
                predescuentoDb.INGRESADO_POR = User.Identity?.Name ?? string.Empty;
                predescuentoDb.FECHA_APLICACION = model.FECHA_APLICACION;

                _OracleContext.PREDESCUENTOs.Update(predescuentoDb);

                // Traer detalles actuales en BD
                var detallesExistentes = await _OracleContext.PREDETDESCUENTOs
                    .Where(d =>
                        d.BU_NOMBRE == model.BU_NOMBRE &&
                        d.COD_CLIENTE == model.COD_CLIENTE &&
                        d.CONSECUTIVO == model.CONSECUTIVO)
                    .ToListAsync();

                // Detalles a eliminar: existen en BD pero no en nuevos
                var detallesAEliminar = detallesExistentes
                    .Where(dbe => !detallesNuevos.Any(dn => dn.CONSECUTIVODETALLE == dbe.CONSECUTIVODETALLE))
                    .ToList();

                _OracleContext.PREDETDESCUENTOs.RemoveRange(detallesAEliminar);

                // Detalles a agregar: nuevos que no están en BD
                var detallesAAgregar = detallesNuevos
                    .Where(dn => !detallesExistentes.Any(dbe => dbe.CONSECUTIVODETALLE == dn.CONSECUTIVODETALLE))
                    .ToList();

                foreach (var det in detallesAAgregar)
                {
                    _OracleContext.PREDETDESCUENTOs.Add(det);
                }

                // Detalles a actualizar: están en ambos por CONSECUTIVODETALLE
                var detallesAActualizar = detallesNuevos
                    .Where(dn => detallesExistentes.Any(dbe => dbe.CONSECUTIVODETALLE == dn.CONSECUTIVODETALLE))
                    .ToList();

                foreach (var detUpd in detallesAActualizar)
                {
                    var detExist = detallesExistentes
                        .First(d => d.CONSECUTIVODETALLE == detUpd.CONSECUTIVODETALLE);

                    bool huboCambios = false;

                    if (detExist.COD_LINEA != detUpd.COD_LINEA)
                    {
                        detExist.COD_LINEA = detUpd.COD_LINEA;
                        huboCambios = true;
                    }
                    if (detExist.COD_ARTICULO != detUpd.COD_ARTICULO)
                    {
                        detExist.COD_ARTICULO = detUpd.COD_ARTICULO;
                        huboCambios = true;
                    }
                    if (detExist.TIPO != detUpd.TIPO)
                    {
                        detExist.TIPO = detUpd.TIPO;
                        huboCambios = true;
                    }
                    if (detExist.VALOR != detUpd.VALOR)
                    {
                        detExist.VALOR = detUpd.VALOR;
                        huboCambios = true;
                    }
                    if (detExist.COD_CLASE != detUpd.COD_CLASE)
                    {
                        detExist.COD_CLASE = detUpd.COD_CLASE;
                        huboCambios = true;
                    }

                    if (huboCambios)
                    {
                        _OracleContext.PREDETDESCUENTOs.Update(detExist);
                    }
                }

                await _OracleContext.SaveChangesAsync();
                await transaction.CommitAsync();

                // ✅ Redirección según estado final
                var estadoFinal = (predescuentoDb.ESTADO ?? "").Trim();

                if (estadoFinal.Equals("Pendiente Aprobacion", StringComparison.OrdinalIgnoreCase))
                {
                    // vuelve a Details para que puedan procesar desde ahí
                    return RedirectToAction(nameof(Details), new
                    {
                        id = predescuentoDb.CONSECUTIVO,
                        buNombre = predescuentoDb.BU_NOMBRE,
                        codCliente = predescuentoDb.COD_CLIENTE
                    });
                }

                // si queda en Pendiente (o cualquier otro), vuelve a Index (podés filtrar por cliente si querés)
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync();

                bool existe = await _OracleContext.PREDESCUENTOs.AnyAsync(p =>
                    p.BU_NOMBRE == model.BU_NOMBRE &&
                    p.COD_CLIENTE == model.COD_CLIENTE &&
                    p.CONSECUTIVO == model.CONSECUTIVO
                );

                if (!existe)
                    return NotFound();

                throw;
            }
        }

        private string? NormalizeNullString(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            if (input.Trim().ToUpper() == "NULL")
                return null;

            return input.Trim();
        }

        private bool PredescuentoExists(string consecutivo)
        {
            return _OracleContext.PREDESCUENTOs
                .Any(p => p.CONSECUTIVO == consecutivo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcesarSolicitud(string CodCia, string Consecutivo, string Estado, string Observaciones)
        {
            if (string.IsNullOrEmpty(CodCia) || string.IsNullOrEmpty(Consecutivo))
                return NotFound();

            // Buscar por BU_NOMBRE (antes CodCia) + CONSECUTIVO
            var solicitud = await _OracleContext.PREDESCUENTOs
                .FirstOrDefaultAsync(p =>
                    p.BU_NOMBRE == CodCia &&
                    p.CONSECUTIVO == Consecutivo);

            if (solicitud == null)
                return NotFound();

            // Actualizar estado de la solicitud
            solicitud.ESTADO = Estado;
            solicitud.OBSERVACIONES = Observaciones;
            solicitud.AUTORIZADO_POR = User.Identity?.Name;
            solicitud.FECHA_APLICACION = DateTime.Now;

            _OracleContext.PREDESCUENTOs.Update(solicitud);
            await _OracleContext.SaveChangesAsync();


            // Solo generar en PREDESCUENTOS_MASTER cuando esté aprobada
            // Solo generar y sincronizar cuando esté aprobada
            if (string.Equals(Estado, "Aprobado", StringComparison.OrdinalIgnoreCase))
            {
                await SincronizarPredesclaseOracleAsync(solicitud);
                await GenerarDescuentosMasterAsync(solicitud);
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task SincronizarPredesclaseOracleAsync(PREDESCUENTO solicitud)
        {
            const string organizationCode = "CR_3";

            static string? Normalizar(string? valor)
            {
                if (string.IsNullOrWhiteSpace(valor))
                    return null;

                valor = valor.Trim();
                return valor.Equals("NULL", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : valor;
            }

            var buNombre = (solicitud.BU_NOMBRE ?? "").Trim();
            var codCliente = (solicitud.COD_CLIENTE ?? "").Trim();
            var consecutivo = (solicitud.CONSECUTIVO ?? "").Trim();

            if (string.IsNullOrWhiteSpace(buNombre) ||
                string.IsNullOrWhiteSpace(codCliente) ||
                string.IsNullOrWhiteSpace(consecutivo))
                return;

            var detalles = await _OracleContext.PREDETDESCUENTOs
                .AsNoTracking()
                .Where(d =>
                    d.BU_NOMBRE == buNombre &&
                    d.COD_CLIENTE == codCliente &&
                    d.CONSECUTIVO == consecutivo)
                .ToListAsync();

            if (detalles.Count == 0)
                return;

            DateTime? fechaInicio = solicitud.FECHAINICIO;

            if (!fechaInicio.HasValue || fechaInicio.Value == default)
                fechaInicio = solicitud.FECHA_APLICACION;

            if (!fechaInicio.HasValue || fechaInicio.Value == default)
                fechaInicio = solicitud.FECHASOLICITUD;

            DateTime? fechaFin = solicitud.FECHAFIN;

            if (fechaFin.HasValue && fechaFin.Value == default)
                fechaFin = null;

            var articulosExplicitos = detalles
                .Select(d => Normalizar(d.COD_ARTICULO))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var bloqueadosExplicitos = await ObtenerArticulosNoPromoActivosAsync(
                articulosExplicitos,
                buNombre,
                "CR_3",
                HttpContext.RequestAborted);

            var noAceptanExplicitos = await ObtenerArticulosNoAceptanDescuentoAsync(
                articulosExplicitos,
                buNombre,
                "LCR_3",
                HttpContext.RequestAborted);

            bool esPromo =
                !string.IsNullOrWhiteSpace(solicitud.TIPODESCUENTO) &&
                solicitud.TIPODESCUENTO.Contains(
                    "promocional",
                    StringComparison.OrdinalIgnoreCase);

            var sinFijoVigente = esPromo
                ? await ObtenerArticulosSinDescuentoClienteVigenteAsync(
                    articulosExplicitos,
                    codCliente,
                    buNombre,
                    HttpContext.RequestAborted)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Todas las llaves solicitadas se consideran parte del DELTA.
            // Aunque una llave haya dejado de ser elegible entre el guardado y la
            // aprobación, debe eliminarse su versión anterior de PREDESCLASEORACLE.
            var filasSolicitadas = detalles
                .Select(d => (
                    CategoryCode: Normalizar(d.COD_LINEA),
                    SubcategoryCode: Normalizar(d.COD_CLASE),
                    ItemNumber: Normalizar(d.COD_ARTICULO),
                    Porcentaje: d.VALOR
                ))
                .Where(x => !string.IsNullOrWhiteSpace(x.CategoryCode))
                .GroupBy(x => new
                {
                    x.CategoryCode,
                    x.SubcategoryCode,
                    x.ItemNumber
                })
                .Select(g => g.Last())
                .ToList();

            var filas = filasSolicitadas
                .Where(x =>
                    string.IsNullOrWhiteSpace(x.ItemNumber) ||
                    (
                        !bloqueadosExplicitos.Contains(x.ItemNumber!) &&
                        !noAceptanExplicitos.Contains(x.ItemNumber!) &&
                        !sinFijoVigente.Contains(x.ItemNumber!)
                    ))
                .ToList();

            var filasValidas = new List<(
                string? CategoryCode,
                string? SubcategoryCode,
                string? ItemNumber,
                decimal Porcentaje)>();

            foreach (var fila in filas)
            {
                if (!string.IsNullOrWhiteSpace(fila.ItemNumber))
                {
                    filasValidas.Add(fila);
                    continue;
                }

                if (await ExisteArticuloElegibleEnScopeAsync(
                    fila.CategoryCode,
                    fila.SubcategoryCode,
                    buNombre,
                    codCliente,
                    solicitud.TIPODESCUENTO,
                    HttpContext.RequestAborted))
                {
                    filasValidas.Add(fila);
                }
            }

            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? trxPropia = null;

            if (_OracleContext.Database.CurrentTransaction == null)
                trxPropia = await _OracleContext.Database.BeginTransactionAsync();

            try
            {
                // Mantiene semántica DELTA:
                // solamente reemplaza las llaves presentes en esta solicitud.
                // No borra descuentos de otras llaves que la solicitud no tocó.
                // Primero se quita cualquier versión anterior de TODAS las llaves
                // solicitadas; luego se insertan únicamente las que siguen válidas.
                foreach (var fila in filasSolicitadas)
                {
                    await _OracleContext.Database.ExecuteSqlInterpolatedAsync($@"
                        DELETE FROM PREDESCLASEORACLE
                         WHERE TRIM(ORGANIZATION_CODE) = {organizationCode}
                           AND TRIM(IDCLIENTE) = {codCliente}
                           AND NVL(TRIM(CATEGORY_CODE), '##NULL##') =
                               NVL({fila.CategoryCode}, '##NULL##')
                           AND NVL(TRIM(SUBCATEGORY_CODE), '##NULL##') =
                               NVL({fila.SubcategoryCode}, '##NULL##')
                           AND NVL(TRIM(ITEM_NUMBER), '##NULL##') =
                               NVL({fila.ItemNumber}, '##NULL##')
                    ");
                }

                foreach (var fila in filasValidas)
                {
                    await _OracleContext.Database.ExecuteSqlInterpolatedAsync($@"
                        INSERT INTO PREDESCLASEORACLE
                        (
                            ORGANIZATION_CODE,
                            IDCLIENTE,
                            CATEGORY_CODE,
                            SUBCATEGORY_CODE,
                            ITEM_NUMBER,
                            PORCENTAJE,
                            FECHA_INICIO,
                            FECHA_FIN
                        )
                        VALUES
                        (
                            {organizationCode},
                            {codCliente},
                            {fila.CategoryCode},
                            {fila.SubcategoryCode},
                            {fila.ItemNumber},
                            {fila.Porcentaje},
                            {fechaInicio},
                            {fechaFin}
                        )
                    ");
                }

                if (trxPropia != null)
                    await trxPropia.CommitAsync();
            }
            catch
            {
                if (trxPropia != null)
                    await trxPropia.RollbackAsync();
                throw;
            }
            finally
            {
                if (trxPropia != null)
                    await trxPropia.DisposeAsync();
            }
        }

        private async Task GenerarDescuentosMasterAsync(PREDESCUENTO solicitud)
        {
            // =========================
            // Datos base
            // =========================
            string buNombre = solicitud.BU_NOMBRE;
            string organizationCode = "CR_3";
            string codCliente = solicitud.COD_CLIENTE;
            string consecutivo = solicitud.CONSECUTIVO;
            string usuario = User.Identity?.Name ?? "SYSTEM";
            string fechaTexto = DateTime.Now.ToString("yyyy-MM-dd");

            // ✅ FECHAS de la solicitud a replicar en todos los registros master
            DateTime? fechaInicioMaster = solicitud.FECHAINICIO ?? solicitud.FECHA_APLICACION; // <- si no querés fallback, pon: solicitud.FECHAINICIO
            DateTime? fechaFinMaster = solicitud.FECHAFIN;

            static string T(string? s) => (s ?? "").Trim();

            static string MakeKey(string? linea, string? clase, string? articulo)
                => $"{T(linea)}|{T(clase)}|{T(articulo)}";

            static string MakePairKey(string? linea, string? clase)
                => $"{T(linea)}|{T(clase)}";

            static IEnumerable<List<TItem>> Chunk<TItem>(IEnumerable<TItem> source, int size)
            {
                var batch = new List<TItem>(size);
                foreach (var item in source)
                {
                    batch.Add(item);
                    if (batch.Count == size)
                    {
                        yield return batch;
                        batch = new List<TItem>(size);
                    }
                }
                if (batch.Count > 0) yield return batch;
            }

            // =========================
            // 1) Detalles
            // =========================
            var detalles = await _OracleContext.PREDETDESCUENTOs
                .AsNoTracking()
                .Where(d => d.BU_NOMBRE == buNombre &&
                            d.COD_CLIENTE == codCliente &&
                            d.CONSECUTIVO == consecutivo)
                .ToListAsync();

            if (detalles.Count == 0)
                return;

            // =========================
            // 2) Combos existentes (para no chocar con AK)
            // =========================
            var existentes = await _OracleContext.PREDESCUENTOS_MASTERs
                .AsNoTracking()
                .Where(m => m.BU_NOMBRE == buNombre &&
                            m.COD_CLIENTE == codCliente &&
                            m.CONSECUTIVO == consecutivo)
                .Select(m => new { m.COD_LINEA, m.COD_CLASE, m.COD_ARTICULO })
                .ToListAsync();

            var combosInsertados = new HashSet<string>(
                existentes.Select(e => MakeKey(e.COD_LINEA, e.COD_CLASE, e.COD_ARTICULO)),
                StringComparer.OrdinalIgnoreCase
            );

            // =========================
            // 3) Preparar maps de valores (VALOR) por scope
            // =========================
            var artValor = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in detalles.Where(d => !string.IsNullOrWhiteSpace(d.COD_ARTICULO)))
            {
                var art = T(d.COD_ARTICULO);
                if (!artValor.ContainsKey(art))
                    artValor[art] = d.VALOR;
            }

            var pairValor = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in detalles.Where(d => string.IsNullOrWhiteSpace(d.COD_ARTICULO) &&
                                                 !string.IsNullOrWhiteSpace(d.COD_CLASE)))
            {
                var pk = MakePairKey(d.COD_LINEA, d.COD_CLASE);
                if (!pairValor.ContainsKey(pk))
                    pairValor[pk] = d.VALOR;
            }

            var lineValor = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in detalles.Where(d => !string.IsNullOrWhiteSpace(d.COD_LINEA) &&
                                                 string.IsNullOrWhiteSpace(d.COD_CLASE) &&
                                                 string.IsNullOrWhiteSpace(d.COD_ARTICULO)))
            {
                var ln = T(d.COD_LINEA);
                if (!lineValor.ContainsKey(ln))
                    lineValor[ln] = d.VALOR;
            }

            // =========================
            // 4) Conexión Oracle (1 sola apertura)
            // =========================
            var connection = _OracleContext.Database.GetDbConnection();
            bool cerrarAlFinal = false;

            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
                cerrarAlFinal = true;
            }

            var invByArticulo = new Dictionary<string, (string CodLinea, string CodClase, string Medida)>(StringComparer.OrdinalIgnoreCase);
            var invRowsByPairs = new List<(string CodArticulo, string CodLinea, string CodClase, string Medida)>(capacity: 1024);
            var invRowsByLines = new List<(string CodArticulo, string CodLinea, string CodClase, string Medida)>(capacity: 1024);

            try
            {
                // Q1: por artículos
                var codArticulosUnicos = artValor.Keys.ToList();
                if (codArticulosUnicos.Count > 0)
                {
                    foreach (var chunk in Chunk(codArticulosUnicos, 900))
                    {
                        using var cmd = connection.CreateCommand();
                        var paramNames = new List<string>(chunk.Count);

                        for (int i = 0; i < chunk.Count; i++)
                        {
                            var p = cmd.CreateParameter();
                            p.ParameterName = "a" + i;
                            p.Value = chunk[i];
                            cmd.Parameters.Add(p);
                            paramNames.Add(":" + p.ParameterName);
                        }

                        cmd.CommandText = $@"
                        SELECT COD_ARTICULO, COD_LINEA, COD_CLASE, MEDIDA
                        FROM INV_ARTICULO
                        WHERE COD_ARTICULO IN ({string.Join(",", paramNames)})";

                        using var reader = await cmd.ExecuteReaderAsync();
                        while (await reader.ReadAsync())
                        {
                            var codArt = T(reader["COD_ARTICULO"]?.ToString());
                            if (string.IsNullOrWhiteSpace(codArt)) continue;

                            invByArticulo[codArt] = (
                                T(reader["COD_LINEA"]?.ToString()),
                                T(reader["COD_CLASE"]?.ToString()),
                                T(reader["MEDIDA"]?.ToString())
                            );
                        }
                    }
                }

                // Q2: por pares (linea,clase)
                var pairKeys = pairValor.Keys.ToList();
                if (pairKeys.Count > 0)
                {
                    var pairs = pairKeys
                        .Select(k =>
                        {
                            var parts = k.Split('|');
                            return (Linea: parts.Length > 0 ? parts[0] : "", Clase: parts.Length > 1 ? parts[1] : "");
                        })
                        .Where(x => !string.IsNullOrWhiteSpace(x.Linea) && !string.IsNullOrWhiteSpace(x.Clase))
                        .ToList();

                    foreach (var chunk in Chunk(pairs, 200))
                    {
                        using var cmd = connection.CreateCommand();

                        var whereParts = new List<string>(chunk.Count);
                        for (int i = 0; i < chunk.Count; i++)
                        {
                            var pL = cmd.CreateParameter();
                            pL.ParameterName = "l" + i;
                            pL.Value = chunk[i].Linea;
                            cmd.Parameters.Add(pL);

                            var pC = cmd.CreateParameter();
                            pC.ParameterName = "c" + i;
                            pC.Value = chunk[i].Clase;
                            cmd.Parameters.Add(pC);

                            whereParts.Add($"(COD_LINEA = :{pL.ParameterName} AND COD_CLASE = :{pC.ParameterName})");
                        }

                        cmd.CommandText = $@"
                        SELECT COD_ARTICULO, COD_LINEA, COD_CLASE, MEDIDA
                        FROM INV_ARTICULO
                        WHERE {string.Join(" OR ", whereParts)}";

                        using var reader = await cmd.ExecuteReaderAsync();
                        while (await reader.ReadAsync())
                        {
                            var codArt = T(reader["COD_ARTICULO"]?.ToString());
                            if (string.IsNullOrWhiteSpace(codArt)) continue;

                            invRowsByPairs.Add((
                                codArt,
                                T(reader["COD_LINEA"]?.ToString()),
                                T(reader["COD_CLASE"]?.ToString()),
                                T(reader["MEDIDA"]?.ToString())
                            ));
                        }
                    }
                }

                // Q3: por líneas
                var lineasUnicas = lineValor.Keys.ToList();
                if (lineasUnicas.Count > 0)
                {
                    foreach (var chunk in Chunk(lineasUnicas, 900))
                    {
                        using var cmd = connection.CreateCommand();
                        var paramNames = new List<string>(chunk.Count);

                        for (int i = 0; i < chunk.Count; i++)
                        {
                            var p = cmd.CreateParameter();
                            p.ParameterName = "ln" + i;
                            p.Value = chunk[i];
                            cmd.Parameters.Add(p);
                            paramNames.Add(":" + p.ParameterName);
                        }

                        cmd.CommandText = $@"
                        SELECT COD_ARTICULO, COD_LINEA, COD_CLASE, MEDIDA
                        FROM INV_ARTICULO
                        WHERE COD_LINEA IN ({string.Join(",", paramNames)})";

                        using var reader = await cmd.ExecuteReaderAsync();
                        while (await reader.ReadAsync())
                        {
                            var codArt = T(reader["COD_ARTICULO"]?.ToString());
                            if (string.IsNullOrWhiteSpace(codArt)) continue;

                            invRowsByLines.Add((
                                codArt,
                                T(reader["COD_LINEA"]?.ToString()),
                                T(reader["COD_CLASE"]?.ToString()),
                                T(reader["MEDIDA"]?.ToString())
                            ));
                        }
                    }
                }
            }
            finally
            {
                if (cerrarAlFinal && connection.State == System.Data.ConnectionState.Open)
                    await connection.CloseAsync();
            }


            // =========================
            // 4.2) (NUEVO) Validar que el artículo tenga DESCUENTO FIJO en XXORA_DISCOUNT_LIST
            //      (END_DATE IS NULL) para este cliente.
            //      Si NO existe => NO se genera ese artículo en PREDESCUENTOS_MASTER.
            // =========================
            var candidateItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // candidatos desde las 3 fuentes (artículo explícito, por clase, por línea)
            foreach (var k in invByArticulo.Keys)
                if (!string.IsNullOrWhiteSpace(k)) candidateItems.Add(T(k));

            foreach (var r in invRowsByPairs)
                if (!string.IsNullOrWhiteSpace(r.CodArticulo)) candidateItems.Add(T(r.CodArticulo));

            foreach (var r in invRowsByLines)
                if (!string.IsNullOrWhiteSpace(r.CodArticulo)) candidateItems.Add(T(r.CodArticulo));

            if (candidateItems.Count == 0)
                return;

            // =========================================================
            // REGLA GLOBAL: excluir ART_NO_PROMO ACTIVO
            // Aplica a FIJO y PROMOCIONAL y a artículos que llegaron
            // explícitos, por clase o por línea.
            // =========================================================
            var noPromoActivos = await ObtenerArticulosNoPromoActivosAsync(
                candidateItems,
                buNombre,
                organizationCode,
                HttpContext.RequestAborted);

            // =========================================================
            // 4.1) Validar ACCEPTADESCUENTO = 'S' en XXORA_ITEM_MASTER
            // =========================================================

            // OJO:
            // PREDESCUENTOS_MASTER usa CR_3,
            // pero XXORA_ITEM_MASTER está trabajando con LCR_3.
            const string organizationItemMaster = "LCR_3";

            var acceptedItems = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );

            foreach (var chunk in Chunk(candidateItems.ToList(), 900))
            {
                var itemsAceptados = await _OracleContext.XXORA_ITEM_MASTERs
                    .AsNoTracking()
                    .Where(x =>
                        x.BU_NAME != null &&
                        x.ORGANIZATION_CODE != null &&
                        x.ITEM_NUMBER != null &&
                        x.ACCEPTADESCUENTO != null &&

                        x.BU_NAME.Trim().ToUpper() ==
                            buNombre.Trim().ToUpper() &&

                        x.ORGANIZATION_CODE.Trim().ToUpper() ==
                            organizationItemMaster &&

                        chunk.Contains(x.ITEM_NUMBER.Trim()) &&

                        x.ACCEPTADESCUENTO.Trim().ToUpper() == "S"
                    )
                    .Select(x => x.ITEM_NUMBER)
                    .Distinct()
                    .ToListAsync();

                foreach (var item in itemsAceptados)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                        acceptedItems.Add(T(item));
                }
            }

            // Helper
            bool AceptaDescuento(string codArt)
            {
                return !string.IsNullOrWhiteSpace(codArt) &&
                       acceptedItems.Contains(T(codArt));
            }

            // =========================================================
            // El descuento CLIENTE previo solamente es obligatorio
            // cuando la solicitud es PROMOCIONAL.
            //
            // Para solicitudes FIJAS no debe exigirse que el descuento
            // ya exista, porque precisamente se está creando/actualizando.
            // =========================================================
            bool esPromocional = string.Equals(
                T(solicitud.TIPODESCUENTO),
                "Descuento Promocional",
                StringComparison.OrdinalIgnoreCase
            );

            var fixedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Consultar descuentos CLIENTE previos únicamente para promociones.
            if (esPromocional)
            {
                foreach (var chunk in Chunk(candidateItems.ToList(), 900))
                {
                    var rows = await _OracleContext.XXORA_DISCOUNT_LISTs
                        .AsNoTracking()
                        .Where(x =>
                            x.BU_NAME == buNombre &&
                            x.PARTY_NUMBER == codCliente &&
                            x.ITEM_NUMBER != null &&
                            x.RULE_DISCOUNT_NAME != null &&
                            chunk.Contains(x.ITEM_NUMBER) &&
                            x.RULE_DISCOUNT_NAME.Trim().ToUpper().Contains("CLIENT")
                        )
                        .Select(x => x.ITEM_NUMBER)
                        .Distinct()
                        .ToListAsync();

                    foreach (var item in rows)
                    {
                        if (!string.IsNullOrWhiteSpace(item))
                            fixedItems.Add(T(item));
                    }
                }
            }

            bool TieneFijoEnXxora(string codArt)
            {
                return !string.IsNullOrWhiteSpace(codArt) &&
                       fixedItems.Contains(T(codArt));
            }

            bool PuedeGenerarEnMaster(string codArt)
            {
                var item = T(codArt);

                if (string.IsNullOrWhiteSpace(item))
                    return false;

                // Aplica tanto a FIJO como a PROMOCIONAL.
                if (noPromoActivos.Contains(item))
                    return false;

                // Aplica tanto a FIJO como a PROMOCIONAL.
                if (!AceptaDescuento(item))
                    return false;

                // Solamente las promociones requieren descuento CLIENTE previo.
                if (esPromocional && !TieneFijoEnXxora(item))
                    return false;

                return true;
            }



            // =========================
            // 5) Construir inserts en memoria (prioridad: artículo > clase > línea)
            // =========================
            var nuevos = new List<PREDESCUENTOS_MASTER>(capacity: 4096);

            // 5.1 Artículo explícito
            foreach (var kv in artValor)
            {
                var codArt = kv.Key;
                var porcentaje = kv.Value;

                // ✅ NUEVO: si no tiene fijo en XXORA, no se genera
                if (!PuedeGenerarEnMaster(codArt))
                    continue;

                if (!invByArticulo.TryGetValue(codArt, out var art))
                    continue;

                var key = MakeKey(art.CodLinea, art.CodClase, codArt);
                if (!combosInsertados.Add(key))
                    continue;

                nuevos.Add(new PREDESCUENTOS_MASTER
                {
                    CONSECUTIVO = consecutivo,
                    ORGANIZATION_CODE = organizationCode,
                    BU_NOMBRE = buNombre,
                    COD_CLIENTE = codCliente,
                    COD_LINEA = art.CodLinea,
                    COD_CLASE = art.CodClase,
                    COD_ARTICULO = codArt,
                    MEDIDA = art.Medida,

                    // ✅ fechas solicitud
                    FECHA_INICIO = fechaInicioMaster,
                    FECHA_FIN = fechaFinMaster,

                    PORCENTAJE = porcentaje,
                    COD_USUARIO = usuario,
                    FECHA = fechaTexto,
                    LOCAL1 = "S",
                    REPLICA1 = "S"
                });
            }

            // 5.2 Por clase (sin artículo)
            foreach (var row in invRowsByPairs)
            {
                // ✅ NUEVO
                if (!PuedeGenerarEnMaster(row.CodArticulo))
                    continue;

                var pairKey = MakePairKey(row.CodLinea, row.CodClase);
                if (!pairValor.TryGetValue(pairKey, out var porcentaje))
                    continue;

                var key = MakeKey(row.CodLinea, row.CodClase, row.CodArticulo);
                if (!combosInsertados.Add(key))
                    continue;

                nuevos.Add(new PREDESCUENTOS_MASTER
                {
                    CONSECUTIVO = consecutivo,
                    ORGANIZATION_CODE = organizationCode,
                    BU_NOMBRE = buNombre,
                    COD_CLIENTE = codCliente,
                    COD_LINEA = row.CodLinea,
                    COD_CLASE = row.CodClase,
                    COD_ARTICULO = row.CodArticulo,
                    MEDIDA = row.Medida,

                    // ✅ fechas solicitud
                    FECHA_INICIO = fechaInicioMaster,
                    FECHA_FIN = fechaFinMaster,

                    PORCENTAJE = porcentaje,
                    COD_USUARIO = usuario,
                    FECHA = fechaTexto,
                    LOCAL1 = "S",
                    REPLICA1 = "S"
                });
            }

            // 5.3 Solo línea
            foreach (var row in invRowsByLines)
            {
                // ✅ NUEVO
                if (!PuedeGenerarEnMaster(row.CodArticulo))
                    continue;

                if (!lineValor.TryGetValue(T(row.CodLinea), out var porcentaje))
                    continue;

                var key = MakeKey(row.CodLinea, row.CodClase, row.CodArticulo);
                if (!combosInsertados.Add(key))
                    continue;

                nuevos.Add(new PREDESCUENTOS_MASTER
                {
                    CONSECUTIVO = consecutivo,
                    ORGANIZATION_CODE = organizationCode,
                    BU_NOMBRE = buNombre,
                    COD_CLIENTE = codCliente,
                    COD_LINEA = row.CodLinea,
                    COD_CLASE = row.CodClase,
                    COD_ARTICULO = row.CodArticulo,
                    MEDIDA = row.Medida,

                    // ✅ fechas solicitud
                    FECHA_INICIO = fechaInicioMaster,
                    FECHA_FIN = fechaFinMaster,

                    PORCENTAJE = porcentaje,
                    COD_USUARIO = usuario,
                    FECHA = fechaTexto,
                    LOCAL1 = "S",
                    REPLICA1 = "S"
                });
            }

            if (nuevos.Count == 0)
                return;

            // =========================
            // 6) Insert masivo (más rápido en EF)
            // =========================
            var prevAutoDetect = _OracleContext.ChangeTracker.AutoDetectChangesEnabled;
            _OracleContext.ChangeTracker.AutoDetectChangesEnabled = false;

            try
            {
                _OracleContext.PREDESCUENTOS_MASTERs.AddRange(nuevos);
                await _OracleContext.SaveChangesAsync();
            }
            finally
            {
                _OracleContext.ChangeTracker.AutoDetectChangesEnabled = prevAutoDetect;
            }
        }


        // Comparador case-insensitive para (BU, Party)
        private sealed class BuPartyComparer : IEqualityComparer<(string BU, string Party)>
        {
            public bool Equals((string BU, string Party) x, (string BU, string Party) y) =>
                string.Equals(x.BU, y.BU, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Party, y.Party, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode((string BU, string Party) obj)
            {
                unchecked
                {
                    var bu = obj.BU ?? "";
                    var party = obj.Party ?? "";

                    int h1 = StringComparer.OrdinalIgnoreCase.GetHashCode(bu);
                    int h2 = StringComparer.OrdinalIgnoreCase.GetHashCode(party);
                    return (h1 * 397) ^ h2;
                }
            }
        }

        // =========================
        // Comparer para GroupBy con tuple "custom"
        // =========================
        // (C# no puede inferir bien equality en algunos escenarios con tuplas anónimas + StringComparer)
        sealed class TupleComparer : IEqualityComparer<(string BU, string Cliente, string DiscountListId, string Item, string Uom)>
        {
            public bool Equals(
                    (string BU, string Cliente, string DiscountListId, string Item, string Uom) x,
                    (string BU, string Cliente, string DiscountListId, string Item, string Uom) y
            )
            {
                return string.Equals(x.BU, y.BU, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Cliente, y.Cliente, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.DiscountListId, y.DiscountListId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Item, y.Item, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Uom, y.Uom, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode((string BU, string Cliente, string DiscountListId, string Item, string Uom) obj)
            {
                return HashCode.Combine(
                        obj.BU?.ToLowerInvariant(),
                        obj.Cliente?.ToLowerInvariant(),
                        obj.DiscountListId?.ToLowerInvariant(),
                        obj.Item?.ToLowerInvariant(),
                        obj.Uom?.ToLowerInvariant()
                );
            }
        }

        private static byte[] ZipSingleFile(byte[] fileBytes, string entryFileName)
        {
            using var zipStream = new MemoryStream();
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = zip.CreateEntry(entryFileName, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(fileBytes, 0, fileBytes.Length);
            }

            zipStream.Position = 0;
            return zipStream.ToArray();
        }

        private static string FormatDate(DateTime? fecha) =>
            fecha.HasValue ? fecha.Value.ToString("yyyy/MM/dd HH:mm:ss") : string.Empty;

        private static string GetOperationCode(PREDESCUENTO pre)
        {
            if (string.Equals(pre.TIPODESCUENTO, "Descuento Promocional", StringComparison.OrdinalIgnoreCase))
                return "CREATE";
            if (string.Equals(pre.TIPODESCUENTO, "Descuento Fijo", StringComparison.OrdinalIgnoreCase))
                return "NO-OP";
            return string.Empty;
        }

        // Rango ORIGINAL por solicitud:
        // - Fijo/CLIENTE: inicio = FECHA_APLICACION (fallback FECHAINICIO/FECHASOLICITUD),
        //   fin = FECHASOLICITUD + 5 años.
        // - Promocional: inicio = FECHAINICIO (fallback FECHASOLICITUD), fin = FECHAFIN.
        private static (DateTime? Start, DateTime? End) GetRangoOriginal(PREDESCUENTO pre)
        {
            if (string.Equals(pre.TIPODESCUENTO, "Descuento Fijo", StringComparison.OrdinalIgnoreCase))
            {
                var start = pre.FECHA_APLICACION ?? pre.FECHAINICIO ?? pre.FECHASOLICITUD;
                var end = pre.FECHASOLICITUD.AddYears(5);
                return (start, end);
            }
            else
            {
                var start = pre.FECHAINICIO ?? pre.FECHASOLICITUD;
                var end = pre.FECHAFIN;
                return (start, end);
            }
        }

        private static string GetOperationCodePorFiltro(bool esFijo)
        {
            // Promocional => CREATE, Fijo/Activo => NO_OP
            return esFijo ? "NO-OP" : "CREATE";
        }

        private static (DateTime? Start, DateTime? End) GetRangoPorFiltro(PREDESCUENTO pre, bool esFijo)
        {
            if (esFijo)
            {
                var start = pre.FECHA_APLICACION ?? pre.FECHAINICIO ?? pre.FECHASOLICITUD;
                var end = pre.FECHASOLICITUD.AddYears(5);
                return (start, end);
            }
            else
            {
                var start = pre.FECHAINICIO ?? pre.FECHASOLICITUD;
                var end = pre.FECHAFIN;
                return (start, end);
            }
        }


        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrEmpty(id))
                return NotFound();

            // 1) Encabezado
            var encabezado = await _OracleContext.PREDESCUENTOs
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.CONSECUTIVO == id);

            if (encabezado == null)
                return NotFound();

            // Creamos una instancia "limpia" para la vista (opcional, igual que en Details)
            var predescuento = new SolicitudesDescuentos.ModelsOracle.PREDESCUENTO
            {
                BU_NOMBRE = encabezado.BU_NOMBRE,
                COD_CLIENTE = encabezado.COD_CLIENTE,
                CONSECUTIVO = encabezado.CONSECUTIVO,
                FECHASOLICITUD = encabezado.FECHASOLICITUD,
                TIPODESCUENTO = encabezado.TIPODESCUENTO,
                FECHAINICIO = encabezado.FECHAINICIO,
                FECHAFIN = encabezado.FECHAFIN,
                OBSERVACIONES = encabezado.OBSERVACIONES,
                INGRESADO_POR = encabezado.INGRESADO_POR,
                FECHAREGISTRO = encabezado.FECHAREGISTRO,
                ESTADO = encabezado.ESTADO,
                AUTORIZADO_POR = encabezado.AUTORIZADO_POR,
                FECHA_APLICACION = encabezado.FECHA_APLICACION
            };

            // 2) Nombre del cliente (GEN_CLIENTE)

            var cliente = await _OracleContext.GEN_CLIENTEs
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.IDCLIENTE == predescuento.COD_CLIENTE);

            ViewBag.NombreCliente = cliente?.NOMBRE_CLIENTE ?? string.Empty;

            // 3) Detalles PREDETDESCUENTO
            var detalles = await _OracleContext.PREDETDESCUENTOs
                .AsNoTracking()
                .Where(d =>
                    d.BU_NOMBRE == predescuento.BU_NOMBRE &&
                    d.COD_CLIENTE == predescuento.COD_CLIENTE &&
                    d.CONSECUTIVO == predescuento.CONSECUTIVO)
                .OrderBy(d => d.COD_LINEA)
                .ToListAsync();

            // 4) Diccionario de descripciones de línea (INV_LINEA), igual que en Details
            var codLineas = detalles
                .Select(d => d.COD_LINEA)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .ToList();

            var lineasDic = await _OracleContext.INV_LINEAs
                .AsNoTracking()
                .Where(l => codLineas.Contains(l.CATEGORY_CODE))
                .ToDictionaryAsync(
                    l => l.CATEGORY_CODE,
                    l => l.CATEGORY_NAME
                );

            ViewBag.DesLineas = lineasDic;

            // Asignar detalles al navigation property
            predescuento.PREDETDESCUENTOs = detalles;

            return View(predescuento);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            if (string.IsNullOrEmpty(id))
                return NotFound();

            // Encabezado
            var predescuento = await _OracleContext.PREDESCUENTOs
                .FirstOrDefaultAsync(p => p.CONSECUTIVO == id);

            if (predescuento == null)
                return NotFound();

            // Detalles hijos
            var detalles = await _OracleContext.PREDETDESCUENTOs
                .Where(d =>
                    d.BU_NOMBRE == predescuento.BU_NOMBRE &&
                    d.COD_CLIENTE == predescuento.COD_CLIENTE &&
                    d.CONSECUTIVO == predescuento.CONSECUTIVO)
                .ToListAsync();

            // Eliminar primero hijos, luego padre
            _OracleContext.PREDETDESCUENTOs.RemoveRange(detalles);
            _OracleContext.PREDESCUENTOs.Remove(predescuento);

            await _OracleContext.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}
