using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SolicitudesDescuentos.Data;
using SolicitudesDescuentos.ModelsTomaFis;

namespace SolicitudesDescuentos.Controllers
{
    public class TomaFisicaController : Controller
    {
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

        [HttpGet]
        public async Task<IActionResult> Consultar(
            string? codigoArticulo,
            string? codigoBarras)
        {
            var articuloBuscado = Normalizar(codigoArticulo);
            var barrasBuscado = (codigoBarras ?? "").Trim();

            if (string.IsNullOrWhiteSpace(articuloBuscado) &&
                string.IsNullOrWhiteSpace(barrasBuscado))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "Digite un código de artículo o un código de barras."
                });
            }

            string codigoArticuloReal;

            if (!string.IsNullOrWhiteSpace(barrasBuscado))
            {
                if (barrasBuscado.Length > 25)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "El código de barras no puede superar 25 caracteres."
                    });
                }

                var registroBarras = await _context.INV_ARTIC_CODBARs
                    .AsNoTracking()
                    .Where(x =>
                        x.COD_CIA == CodCia &&
                        x.COD_BARRAS == barrasBuscado)
                    .Select(x => new
                    {
                        x.COD_ARTICULO
                    })
                    .FirstOrDefaultAsync();

                if (registroBarras == null)
                {
                    return NotFound(new
                    {
                        ok = false,
                        mensaje = $"No existe el código de barras {barrasBuscado}."
                    });
                }

                codigoArticuloReal =
                    (registroBarras.COD_ARTICULO ?? "").Trim();
            }
            else
            {
                if (articuloBuscado.Length > 20)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "El código del artículo no puede superar 20 caracteres."
                    });
                }

                codigoArticuloReal = articuloBuscado;
            }

            var codigoArticuloNormalizado =
                Normalizar(codigoArticuloReal);

            var articulo = await _context.INV_ARTICULOs
                .AsNoTracking()
                .Where(x =>
                    x.COD_ARTICULO != null &&
                    x.COD_ARTICULO.Trim().ToUpper() ==
                    codigoArticuloNormalizado)
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
                    mensaje = $"No existe el artículo {codigoArticuloReal}."
                });
            }

            codigoArticuloReal =
                (articulo.COD_ARTICULO ?? "").Trim();

            var codigos = await _context.INV_ARTIC_CODBARs
                .AsNoTracking()
                .Where(x =>
                    x.COD_CIA == CodCia &&
                    x.COD_ARTICULO == codigoArticuloReal)
                .OrderBy(x => x.COD_BARRAS)
                .Select(x => new
                {
                    codigoBarras = x.COD_BARRAS
                })
                .ToListAsync();

            return Json(new
            {
                ok = true,
                codigoArticulo = codigoArticuloReal,
                descripcion = (articulo.DESCRIPCION ?? "").Trim(),
                codigosBarras = codigos.Select(x => new
                {
                    codigoBarras = (x.codigoBarras ?? "").Trim()
                })
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarCodigoBarras(
            string? codigoArticulo,
            string? codigoBarras)
        {
            var articuloKey = Normalizar(codigoArticulo);
            var barras = (codigoBarras ?? "").Trim();

            if (string.IsNullOrWhiteSpace(articuloKey) ||
                string.IsNullOrWhiteSpace(barras))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El artículo y el código de barras son obligatorios."
                });
            }

            var registro = await _context.INV_ARTIC_CODBARs
                .Where(x =>
                    x.COD_CIA == CodCia &&
                    x.COD_ARTICULO != null &&
                    x.COD_ARTICULO.Trim().ToUpper() == articuloKey &&
                    x.COD_BARRAS == barras)
                .FirstOrDefaultAsync();

            if (registro == null)
            {
                return NotFound(new
                {
                    ok = false,
                    mensaje = "El código de barras ya no existe para ese artículo."
                });
            }

            _context.INV_ARTIC_CODBARs.Remove(registro);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "No se pudo eliminar el código de barras.",
                    detalle = ex.GetBaseException().Message
                });
            }

            return Json(new
            {
                ok = true,
                mensaje = $"Se eliminó el código de barras {barras}."
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
                LOCAL1 = "S",
                REPLICA1 = "S",
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
