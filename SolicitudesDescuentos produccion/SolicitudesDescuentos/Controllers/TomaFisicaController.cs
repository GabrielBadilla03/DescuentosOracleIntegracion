using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SolicitudesDescuentos.Data;
using SolicitudesDescuentos.ModelsTomaFis;

namespace SolicitudesDescuentos.Controllers
{
    public class TomaFisicaController : Controller
    {
        /*
         * Compañía utilizada al insertar en TOMAFIS.INV_ARTIC_CODBAR.
         * Si la base TOMAFIS utiliza otro código de compañía, este es
         * el único valor que debe cambiarse.
         */
        private const string CodCia = "001";

        private readonly TomaFisDbContext _context;

        public TomaFisicaController(TomaFisDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> ValidarArticulo(string? codigo)
        {
            var codigoNormalizado = Normalizar(codigo);

            if (string.IsNullOrWhiteSpace(codigoNormalizado))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "Digite el código del artículo."
                });
            }

            if (codigoNormalizado.Length > 20)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El código del artículo no puede superar 20 caracteres."
                });
            }

            var articulo = await _context.INV_ARTICULOs
                .AsNoTracking()
                .Where(x =>
                    x.COD_ARTICULO != null &&
                    x.COD_ARTICULO.Trim().ToUpper() == codigoNormalizado)
                .Select(x => new
                {
                    x.COD_ARTICULO,
                    x.DESCRIPCION
                })
                .FirstOrDefaultAsync();

            if (articulo == null)
            {
                return NotFound(new
                {
                    ok = false,
                    mensaje = $"No existe el artículo {codigoNormalizado}."
                });
            }

            return Json(new
            {
                ok = true,
                codigo = (articulo.COD_ARTICULO ?? "").Trim(),
                descripcion = (articulo.DESCRIPCION ?? "").Trim()
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarCodigoBarras(
            string? codigoArticulo,
            string? codigoBarras)
        {
            var articuloKey = Normalizar(codigoArticulo);
            var barras = (codigoBarras ?? "").Trim();

            if (string.IsNullOrWhiteSpace(articuloKey))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El código del artículo es obligatorio."
                });
            }

            if (string.IsNullOrWhiteSpace(barras))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El código de barras es obligatorio."
                });
            }

            if (articuloKey.Length > 20)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El código del artículo no puede superar 20 caracteres."
                });
            }

            if (barras.Length > 25)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El código de barras no puede superar 25 caracteres."
                });
            }

            /*
             * Se vuelve a validar el artículo del lado del servidor.
             * No se confía únicamente en la validación realizada por JavaScript.
             */
            var articulo = await _context.INV_ARTICULOs
                .AsNoTracking()
                .Where(x =>
                    x.COD_ARTICULO != null &&
                    x.COD_ARTICULO.Trim().ToUpper() == articuloKey)
                .Select(x => new
                {
                    x.COD_ARTICULO,
                    x.DESCRIPCION
                })
                .FirstOrDefaultAsync();

            if (articulo == null)
            {
                return NotFound(new
                {
                    ok = false,
                    mensaje = $"El artículo {articuloKey} ya no existe."
                });
            }

            var codigoArticuloReal =
                (articulo.COD_ARTICULO ?? "").Trim();

            /*
             * INV_ARTIC_CODBAR_IDX1 es único por COD_CIA + COD_BARRAS.
             * Se valida antes del INSERT para devolver un mensaje entendible.
             */
            var existente = await _context.INV_ARTIC_CODBARs
                .AsNoTracking()
                .Where(x =>
                    x.COD_CIA == CodCia &&
                    x.COD_BARRAS == barras)
                .Select(x => new
                {
                    x.COD_ARTICULO,
                    x.COD_BARRAS
                })
                .FirstOrDefaultAsync();

            if (existente != null)
            {
                return Conflict(new
                {
                    ok = false,
                    mensaje =
                        $"El código de barras {barras} ya está registrado " +
                        $"para el artículo {(existente.COD_ARTICULO ?? "").Trim()}."
                });
            }

            var nuevo = new INV_ARTIC_CODBAR
            {
                COD_CIA = CodCia,
                COD_ARTICULO = codigoArticuloReal,
                COD_BARRAS = barras,

                // Valores estándar usados por la tabla.
                LOCAL1 = "S",
                REPLICA1 = "S",

                // Son campos opcionales en el modelo actual.
                TITULAR = null,
                CLASE = null
            };

            _context.INV_ARTIC_CODBARs.Add(nuevo);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                /*
                 * También se captura la restricción única por si dos equipos
                 * intentan guardar el mismo código de barras al mismo tiempo.
                 */
                return Conflict(new
                {
                    ok = false,
                    mensaje =
                        "No se pudo guardar el código de barras. " +
                        "Puede que ya haya sido registrado por otro usuario.",
                    detalle = ex.GetBaseException().Message
                });
            }

            return Json(new
            {
                ok = true,
                codigoArticulo = codigoArticuloReal,
                descripcion = (articulo.DESCRIPCION ?? "").Trim(),
                codigoBarras = barras,
                mensaje = "Código de barras guardado."
            });
        }

        private static string Normalizar(string? valor)
        {
            return (valor ?? "")
                .Trim()
                .ToUpperInvariant();
        }
    }
}
