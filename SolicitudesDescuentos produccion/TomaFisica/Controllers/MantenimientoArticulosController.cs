using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TomaFisica.Data;

namespace TomaFisica.Controllers;

[Authorize]
public class MantenimientoArticulosController : Controller
{
    private const int TamanoPagina = 30;
    private readonly TomaFisDbContext _context;
    private readonly ILogger<MantenimientoArticulosController> _logger;

    public MantenimientoArticulosController(
        TomaFisDbContext context, ILogger<MantenimientoArticulosController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> Buscar(string? buscar, int pagina = 1, CancellationToken cancellationToken = default)
    {
        var texto = (buscar ?? "").Trim();
        if (texto.Length > 80)
            return BadRequest(new { ok = false, mensaje = "La búsqueda no puede superar 80 caracteres." });

        pagina = Math.Clamp(pagina, 1, 1000);
        var query = _context.INV_ARTICULOs.AsNoTracking().AsQueryable();
        if (texto.Length > 0)
        {
            var clave = texto.ToUpperInvariant();
            query = query.Where(x =>
                (x.COD_ARTICULO != null && x.COD_ARTICULO.ToUpper().Contains(clave)) ||
                (x.DESCRIPCION != null && x.DESCRIPCION.ToUpper().Contains(clave)));
        }

        var filas = await query.OrderBy(x => x.COD_ARTICULO)
            .Skip((pagina - 1) * TamanoPagina)
            .Take(TamanoPagina + 1)
            .Select(x => new
            {
                codigoArticulo = x.COD_ARTICULO,
                descripcion = x.DESCRIPCION,
                medida = x.MEDIDA,
                tipoInventario = x.TIPOINVENTARIO,
                requiereBatch = x.REQUIEREBATCH
            }).ToListAsync(cancellationToken);

        return Json(new
        {
            ok = true,
            pagina,
            hayMas = filas.Count > TamanoPagina,
            articulos = filas.Take(TamanoPagina)
        });
    }

    [HttpGet]
    public async Task<IActionResult> Detalle(string? codigoArticulo, CancellationToken cancellationToken)
    {
        var codigo = Normalizar(codigoArticulo);
        if (codigo.Length == 0 || codigo.Length > 20)
            return BadRequest(new { ok = false, mensaje = "Código de artículo inválido." });

        var articulo = await _context.INV_ARTICULOs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.COD_ARTICULO != null &&
                x.COD_ARTICULO.Trim().ToUpper() == codigo, cancellationToken);
        if (articulo == null)
            return NotFound(new { ok = false, mensaje = "El artículo no existe." });

        return Json(new
        {
            ok = true,
            articulo = new
            {
                codigoArticulo = articulo.COD_ARTICULO.Trim(),
                descripcion = articulo.DESCRIPCION?.Trim(),
                medida = articulo.MEDIDA?.Trim(),
                multiploCaja = articulo.MULTIPLOCAJA,
                tipoInventario = articulo.TIPOINVENTARIO?.Trim(),
                categoria = articulo.CATEGORIA?.Trim(),
                subcategoria = articulo.SUBCATEGORIA?.Trim(),
                unidadNegocio = articulo.UNIDADNEGOCIO,
                origen = articulo.ORIGEN,
                requiereBatch = articulo.REQUIEREBATCH?.Trim()
            }
        });
    }

