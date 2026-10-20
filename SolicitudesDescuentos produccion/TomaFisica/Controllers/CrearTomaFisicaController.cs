using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TomaFisica.Data;
using TomaFisica.ModelsTomaFis;

namespace TomaFisica.Controllers;

[Authorize]
public class CrearTomaFisicaController : Controller
{
    private const string CodCiaFijo = "001";
    private const string EstadoNuevaToma = "L";

    private readonly TomaFisDbContext _context;
    private readonly IConfiguration _configuration;

    public CrearTomaFisicaController(
        TomaFisDbContext context,
        IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> DatosIniciales(
        CancellationToken cancellationToken)
    {
        var codSucursal = ObtenerCodSucursal();

        if (string.IsNullOrWhiteSpace(codSucursal))
        {
            return BadRequest(new
            {
                ok = false,
                mensaje =
                    "No se encuentra configurada la sucursal del usuario."
            });
        }

        var bodegas = await _context.INV_BODEGAs
            .AsNoTracking()
            .Where(x =>
                x.COD_CIA != null &&
                x.COD_BODEGA != null &&
                x.SUCURSAL != null &&
                x.COD_CIA.Trim() == CodCiaFijo &&
                x.SUCURSAL.Trim() == codSucursal)
            .OrderBy(x => x.COD_BODEGA)
            .Select(x => new
            {
                codigo = x.COD_BODEGA.Trim(),
                descripcion = x.DES_BODEGA == null
                    ? ""
                    : x.DES_BODEGA.Trim()
            })
            .ToListAsync(cancellationToken);

        var siguienteSecuencia =
            await ObtenerSiguienteSecuenciaAsync(
                cancellationToken);

        return Json(new
        {
            ok = true,
            codCia = CodCiaFijo,
            codSucursal,
            usuario = ObtenerUsuario(),
            estado = EstadoNuevaToma,
            estadoDescripcion = "Listo para toma",
            siguienteSecuencia,
            bodegas
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(
        string? codBodega,
        string? tipoInventario,
        string? tipo,
        string? observacion,
        CancellationToken cancellationToken)
    {
        var codSucursal = ObtenerCodSucursal();
        var bodegaKey = Normalizar(codBodega);
        var tipoInventarioKey = Normalizar(tipoInventario);
        var tipoKey = Normalizar(tipo);
        var observacionLimpia = (observacion ?? "").Trim();

        if (string.IsNullOrWhiteSpace(codSucursal))
        {
            return BadRequest(new
            {
                ok = false,
                mensaje =
                    "No se encuentra configurada la sucursal del usuario."
            });
        }

        if (string.IsNullOrWhiteSpace(bodegaKey))
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = "Seleccione una bodega."
            });
        }

        if (string.IsNullOrWhiteSpace(tipoInventarioKey))
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = "Digite el tipo de inventario."
            });
        }

        if (tipoInventarioKey.Length > 3)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje =
                    "El tipo de inventario no puede superar 3 caracteres."
            });
        }

        if (tipoKey is not ("1" or "2" or "3"))
        {
            return BadRequest(new
            {
                ok = false,
                mensaje =
                    "El tipo de toma debe ser 1, 2 o 3."
            });
        }

        if (observacionLimpia.Length > 100)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje =
                    "La observación no puede superar 100 caracteres."
            });
        }

        var bodegaExiste = await _context.INV_BODEGAs
            .AsNoTracking()
            .AnyAsync(x =>
                x.COD_CIA != null &&
                x.COD_BODEGA != null &&
                x.SUCURSAL != null &&
                x.COD_CIA.Trim() == CodCiaFijo &&
                x.COD_BODEGA.Trim() == bodegaKey &&
                x.SUCURSAL.Trim() == codSucursal,
                cancellationToken);

        if (!bodegaExiste)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje =
                    $"La bodega {bodegaKey} no pertenece a la compañía {CodCiaFijo} y sucursal {codSucursal}."
            });
        }

        /*
         * La secuencia se calcula al guardar, no se confía en el valor
         * que se muestra en pantalla.
         *
         * Serializable reduce el riesgo de que dos usuarios obtengan
         * simultáneamente el mismo MAX(SECUENCIA) + 1.
         */
        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var nuevaSecuencia =
                await ObtenerSiguienteSecuenciaAsync(
                    cancellationToken);

            var usuario = ObtenerUsuario();

            if (usuario.Length > 30)
                usuario = usuario[..30];

            var nuevaToma = new INV_TOMAFI
            {
                SECUENCIA = nuevaSecuencia,

                // Requisito de esta pantalla:
                // siempre se trabaja con compañía 001.
                COD_CIA = CodCiaFijo,

                COD_BODEGA = bodegaKey,

                OBSERVACION =
                    string.IsNullOrWhiteSpace(observacionLimpia)
                        ? null
                        : observacionLimpia,

                FEC_MOVTO = DateTime.Now,
                FEC_APLICA = null,

                // Estados de INV_TOMAFIS:
                // P = Pendiente
                // L = Listo para toma
                // A = Aplicada
                //
                // Una nueva toma creada desde esta pantalla
                // queda inmediatamente lista para conteo.
                ESTADO = EstadoNuevaToma,

                // 1 = Conteo 1
                // 2 = Conteo 2
                // 3 = Conteo de Verificación
                TIPO = tipoKey,

                USUARIO = usuario,

                // Estos campos no se solicitan al usuario.
                LOCAL1 = "S",
                REPLICA1 = "S"
            };

            _context.INV_TOMAFIs.Add(nuevaToma);

            await _context.SaveChangesAsync(
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);

            return Json(new
            {
                ok = true,
                mensaje =
                    $"Toma física {nuevaSecuencia} creada correctamente.",
                secuencia = nuevaSecuencia,
                codCia = CodCiaFijo,
                codBodega = bodegaKey,
                tipoInventario = tipoInventarioKey,
                tipo = tipoKey,
                estado = EstadoNuevaToma,
                estadoDescripcion = "Listo para toma",
                usuario,
                fechaMovimiento =
                    nuevaToma.FEC_MOVTO
                        .ToString("dd/MM/yyyy HH:mm"),
                siguienteSecuencia =
                    nuevaSecuencia + 1
            });
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            _context.ChangeTracker.Clear();

            return StatusCode(500, new
            {
                ok = false,
                mensaje =
                    "No fue posible crear la toma física.",
                detalle =
                    ex.GetBaseException().Message
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            _context.ChangeTracker.Clear();

            return StatusCode(500, new
            {
                ok = false,
                mensaje =
                    "Ocurrió un error al crear la toma física.",
                detalle = ex.Message
            });
        }
    }

    private async Task<int> ObtenerSiguienteSecuenciaAsync(
        CancellationToken cancellationToken)
    {
        var ultimaSecuencia =
            await _context.INV_TOMAFIs
                .AsNoTracking()
                .MaxAsync(
                    x => (int?)x.SECUENCIA,
                    cancellationToken)
            ?? 0;

        return checked(ultimaSecuencia + 1);
    }

    private string ObtenerUsuario()
    {
        var usuario =
            User.FindFirst("Username")
                ?.Value
                ?.Trim();

        if (string.IsNullOrWhiteSpace(usuario))
        {
            usuario =
                User.Identity
                    ?.Name
                    ?.Trim();
        }

        return string.IsNullOrWhiteSpace(usuario)
            ? "WEB"
            : usuario.ToUpperInvariant();
    }

    private string ObtenerCodSucursal()
    {
        var claim =
            User.FindFirst("CodSucursal")
                ?.Value
                ?.Trim();

        if (!string.IsNullOrWhiteSpace(claim))
            return claim;

        return _configuration[
            "TomaFisica:COD_SUCURSAL"]?.Trim() ?? "";
    }

    private static string Normalizar(
        string? valor)
    {
        return (valor ?? "")
            .Trim()
            .ToUpperInvariant();
    }
}
