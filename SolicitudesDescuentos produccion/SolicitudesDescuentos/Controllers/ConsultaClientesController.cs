using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SolicitudesDescuentos.Data;
using SolicitudesDescuentos.ModelsOracle.ViewModels;

namespace SolicitudesDescuentos.Controllers
{
    /// <summary>
    /// Consulta de SOLO LECTURA sobre BG_INTUSER.PREDESCLASEORACLE.
    /// No crea, actualiza ni elimina descuentos.
    /// </summary>
    [Authorize]
    public sealed class ConsultaClientesController : Controller
    {
        private const string Bu = "LANCO_CR";
        private const string Organizacion = "LCR_3";
        private const int TamanoPagina = 200;

        private readonly OracleContext _db;
        private readonly ILogger<ConsultaClientesController> _logger;

        public ConsultaClientesController(OracleContext db, ILogger<ConsultaClientesController> logger)
        {
            _db = db;
            _logger = logger;
        }

        private static string Normalizar(string? s, int max = 300)
        {
            var valor = (s ?? "").Trim();
            return valor.Length <= max ? valor : valor.Substring(0, max);
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string? idCliente,
            string? tipo,
            string? linea,
            string? clase,
            string? articulo,
            int pagina = 1)
        {
            var vm = new ConsultaClientesDescuentosVm
            {
                Cliente = Normalizar(idCliente, 30),
                Tipo = Normalizar(tipo, 20).ToUpperInvariant(),
                Linea = Normalizar(linea, 820),
                Clase = Normalizar(clase, 820),
                Articulo = Normalizar(articulo, 300),
                Pagina = Math.Max(1, pagina),
                TamanoPagina = TamanoPagina
            };

            if (vm.Tipo != "CLIENTE" && vm.Tipo != "PROMOCION")
                vm.Tipo = "";

            // Evita consultas globales enormes si no se eligió cliente.
            if (string.IsNullOrWhiteSpace(vm.Cliente))
                return View(vm);

            vm.Consultado = true;
            var ct = HttpContext.RequestAborted;

            try
            {
                var nombreCliente = await _db.GEN_CLIENTEs.AsNoTracking()
                    .Where(c => c.IDCLIENTE == vm.Cliente)
                    .Select(c => c.NOMBRE_CLIENTE)
                    .FirstOrDefaultAsync(ct);
                vm.NombreCliente = (nombreCliente ?? "").Trim();

                // Si se filtra un artículo, incluimos también la línea y clase
                // que lo cubren: un artículo sin registro individual puede
                // tener descuento por alguno de esos niveles.
                string lineaArticulo = "";
                string claseArticulo = "";
                if (!string.IsNullOrWhiteSpace(vm.Articulo))
                {
                    var art = await _db.XXORA_ITEM_MASTERs.AsNoTracking()
                        .Where(x => x.BU_NAME == Bu &&
                                    x.ORGANIZATION_CODE == Organizacion &&
                                    x.ITEM_NUMBER == vm.Articulo)
                        .Select(x => new { x.CATEGORY_CODE, x.SUBCATEGORY_CODE })
                        .FirstOrDefaultAsync(ct);

                    if (art != null)
                    {
                        lineaArticulo = (art.CATEGORY_CODE ?? "").Trim();
                        claseArticulo = (art.SUBCATEGORY_CODE ?? "").Trim();
                    }
                }

                var query = _db.PREDESCLASEORACLEs.AsNoTracking()
                    .Where(d => d.ORGANIZATION_CODE == Organizacion && d.IDCLIENTE == vm.Cliente);

                if (!string.IsNullOrEmpty(vm.Tipo))
                    query = query.Where(d => d.RULE_DISCOUNT_NAME == vm.Tipo);

                if (!string.IsNullOrEmpty(vm.Linea))
                    query = query.Where(d => d.CATEGORY_CODE == vm.Linea);

                if (!string.IsNullOrEmpty(vm.Articulo))
                {
                    // Los filtros deben ser coherentes con la clasificación
                    // del artículo. Evita devolver una clase equivocada.
                    if ((!string.IsNullOrEmpty(vm.Clase) && vm.Clase != claseArticulo) ||
                        (!string.IsNullOrEmpty(vm.Linea) && vm.Linea != lineaArticulo &&
                         !string.IsNullOrEmpty(lineaArticulo)))
                    {
                        query = query.Where(d => false);
                    }

                    var item = vm.Articulo;
                    var lin = lineaArticulo;
                    var cls = claseArticulo;
                    vm.IncluyeCobertura = !string.IsNullOrEmpty(lin);

                    // No buscar solamente ITEM_NUMBER: los descuentos resumen
                    // se aplican aunque no exista una fila individual.
                    query = query.Where(d =>
                        d.ITEM_NUMBER == item ||
                        (d.ITEM_NUMBER == null && d.SUBCATEGORY_CODE == cls &&
                         d.CATEGORY_CODE == lin && cls != "" && lin != "") ||
                        (d.ITEM_NUMBER == null && d.SUBCATEGORY_CODE == null &&
                         d.CATEGORY_CODE == lin && lin != ""));
                }
                else if (!string.IsNullOrEmpty(vm.Clase))
                {
                    var cls = vm.Clase;
                    var lin = vm.Linea;
                    // Incluye línea superior para explicar la cobertura de clase.
                    query = query.Where(d => d.SUBCATEGORY_CODE == cls ||
                        (lin != "" && d.ITEM_NUMBER == null && d.SUBCATEGORY_CODE == null &&
                         d.CATEGORY_CODE == lin));
                    vm.IncluyeCobertura = !string.IsNullOrEmpty(vm.Linea);
                }

                vm.Total = await query.CountAsync(ct);
                vm.Pagina = Math.Min(vm.Pagina, vm.TotalPaginas);

                var registros = await query
                    .OrderBy(d => d.RULE_DISCOUNT_NAME)
                    .ThenBy(d => d.CATEGORY_CODE)
                    .ThenBy(d => d.SUBCATEGORY_CODE == null ? 0 : 1)
                    .ThenBy(d => d.SUBCATEGORY_CODE)
                    .ThenBy(d => d.ITEM_NUMBER == null ? 0 : 1)
                    .ThenBy(d => d.ITEM_NUMBER)
                    .Skip((vm.Pagina - 1) * TamanoPagina)
                    .Take(TamanoPagina)
                    .Select(d => new ConsultaDescuentoFilaVm
                    {
                        Tipo = d.RULE_DISCOUNT_NAME ?? "",
                        Categoria = d.CATEGORY_CODE ?? "",
                        Subcategoria = d.SUBCATEGORY_CODE ?? "",
                        Articulo = d.ITEM_NUMBER ?? "",
                        Porcentaje = d.PORCENTAJE,
                        Inicio = d.FECHA_INICIO,
                        Fin = d.FECHA_FIN
                    })
                    .ToListAsync(ct);

                // Buscar descripciones sin generar N+1 consultas.
                var lineas = registros.Select(x => x.Categoria)
                    .Where(x => x.Length > 0).Distinct().ToList();
                var clases = registros.Select(x => x.Subcategoria)
                    .Where(x => x.Length > 0).Distinct().ToList();
                var articulos = registros.Select(x => x.Articulo)
                    .Where(x => x.Length > 0).Distinct().ToList();

                var nombresLineas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var nombresClases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var nombresArticulos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (lineas.Count > 0)
                {
                    var rows = await _db.INV_LINEAs.AsNoTracking()
                        .Where(x => lineas.Contains(x.CATEGORY_CODE))
                        .Select(x => new { x.CATEGORY_CODE, x.CATEGORY_NAME })
                        .ToListAsync(ct);
                    foreach (var row in rows)
                        nombresLineas.TryAdd((row.CATEGORY_CODE ?? "").Trim(),
                            (row.CATEGORY_NAME ?? "").Trim());
                }

                if (clases.Count > 0 && lineas.Count > 0)
                {
                    var rows = await _db.INV_CLASEs.AsNoTracking()
                        .Where(x => lineas.Contains(x.CATEGORY_CODE) &&
                                    clases.Contains(x.SUBCATEGORY_CODE))
                        .Select(x => new { x.CATEGORY_CODE, x.SUBCATEGORY_CODE, x.SUBCATEGORY_NAME })
                        .ToListAsync(ct);
                    foreach (var row in rows)
                    {
                        var clave = $"{(row.CATEGORY_CODE ?? "").Trim()}|{(row.SUBCATEGORY_CODE ?? "").Trim()}";
                        nombresClases.TryAdd(clave, (row.SUBCATEGORY_NAME ?? "").Trim());
                    }
                }

                if (articulos.Count > 0)
                {
                    var rows = await _db.XXORA_ITEM_MASTERs.AsNoTracking()
                        .Where(x => x.BU_NAME == Bu && x.ORGANIZATION_CODE == Organizacion &&
                                    articulos.Contains(x.ITEM_NUMBER))
                        .Select(x => new { x.ITEM_NUMBER, x.DESCRIPTION })
                        .ToListAsync(ct);
                    foreach (var row in rows)
                        nombresArticulos.TryAdd((row.ITEM_NUMBER ?? "").Trim(),
                            (row.DESCRIPTION ?? "").Trim());
                }

                foreach (var row in registros)
                {
                    row.Categoria = row.Categoria.Trim();
                    row.Subcategoria = row.Subcategoria.Trim();
                    row.Articulo = row.Articulo.Trim();
                    row.Tipo = row.Tipo.Trim();
                    nombresLineas.TryGetValue(row.Categoria, out var nomLinea);
                    nombresClases.TryGetValue($"{row.Categoria}|{row.Subcategoria}", out var nomClase);
                    nombresArticulos.TryGetValue(row.Articulo, out var nomArticulo);
                    row.NombreLinea = nomLinea ?? "";
                    row.NombreClase = nomClase ?? "";
                    row.NombreArticulo = nomArticulo ?? "";
                }

                vm.Descuentos = registros;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error consultando PREDESCLASEORACLE, cliente={Cliente}", vm.Cliente);
                vm.Advertencia = "No se pudo consultar los descuentos. Revisá la conexión de Oracle y la columna RULE_DISCOUNT_NAME.";
            }

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> BuscarClientes(string? q)
        {
            q = Normalizar(q, 100);
            if (q.Length < 2)
                return Json(Array.Empty<object>());

            var termino = q.ToUpperInvariant();
            var clientes = await _db.GEN_CLIENTEs.AsNoTracking()
                .Where(x => x.IDCLIENTE.ToUpper().Contains(termino) ||
                            x.NOMBRE_CLIENTE.ToUpper().Contains(termino))
                .OrderBy(x => x.NOMBRE_CLIENTE)
                .Take(40)
                .Select(x => new { codigo = x.IDCLIENTE, nombre = x.NOMBRE_CLIENTE })
                .ToListAsync(HttpContext.RequestAborted);
            return Json(clientes);
        }

        [HttpGet]
        public async Task<IActionResult> BuscarArticulos(string? q, string? linea, string? clase)
        {
            q = Normalizar(q, 100);
            linea = Normalizar(linea, 820);
            clase = Normalizar(clase, 820);
            if (q.Length < 2)
                return Json(Array.Empty<object>());

            var termino = q.ToUpperInvariant();
            var query = _db.XXORA_ITEM_MASTERs.AsNoTracking()
                .Where(x => x.BU_NAME == Bu &&
                            x.ORGANIZATION_CODE == Organizacion &&
                            x.ACCEPTADESCUENTO == "S" &&
                            (x.ITEM_NUMBER.ToUpper().Contains(termino) ||
                             x.DESCRIPTION.ToUpper().Contains(termino)));

            if (!string.IsNullOrWhiteSpace(linea))
                query = query.Where(x => x.CATEGORY_CODE == linea);
            if (!string.IsNullOrWhiteSpace(clase))
                query = query.Where(x => x.SUBCATEGORY_CODE == clase);

            var articulos = await query.OrderBy(x => x.ITEM_NUMBER)
                .Take(60)
                .Select(x => new { codigo = x.ITEM_NUMBER, descripcion = x.DESCRIPTION })
                .ToListAsync(HttpContext.RequestAborted);
            return Json(articulos);
        }
    }
}