    [HttpGet]
    public async Task<IActionResult> Catalogos(CancellationToken cancellationToken)
    {
        var medidas = await _context.INV_MEDIDAs.AsNoTracking()
            .Select(x => new { codigo = x.MEDIDA, descripcion = x.DES_MEDIDA })
            .ToListAsync(cancellationToken);
        var tipos = await _context.INV_TIPOSINVs.AsNoTracking()
            .Select(x => x.TIPOINVENTARIO)
            .ToListAsync(cancellationToken);
        var origenes = await _context.INV_ORIGENs.AsNoTracking()
            .Where(x => x.ORIGEN.HasValue)
            .Select(x => new { codigo = x.ORIGEN, descripcion = x.DESCRIPCION })
            .ToListAsync(cancellationToken);

        return Json(new
        {
            ok = true,
            medidas = medidas.Where(x => !string.IsNullOrWhiteSpace(x.codigo))
                .GroupBy(x => x.codigo.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(x => new { codigo = x.Key, descripcion = x.First().descripcion?.Trim() ?? "" })
                .OrderBy(x => x.codigo).ToList(),
            tipos = tipos.Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x).ToList(),
            origenes = origenes.GroupBy(x => x.codigo)
                .Select(x => new { codigo = x.Key, descripcion = x.First().descripcion?.Trim() ?? "" })
                .OrderBy(x => x.codigo).ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Guardar(
        [FromForm] EditarArticuloRequest datos, CancellationToken cancellationToken)
    {
        var codigo = Normalizar(datos.CodigoArticulo);
        var descripcion = (datos.Descripcion ?? "").Trim();
        var medida = Normalizar(datos.Medida);
        var tipo = Normalizar(datos.TipoInventario);
        var categoria = (datos.Categoria ?? "").Trim();
        var subcategoria = (datos.Subcategoria ?? "").Trim();
        var requiereBatch = Normalizar(datos.RequiereBatch);
        var unidadNegocio = Normalizar(datos.UnidadNegocio);
        var origenTexto = (datos.Origen ?? "").Trim();
        var multiploTexto = (datos.MultiploCaja ?? "").Trim().Replace(',', '.');

        if (codigo.Length == 0 || codigo.Length > 20)
            return BadRequest(new { ok = false, mensaje = "Código de artículo inválido." });
        if (descripcion.Length == 0 || descripcion.Length > 100)
            return BadRequest(new { ok = false, mensaje = "La descripción es obligatoria (máximo 100 caracteres)." });
        if (medida.Length == 0 || medida.Length > 10)
            return BadRequest(new { ok = false, mensaje = "La medida es obligatoria (máximo 10 caracteres)." });
        if (tipo.Length == 0 || tipo.Length > 30)
            return BadRequest(new { ok = false, mensaje = "El tipo de inventario es obligatorio (máximo 30 caracteres)." });
        if (categoria.Length == 0 || categoria.Length > 50 ||
            subcategoria.Length == 0 || subcategoria.Length > 50)
            return BadRequest(new { ok = false, mensaje = "Categoría y subcategoría son obligatorias (máximo 50 caracteres cada una)." });
        if (requiereBatch is not ("S" or "N"))
            return BadRequest(new { ok = false, mensaje = "Requiere batch debe ser S o N." });
        if (unidadNegocio is not ("" or "S" or "N"))
            return BadRequest(new { ok = false, mensaje = "Unidad de negocio no es válida." });
        if (!decimal.TryParse(multiploTexto,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var multiploCaja) ||
            multiploCaja < 0 || multiploCaja > 999999.99m ||
            decimal.Round(multiploCaja, 2) != multiploCaja)
            return BadRequest(new { ok = false, mensaje = "Múltiplo de caja inválido (0 a 999999.99; máximo dos decimales)." });

        byte? origen = null;
        if (origenTexto.Length > 0)
        {
            if (!byte.TryParse(origenTexto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var codigoOrigen))
                return BadRequest(new { ok = false, mensaje = "Origen inválido." });
            origen = codigoOrigen;
            if (!await _context.INV_ORIGENs.AsNoTracking().AnyAsync(x =>
                    x.ORIGEN.HasValue && x.ORIGEN.Value == origen.Value, cancellationToken))
                return BadRequest(new { ok = false, mensaje = "El origen no existe en INV_ORIGEN." });
        }

        if (!await _context.INV_MEDIDAs.AsNoTracking().AnyAsync(x =>
                x.MEDIDA != null && x.MEDIDA.Trim().ToUpper() == medida, cancellationToken))
            return BadRequest(new { ok = false, mensaje = "La medida no existe en INV_MEDIDA." });
        if (!await _context.INV_TIPOSINVs.AsNoTracking().AnyAsync(x =>
                x.TIPOINVENTARIO != null && x.TIPOINVENTARIO.Trim().ToUpper() == tipo, cancellationToken))
            return BadRequest(new { ok = false, mensaje = "El tipo de inventario no existe en INV_TIPOSINV." });

        var articulo = await _context.INV_ARTICULOs.FirstOrDefaultAsync(
            x => x.COD_ARTICULO != null && x.COD_ARTICULO.Trim().ToUpper() == codigo,
            cancellationToken);
        if (articulo == null)
            return NotFound(new { ok = false, mensaje = "El artículo ya no existe." });

        // Solo los campos permitidos. No se cambia la llave COD_ARTICULO
        // ni los conteos, códigos de barras u otros registros relacionados.
        articulo.DESCRIPCION = descripcion;
        articulo.MEDIDA = medida;
        articulo.MULTIPLOCAJA = multiploCaja;
        articulo.TIPOINVENTARIO = tipo;
        articulo.CATEGORIA = categoria;
        articulo.SUBCATEGORIA = subcategoria;
        articulo.UNIDADNEGOCIO = unidadNegocio == "" ? null : unidadNegocio == "S";
        articulo.ORIGEN = origen;
        articulo.REQUIEREBATCH = requiereBatch;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return Json(new { ok = true, mensaje = "Artículo actualizado correctamente.", codigoArticulo = articulo.COD_ARTICULO.Trim() });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "No se pudo actualizar el artículo {CodigoArticulo}", codigo);
            return StatusCode(500, new { ok = false, mensaje = "No fue posible guardar el artículo en la base de datos." });
        }
    }

    private static string Normalizar(string? value) => (value ?? "").Trim().ToUpperInvariant();

    public sealed class EditarArticuloRequest
    {
        public string? CodigoArticulo { get; set; }
        public string? Descripcion { get; set; }
        public string? Medida { get; set; }
        public string? MultiploCaja { get; set; }
        public string? TipoInventario { get; set; }
        public string? Categoria { get; set; }
        public string? Subcategoria { get; set; }
        public string? UnidadNegocio { get; set; }
        public string? Origen { get; set; }
        public string? RequiereBatch { get; set; }
    }
}
