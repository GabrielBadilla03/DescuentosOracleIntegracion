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
    private const string LotePredeterminado = "001";

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
                mensaje = "No se encuentra configurada la sucursal del usuario."
            });
        }

        var bodegas = await ObtenerBodegasSucursalAsync(
            codSucursal,
            cancellationToken);

        var siguienteSecuencia =
            await ObtenerSiguienteSecuenciaAsync(cancellationToken);

        return Json(new
        {
            ok = true,
            codCia = CodCiaFijo,
            codSucursal,
            usuario = ObtenerUsuario(),
            estado = EstadoNuevaToma,
            estadoDescripcion = "Listo para toma",
            siguienteSecuencia,
            bodegas = bodegas.Select(x => new
            {
                codigo = (x.COD_BODEGA ?? "").Trim(),
                descripcion = (x.DES_BODEGA ?? "").Trim()
            })
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(
        string? codBodega,
        string? tipo,
        string? observacion,
        CancellationToken cancellationToken)
    {
        var codSucursal = ObtenerCodSucursal();
        var bodegaKey = Normalizar(codBodega);
        var tipoKey = Normalizar(tipo);
        var observacionLimpia = (observacion ?? "").Trim();

        if (string.IsNullOrWhiteSpace(codSucursal))
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = "No se encuentra configurada la sucursal del usuario."
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

        if (tipoKey is not ("1" or "2" or "3"))
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = "El tipo de toma debe ser 1, 2 o 3."
            });
        }

        if (observacionLimpia.Length > 100)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = "La observación no puede superar 100 caracteres."
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

        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var nuevaSecuencia =
                await ObtenerSiguienteSecuenciaAsync(cancellationToken);

            var usuario = ObtenerUsuario();

            if (usuario.Length > 30)
                usuario = usuario[..30];

            var nuevaToma = new INV_TOMAFI
            {
                SECUENCIA = nuevaSecuencia,
                COD_CIA = CodCiaFijo,
                COD_BODEGA = bodegaKey,
                OBSERVACION = string.IsNullOrWhiteSpace(observacionLimpia)
                    ? null
                    : observacionLimpia,
                FEC_MOVTO = DateTime.Now,
                FEC_APLICA = null,

                /*
                 * Esta pantalla web crea directamente en L porque el proceso
                 * de preparación original (P -> L) depende de tablas/procesos
                 * que no están incluidos en este proyecto.
                 */
                ESTADO = EstadoNuevaToma,

                // 1 = un conteo
                // 2 = dos conteos
                // 3 = dos conteos + verificación
                TIPO = tipoKey,

                USUARIO = usuario,
                LOCAL1 = "S",
                REPLICA1 = "S"
            };

            _context.INV_TOMAFIs.Add(nuevaToma);

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Json(new
            {
                ok = true,
                mensaje = $"Toma física {nuevaSecuencia} creada correctamente.",
                secuencia = nuevaSecuencia,
                codCia = CodCiaFijo,
                codBodega = bodegaKey,
                tipo = tipoKey,
                estado = EstadoNuevaToma,
                estadoDescripcion = "Listo para toma",
                usuario,
                fechaMovimiento = nuevaToma.FEC_MOVTO.ToString("dd/MM/yyyy HH:mm"),
                siguienteSecuencia = nuevaSecuencia + 1
            });
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _context.ChangeTracker.Clear();

            return StatusCode(500, new
            {
                ok = false,
                mensaje = "No fue posible crear la toma física.",
                detalle = ex.GetBaseException().Message
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _context.ChangeTracker.Clear();

            return StatusCode(500, new
            {
                ok = false,
                mensaje = "Ocurrió un error al crear la toma física.",
                detalle = ex.Message
            });
        }
    }

    /*
     * Consulta de encabezados de INV_TOMAFIS desde la misma pantalla de
     * creación. Solo muestra tomas pertenecientes a las bodegas de la
     * sucursal del usuario.
     */
    [HttpGet]
    public async Task<IActionResult> ListarTomasConsulta(
        string? secuencia,
        string? estado,
        string? codBodega,
        CancellationToken cancellationToken)
    {
        var codSucursal = ObtenerCodSucursal();

        if (string.IsNullOrWhiteSpace(codSucursal))
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = "No se encuentra configurada la sucursal del usuario."
            });
        }

        int? secuenciaFiltro = null;

        if (!string.IsNullOrWhiteSpace(secuencia))
        {
            if (!int.TryParse(secuencia.Trim(), out var secuenciaNumero) ||
                secuenciaNumero <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "La secuencia indicada no es válida."
                });
            }

            secuenciaFiltro = secuenciaNumero;
        }

        var estadoKey = Normalizar(estado);

        if (!string.IsNullOrWhiteSpace(estadoKey) &&
            estadoKey is not ("P" or "L" or "A"))
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = "El estado debe ser P, L, A o Todos."
            });
        }

        var bodegaKey = Normalizar(codBodega);
        var bodegas = await ObtenerBodegasSucursalAsync(
            codSucursal,
            cancellationToken);

        var codigosBodega = bodegas
            .Select(x => (x.COD_BODEGA ?? "").Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(bodegaKey) &&
            !codigosBodega.Contains(bodegaKey, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = $"La bodega {bodegaKey} no pertenece a la sucursal {codSucursal}."
            });
        }

        if (codigosBodega.Count == 0)
        {
            return Json(new
            {
                ok = true,
                tomas = Array.Empty<object>()
            });
        }

        var query = _context.INV_TOMAFIs
            .AsNoTracking()
            .Where(x =>
                x.COD_CIA != null &&
                x.COD_BODEGA != null &&
                x.COD_CIA.Trim() == CodCiaFijo &&
                codigosBodega.Contains(x.COD_BODEGA.Trim()));

        if (secuenciaFiltro.HasValue)
            query = query.Where(x => x.SECUENCIA == secuenciaFiltro.Value);

        if (!string.IsNullOrWhiteSpace(estadoKey))
        {
            query = query.Where(x =>
                x.ESTADO != null &&
                x.ESTADO.Trim().ToUpper() == estadoKey);
        }

        if (!string.IsNullOrWhiteSpace(bodegaKey))
        {
            query = query.Where(x =>
                x.COD_BODEGA != null &&
                x.COD_BODEGA.Trim().ToUpper() == bodegaKey);
        }

        var tomasDb = await query
            .OrderByDescending(x => x.SECUENCIA)
            .Take(100)
            .Select(x => new
            {
                x.SECUENCIA,
                x.COD_BODEGA,
                x.FEC_MOVTO,
                x.FEC_APLICA,
                x.ESTADO,
                x.TIPO,
                x.OBSERVACION,
                x.USUARIO
            })
            .ToListAsync(cancellationToken);

        var descripcionesBodega = bodegas
            .GroupBy(
                x => (x.COD_BODEGA ?? "").Trim(),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => (x.First().DES_BODEGA ?? "").Trim(),
                StringComparer.OrdinalIgnoreCase);

        var tomas = tomasDb.Select(x =>
        {
            var bodega = (x.COD_BODEGA ?? "").Trim();
            descripcionesBodega.TryGetValue(bodega, out var descripcionBodega);

            return new
            {
                secuencia = x.SECUENCIA,
                codBodega = bodega,
                desBodega = descripcionBodega ?? "",
                fechaMovimiento = x.FEC_MOVTO,
                fechaAplica = x.FEC_APLICA,
                estado = Normalizar(x.ESTADO),
                estadoDescripcion = DescribirEstado(x.ESTADO),
                tipo = Normalizar(x.TIPO),
                tipoDescripcion = DescribirTipo(x.TIPO),
                observacion = (x.OBSERVACION ?? "").Trim(),
                usuario = (x.USUARIO ?? "").Trim()
            };
        });

        return Json(new
        {
            ok = true,
            tomas
        });
    }

    [HttpGet]
    public async Task<IActionResult> CargarTomaConsulta(
        int secuencia,
        CancellationToken cancellationToken)
    {
        var validacion = await ObtenerTomaAccesibleAsync(
            secuencia,
            tracking: false,
            cancellationToken);

        if (!validacion.Ok)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = validacion.Mensaje
            });
        }

        var toma = validacion.Toma!;
        var bodega = validacion.Bodega!;
        var resumen = await ObtenerResumenConteosAsync(
            toma,
            cancellationToken);

        return Json(new
        {
            ok = true,
            toma = CrearRespuestaConsulta(toma, bodega, resumen)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AplicarToma(
        int secuencia,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var validacion = await ObtenerTomaAccesibleAsync(
                secuencia,
                tracking: true,
                cancellationToken);

            if (!validacion.Ok)
            {
                await transaction.RollbackAsync(cancellationToken);

                return BadRequest(new
                {
                    ok = false,
                    mensaje = validacion.Mensaje
                });
            }

            var toma = validacion.Toma!;
            var bodega = validacion.Bodega!;
            var estado = Normalizar(toma.ESTADO);

            if (estado == "A")
            {
                await transaction.RollbackAsync(cancellationToken);

                return BadRequest(new
                {
                    ok = false,
                    mensaje = $"La toma {secuencia} ya se encuentra aplicada."
                });
            }

            if (estado != "L")
            {
                await transaction.RollbackAsync(cancellationToken);

                return BadRequest(new
                {
                    ok = false,
                    mensaje =
                        $"La toma {secuencia} no está lista para aplicar. Estado actual: {estado}."
                });
            }

            var resumen = await ObtenerResumenConteosAsync(
                toma,
                cancellationToken);

            if (!resumen.TipoSoportado)
            {
                await transaction.RollbackAsync(cancellationToken);

                return BadRequest(new
                {
                    ok = false,
                    mensaje =
                        $"El tipo de toma {Normalizar(toma.TIPO)} no se puede aplicar desde esta pantalla."
                });
            }

            if (resumen.TotalDetalles == 0)
            {
                await transaction.RollbackAsync(cancellationToken);

                return BadRequest(new
                {
                    ok = false,
                    mensaje =
                        "La toma no tiene registros de conteo. Debe existir al menos un detalle antes de aplicarla."
                });
            }

            if (resumen.PendientesRequeridos > 0)
            {
                await transaction.RollbackAsync(cancellationToken);

                return BadRequest(new
                {
                    ok = false,
                    mensaje =
                        $"La toma tiene {resumen.PasosCompletados}/{resumen.PasosRequeridos} conteos requeridos completos. " +
                        $"Faltan {resumen.PendientesRequeridos} etapa(s) antes de aplicarla."
                });
            }

            toma.ESTADO = "A";
            toma.FEC_APLICA = DateTime.Now;

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Json(new
            {
                ok = true,
                mensaje =
                    $"Toma física {secuencia} aplicada correctamente. " +
                    $"{resumen.PasosCompletados}/{resumen.PasosRequeridos} conteos requeridos completados.",
                toma = CrearRespuestaConsulta(toma, bodega, resumen)
            });
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _context.ChangeTracker.Clear();

            return StatusCode(500, new
            {
                ok = false,
                mensaje = "No fue posible aplicar la toma física.",
                detalle = ex.GetBaseException().Message
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _context.ChangeTracker.Clear();

            return StatusCode(500, new
            {
                ok = false,
                mensaje = "Ocurrió un error al aplicar la toma física.",
                detalle = ex.Message
            });
        }
    }

    private async Task<List<INV_BODEGA>> ObtenerBodegasSucursalAsync(
        string codSucursal,
        CancellationToken cancellationToken)
    {
        return await _context.INV_BODEGAs
            .AsNoTracking()
            .Where(x =>
                x.COD_CIA != null &&
                x.COD_BODEGA != null &&
                x.SUCURSAL != null &&
                x.COD_CIA.Trim() == CodCiaFijo &&
                x.SUCURSAL.Trim() == codSucursal)
            .OrderBy(x => x.COD_BODEGA)
            .ToListAsync(cancellationToken);
    }

    private async Task<TomaAccesibleResult> ObtenerTomaAccesibleAsync(
        int secuencia,
        bool tracking,
        CancellationToken cancellationToken)
    {
        if (secuencia <= 0)
        {
            return TomaAccesibleResult.Fallo(
                "La secuencia indicada no es válida.");
        }

        var codSucursal = ObtenerCodSucursal();

        if (string.IsNullOrWhiteSpace(codSucursal))
        {
            return TomaAccesibleResult.Fallo(
                "No se encuentra configurada la sucursal del usuario.");
        }

        IQueryable<INV_TOMAFI> query = _context.INV_TOMAFIs;

        if (!tracking)
            query = query.AsNoTracking();

        var toma = await query
            .FirstOrDefaultAsync(x =>
                x.SECUENCIA == secuencia &&
                x.COD_CIA != null &&
                x.COD_CIA.Trim() == CodCiaFijo,
                cancellationToken);

        if (toma == null)
        {
            return TomaAccesibleResult.Fallo(
                $"No existe la toma física {secuencia} para la compañía {CodCiaFijo}.");
        }

        var codBodega = (toma.COD_BODEGA ?? "").Trim();

        if (string.IsNullOrWhiteSpace(codBodega))
        {
            return TomaAccesibleResult.Fallo(
                "La toma física no tiene una bodega definida.");
        }

        var bodega = await _context.INV_BODEGAs
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.COD_CIA != null &&
                x.COD_BODEGA != null &&
                x.SUCURSAL != null &&
                x.COD_CIA.Trim() == CodCiaFijo &&
                x.COD_BODEGA.Trim() == codBodega &&
                x.SUCURSAL.Trim() == codSucursal,
                cancellationToken);

        if (bodega == null)
        {
            return TomaAccesibleResult.Fallo(
                $"La bodega {codBodega} no pertenece a la sucursal {codSucursal}.");
        }

        return TomaAccesibleResult.Exito(toma, bodega);
    }

    private async Task<ResumenConteos> ObtenerResumenConteosAsync(
        INV_TOMAFI toma,
        CancellationToken cancellationToken)
    {
        var detalles = await _context.INV_DETTOMAFIs
            .AsNoTracking()
            .Where(x =>
                x.SECUENCIA == toma.SECUENCIA &&
                x.COD_LOTE == LotePredeterminado &&
                x.LOCALIZA != null &&
                x.LOCALIZA.Trim() != LotePredeterminado)
            .Select(x => new
            {
                x.CONTEO1,
                x.CONTEO2,
                x.CONTEO3
            })
            .ToListAsync(cancellationToken);

        var total = detalles.Count;

        var completos1 = detalles.Count(x =>
            x.CONTEO1.HasValue && x.CONTEO1.Value >= 0);

        var completos2 = detalles.Count(x =>
            x.CONTEO2.HasValue && x.CONTEO2.Value >= 0);

        var completos3 = detalles.Count(x =>
            x.CONTEO3.HasValue && x.CONTEO3.Value >= 0);

        var conteo1Completo =
            total > 0 && completos1 == total;

        var conteo2Completo =
            total > 0 && completos2 == total;

        var conteo3Completo =
            total > 0 && completos3 == total;

        var tipo = Normalizar(toma.TIPO);
        var tipoSoportado = tipo is "P" or "1" or "2" or "3";

        /*
         * El TIPO indica cuántas etapas debe completar la toma:
         *
         * P / 1 = solamente Conteo 1
         * 2     = Conteo 1 + Conteo 2
         * 3     = Conteo 1 + Conteo 2 + Verificación
         *
         * Para una toma tipo 3 completamente terminada:
         * Conteo 1     = 1 / 3
         * Conteo 2     = 2 / 3
         * Verificación = 3 / 3
         * Requerido    = 3 / 3 completados
         */
        var pasosRequeridos = tipo switch
        {
            "P" => 1,
            "1" => 1,
            "2" => 2,
            "3" => 3,
            _ => 0
        };

        var pasosCompletados = 0;

        if (pasosRequeridos >= 1 && conteo1Completo)
            pasosCompletados++;

        if (pasosRequeridos >= 2 && conteo2Completo)
            pasosCompletados++;

        if (pasosRequeridos >= 3 && conteo3Completo)
            pasosCompletados++;

        var conteoRequerido = tipo switch
        {
            "P" => "Conteo 1",
            "1" => "Conteo 1",
            "2" => "Conteo 2",
            "3" => "Verificación",
            _ => "conteo requerido"
        };

        return new ResumenConteos
        {
            TotalDetalles = total,
            Conteo1Completos = completos1,
            Conteo2Completos = completos2,
            Conteo3Completos = completos3,
            Conteo1Completo = conteo1Completo,
            Conteo2Completo = conteo2Completo,
            Conteo3Completo = conteo3Completo,
            PasosRequeridos = pasosRequeridos,
            PasosCompletados = pasosCompletados,
            CompletosRequeridos = pasosCompletados,
            PendientesRequeridos =
                Math.Max(0, pasosRequeridos - pasosCompletados),
            ConteoRequerido = conteoRequerido,
            TipoSoportado = tipoSoportado
        };
    }

    private static object CrearRespuestaConsulta(
        INV_TOMAFI toma,
        INV_BODEGA bodega,
        ResumenConteos resumen)
    {
        var estado = Normalizar(toma.ESTADO);
        var puedeAplicar =
            estado == "L" &&
            resumen.TipoSoportado &&
            resumen.TotalDetalles > 0 &&
            resumen.PendientesRequeridos == 0;

        string motivoAplicacion;

        if (estado == "A")
            motivoAplicacion = "La toma ya fue aplicada.";
        else if (estado == "P")
            motivoAplicacion = "La toma está pendiente y todavía no está lista para conteo/aplicación.";
        else if (estado != "L")
            motivoAplicacion = $"El estado {estado} no permite aplicar la toma.";
        else if (!resumen.TipoSoportado)
            motivoAplicacion = $"El tipo {Normalizar(toma.TIPO)} no se puede aplicar desde esta pantalla.";
        else if (resumen.TotalDetalles == 0)
            motivoAplicacion = "La toma todavía no tiene detalles de conteo.";
        else if (resumen.PendientesRequeridos > 0)
            motivoAplicacion =
                $"Avance requerido: {resumen.PasosCompletados}/{resumen.PasosRequeridos} conteos completos. " +
                $"Faltan {resumen.PendientesRequeridos} etapa(s) antes de aplicar.";
        else
            motivoAplicacion =
                $"Requerido: {resumen.PasosCompletados}/{resumen.PasosRequeridos} completados. " +
                "La toma se puede aplicar.";

        return new
        {
            secuencia = toma.SECUENCIA,
            codCia = (toma.COD_CIA ?? "").Trim(),
            codBodega = (toma.COD_BODEGA ?? "").Trim(),
            desBodega = (bodega.DES_BODEGA ?? "").Trim(),
            fechaMovimiento = toma.FEC_MOVTO,
            fechaAplica = toma.FEC_APLICA,
            estado,
            estadoDescripcion = DescribirEstado(estado),
            tipo = Normalizar(toma.TIPO),
            tipoDescripcion = DescribirTipo(toma.TIPO),
            observacion = (toma.OBSERVACION ?? "").Trim(),
            usuario = (toma.USUARIO ?? "").Trim(),
            totalDetalles = resumen.TotalDetalles,
            conteo1Completos = resumen.Conteo1Completos,
            conteo2Completos = resumen.Conteo2Completos,
            conteo3Completos = resumen.Conteo3Completos,
            conteo1Completo = resumen.Conteo1Completo,
            conteo2Completo = resumen.Conteo2Completo,
            conteo3Completo = resumen.Conteo3Completo,
            pasosRequeridos = resumen.PasosRequeridos,
            pasosCompletados = resumen.PasosCompletados,
            completosRequeridos = resumen.CompletosRequeridos,
            pendientesRequeridos = resumen.PendientesRequeridos,
            conteoRequerido = resumen.ConteoRequerido,
            puedeAplicar,
            motivoAplicacion
        };
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
            usuario = User.Identity?.Name?.Trim();
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

        return _configuration["TomaFisica:COD_SUCURSAL"]?.Trim() ?? "";
    }

    private static string DescribirEstado(string? estado)
    {
        return Normalizar(estado) switch
        {
            "P" => "Pendiente",
            "L" => "Listo para toma",
            "A" => "Aplicada",
            _ => "Desconocido"
        };
    }

    private static string DescribirTipo(string? tipo)
    {
        return Normalizar(tipo) switch
        {
            "P" => "Conteo 1",
            "1" => "Un conteo",
            "2" => "Dos conteos",
            "3" => "Dos conteos + verificación",
            "D" => "Conteo dinámico",
            _ => "Tipo no identificado"
        };
    }

    private static string Normalizar(string? valor)
    {
        return (valor ?? "")
            .Trim()
            .ToUpperInvariant();
    }

    private sealed class ResumenConteos
    {
        public int TotalDetalles { get; init; }
        public int Conteo1Completos { get; init; }
        public int Conteo2Completos { get; init; }
        public int Conteo3Completos { get; init; }

        public bool Conteo1Completo { get; init; }
        public bool Conteo2Completo { get; init; }
        public bool Conteo3Completo { get; init; }

        public int PasosRequeridos { get; init; }
        public int PasosCompletados { get; init; }

        public int CompletosRequeridos { get; init; }
        public int PendientesRequeridos { get; init; }
        public string ConteoRequerido { get; init; } = "";
        public bool TipoSoportado { get; init; }
    }

    private sealed class TomaAccesibleResult
    {
        public bool Ok { get; init; }
        public string Mensaje { get; init; } = "";
        public INV_TOMAFI? Toma { get; init; }
        public INV_BODEGA? Bodega { get; init; }

        public static TomaAccesibleResult Exito(
            INV_TOMAFI toma,
            INV_BODEGA bodega)
        {
            return new TomaAccesibleResult
            {
                Ok = true,
                Toma = toma,
                Bodega = bodega
            };
        }

        public static TomaAccesibleResult Fallo(string mensaje)
        {
            return new TomaAccesibleResult
            {
                Ok = false,
                Mensaje = mensaje
            };
        }
    }
}
