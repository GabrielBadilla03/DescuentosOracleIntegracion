using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TomaFisica.Data;
using TomaFisica.ModelsTomaFis;

namespace TomaFisica.Controllers;

[Authorize]
public class ConteoTomaFisicaController : Controller
{
    private const string LotePredeterminado = "001";

    private readonly TomaFisDbContext _context;
    private readonly IConfiguration _configuration;

    public ConteoTomaFisicaController(
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

    /*
     * La forma pdactext_lote trabaja por "modo de conteo":
     *
     * 1 = Conteo 1
     * 2 = Conteo 2
     * 3 = Conteo de verificación
     *
     * Reglas tomadas de la forma:
     * - Conteo 1: no restringe TIPO, salvo que TIPO='D' no permite actualizar.
     * - Conteo 2: solamente tomas TIPO 2 o 3.
     * - Conteo 3: solamente tomas TIPO 3.
     * - El detalle solo se puede actualizar cuando ESTADO='L'.
     */
    [HttpGet]
    public async Task<IActionResult> ListarTomas(
        string? modo,
        CancellationToken cancellationToken)
    {
        var modoKey = NormalizarModo(modo);

        if (modoKey == null)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = "Modo de conteo inválido."
            });
        }

        var codCia = ObtenerCodCia();
        var codSucursal = ObtenerCodSucursal();

        if (string.IsNullOrWhiteSpace(codCia) ||
            string.IsNullOrWhiteSpace(codSucursal))
        {
            return StatusCode(500, new
            {
                ok = false,
                mensaje =
                    "No se encuentra configurada la compañía o sucursal del usuario."
            });
        }

        /*
         * INV_TOMAFIS no necesita tener SUCURSAL en el modelo.
         * La sucursal se valida por la bodega, igual que la forma valida:
         *
         * INV_BODEGA.COD_CIA
         * INV_BODEGA.COD_BODEGA
         * INV_BODEGA.SUCURSAL
         */
        var bodegasSucursal = await _context.INV_BODEGAs
            .AsNoTracking()
            .Where(x =>
                x.COD_CIA != null &&
                x.SUCURSAL != null &&
                x.COD_CIA.Trim() == codCia &&
                x.SUCURSAL.Trim() == codSucursal)
            .Select(x => new
            {
                Codigo = x.COD_BODEGA,
                Descripcion = x.DES_BODEGA
            })
            .ToListAsync(cancellationToken);

        var codigosBodega = bodegasSucursal
            .Select(x => (x.Codigo ?? "").Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (codigosBodega.Count == 0)
        {
            return Json(new
            {
                ok = true,
                tomas = Array.Empty<object>(),
                mensaje =
                    $"No existen bodegas para CIA {codCia}, sucursal {codSucursal}."
            });
        }

        var query = _context.INV_TOMAFIs
            .AsNoTracking()
            .Where(x =>
                x.COD_CIA != null &&
                x.COD_BODEGA != null &&
                x.ESTADO != null &&
                x.TIPO != null &&
                x.COD_CIA.Trim() == codCia &&
                codigosBodega.Contains(x.COD_BODEGA.Trim()) &&
                x.ESTADO.Trim().ToUpper() == "L" &&
                x.TIPO.Trim().ToUpper() != "D");

        if (modoKey == "2")
        {
            query = query.Where(x =>
                x.TIPO.Trim().ToUpper() == "2" ||
                x.TIPO.Trim().ToUpper() == "3");
        }
        else if (modoKey == "3")
        {
            query = query.Where(x =>
                x.TIPO.Trim().ToUpper() == "3");
        }

        var tomasDb = await query
            .OrderByDescending(x => x.SECUENCIA)
            .Select(x => new
            {
                x.SECUENCIA,
                x.COD_BODEGA,
                x.FEC_MOVTO,
                x.ESTADO,
                x.TIPO,
                x.OBSERVACION
            })
            .ToListAsync(cancellationToken);

        var descripcionesBodega = bodegasSucursal
            .GroupBy(
                x => (x.Codigo ?? "").Trim(),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => (x.First().Descripcion ?? "").Trim(),
                StringComparer.OrdinalIgnoreCase);

        var tomas = tomasDb.Select(x =>
        {
            var codBodega = (x.COD_BODEGA ?? "").Trim();

            descripcionesBodega.TryGetValue(
                codBodega,
                out var desBodega);

            return new
            {
                secuencia = x.SECUENCIA,
                codBodega,
                desBodega = desBodega ?? "",
                fecha = x.FEC_MOVTO,
                estado = (x.ESTADO ?? "").Trim(),
                tipo = (x.TIPO ?? "").Trim(),
                observacion = (x.OBSERVACION ?? "").Trim()
            };
        });

        return Json(new
        {
            ok = true,
            tomas
        });
    }

    [HttpGet]
    public async Task<IActionResult> CargarToma(
        int secuencia,
        string? modo,
        CancellationToken cancellationToken)
    {
        var validacion = await ObtenerTomaValidaAsync(
            secuencia,
            modo,
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
        var modoKey = validacion.Modo!;

        var queryDetalles = _context.INV_DETTOMAFIs
            .AsNoTracking()
            .Where(x =>
                x.SECUENCIA == secuencia &&
                x.LOCALIZA != null &&
                x.LOCALIZA.Trim() != LotePredeterminado);

        /*
         * La forma aplica:
         * CONTEO1 >= 0 / CONTEO2 >= 0 / CONTEO3 >= 0
         * y LOCALIZA <> '001'
         * según el modo.
         */
        if (modoKey == "1")
        {
            queryDetalles = queryDetalles.Where(x =>
                x.CONTEO1.HasValue &&
                x.CONTEO1.Value >= 0);
        }
        else if (modoKey == "2")
        {
            queryDetalles = queryDetalles.Where(x =>
                x.CONTEO2.HasValue &&
                x.CONTEO2.Value >= 0);
        }
        else
        {
            queryDetalles = queryDetalles.Where(x =>
                x.CONTEO3.HasValue &&
                x.CONTEO3.Value >= 0);
        }

        var detallesVisibles =
            await queryDetalles.CountAsync(
                cancellationToken);

        return Json(new
        {
            ok = true,
            toma = new
            {
                secuencia = toma.SECUENCIA,
                codCia = (toma.COD_CIA ?? "").Trim(),
                codBodega = (toma.COD_BODEGA ?? "").Trim(),
                desBodega = (bodega.DES_BODEGA ?? "").Trim(),
                fecha = toma.FEC_MOVTO,
                estado = (toma.ESTADO ?? "").Trim(),
                tipo = (toma.TIPO ?? "").Trim(),
                observacion = (toma.OBSERVACION ?? "").Trim(),
                detallesVisibles
            }
        });
    }

    /*
     * Resuelve lo que se pistoleó.
     *
     * Igual que TraduceBar de la forma:
     * - Si existe en INV_ARTIC_CODBAR, devuelve COD_ARTICULO.
     * - Si no existe como código de barras, usa el valor tal como artículo.
     *
     * También conserva el formato de captura ARTICULO%CANTIDAD.
     * En la forma el delimitador proviene de configuración y el valor por
     * defecto es '%'. Aquí puede configurarse en:
     *
     * TomaFisica:DELIMITADOR
     *
     * Si no existe, se utiliza '%'.
     */
    [HttpGet]
    public async Task<IActionResult> ResolverArticulo(
        int secuencia,
        string? modo,
        string? localiza,
        string? codigo,
        CancellationToken cancellationToken)
    {
        var validacion = await ObtenerTomaValidaAsync(
            secuencia,
            modo,
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
        var modoKey = validacion.Modo!;
        var localizaKey = Normalizar(localiza);

        var validacionLocaliza =
            await ValidarLocalizacionAsync(
                toma,
                localizaKey,
                cancellationToken);

        if (!validacionLocaliza.Ok)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = validacionLocaliza.Mensaje
            });
        }

        var interpretacion = InterpretarCodigoEscaneado(
            codigo);

        if (!interpretacion.Ok)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = interpretacion.Mensaje
            });
        }

        var articulo = await ResolverArticuloAsync(
            (toma.COD_CIA ?? "").Trim(),
            interpretacion.Codigo!,
            cancellationToken);

        if (articulo == null)
        {
            return NotFound(new
            {
                ok = false,
                mensaje =
                    $"El código {interpretacion.Codigo} no corresponde a un artículo existente."
            });
        }

        var detalle = await _context.INV_DETTOMAFIs
            .AsNoTracking()
            .Where(x =>
                x.SECUENCIA == secuencia &&
                x.COD_LOTE == LotePredeterminado &&
                x.LOCALIZA != null &&
                x.COD_ARTICULO != null &&
                x.LOCALIZA.Trim().ToUpper() == localizaKey &&
                x.COD_ARTICULO.Trim().ToUpper() ==
                articulo.CodigoNormalizado)
            .Select(x => new
            {
                x.CONTEO1,
                x.CONTEO2,
                x.CONTEO3,
                x.BATCH,
                x.FECHALOTE
            })
            .FirstOrDefaultAsync(cancellationToken);

        decimal? conteoActual = modoKey switch
        {
            "1" => detalle?.CONTEO1,
            "2" => detalle?.CONTEO2,
            "3" => detalle?.CONTEO3,
            _ => null
        };

        return Json(new
        {
            ok = true,
            codigoArticulo = articulo.Codigo,
            descripcion = articulo.Descripcion,
            medida = articulo.Medida,
            cantidadEscaneada =
                interpretacion.Cantidad,
            existeDetalle = detalle != null,
            conteoActual,
            conteo1 = detalle?.CONTEO1,
            conteo2 = detalle?.CONTEO2,
            conteo3 = detalle?.CONTEO3,
            batch = (detalle?.BATCH ?? "").Trim(),
            fechaLote = detalle?.FECHALOTE
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarConteo(
        int secuencia,
        string? modo,
        string? localiza,
        string? codigo,
        string? cantidad,
        string? batch,
        CancellationToken cancellationToken)
    {
        var validacion = await ObtenerTomaValidaAsync(
            secuencia,
            modo,
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
        var modoKey = validacion.Modo!;
        var localizaKey = Normalizar(localiza);

        var validacionLocaliza =
            await ValidarLocalizacionAsync(
                toma,
                localizaKey,
                cancellationToken);

        if (!validacionLocaliza.Ok)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = validacionLocaliza.Mensaje
            });
        }

        var interpretacion = InterpretarCodigoEscaneado(
            codigo);

        if (!interpretacion.Ok)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = interpretacion.Mensaje
            });
        }

        decimal cantidadFinal;

        if (!string.IsNullOrWhiteSpace(cantidad))
        {
            if (!TryParseCantidad(
                    cantidad,
                    out cantidadFinal))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje =
                        "La cantidad digitada no es válida."
                });
            }
        }
        else if (interpretacion.Cantidad.HasValue)
        {
            cantidadFinal =
                interpretacion.Cantidad.Value;
        }
        else
        {
            return BadRequest(new
            {
                ok = false,
                mensaje =
                    "Debe indicar la cantidad del conteo."
            });
        }

        if (cantidadFinal < 0)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje =
                    "La cantidad no puede ser negativa."
            });
        }

        var articulo = await ResolverArticuloAsync(
            (toma.COD_CIA ?? "").Trim(),
            interpretacion.Codigo!,
            cancellationToken);

        if (articulo == null)
        {
            return NotFound(new
            {
                ok = false,
                mensaje =
                    $"El código {interpretacion.Codigo} no corresponde a un artículo existente."
            });
        }

        string? batchReal = null;
        DateTime? fechaLote = null;

        var batchKey = Normalizar(batch);

        /*
         * WHEN-VALIDATE-ITEM de BATCH en la forma:
         * SELECT FECHA FROM PNORDENES WHERE BATCH = :BATCH
         *
         * Si viene vacío, no se valida.
         */
        /*
        if (!string.IsNullOrWhiteSpace(batchKey))
        {
            var orden = await _context.PNORDENEs
                .AsNoTracking()
                .Where(x =>
                    x.BATCH != null &&
                    x.BATCH.Trim().ToUpper() == batchKey)
                .Select(x => new
                {
                    x.BATCH,
                    x.FECHA
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (orden == null)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje =
                        $"El batch {batchKey} no existe."
                });
            }

            batchReal = (orden.BATCH ?? "").Trim();
            fechaLote = orden.FECHA;
        }
        */
        var detalle = await _context.INV_DETTOMAFIs
            .FirstOrDefaultAsync(x =>
                x.SECUENCIA == secuencia &&
                x.COD_LOTE == LotePredeterminado &&
                x.LOCALIZA != null &&
                x.COD_ARTICULO != null &&
                x.LOCALIZA.Trim().ToUpper() == localizaKey &&
                x.COD_ARTICULO.Trim().ToUpper() ==
                articulo.CodigoNormalizado,
                cancellationToken);

        var esNuevo = detalle == null;

        if (detalle == null)
        {
            detalle = new INV_DETTOMAFI
            {
                SECUENCIA = secuencia,
                COD_LOTE = LotePredeterminado,
                COD_ARTICULO = articulo.Codigo,
                LOCALIZA = localizaKey,
                CAN_EXISTE = 0,
                COSTO = 0,
                CONTEO1 = null,
                CONTEO2 = null,
                CONTEO3 = null,
                LOCAL1 = "S",
                REPLICA1 = "S",
                ORIGEN = null,
                BATCH = null,
                FECHALOTE = null,
                RESERVA = "N"
            };

            _context.INV_DETTOMAFIs.Add(detalle);
        }

        /*
         * El campo digitado depende del parámetro CONTEO de la forma.
         */
        if (modoKey == "1")
            detalle.CONTEO1 = cantidadFinal;
        else if (modoKey == "2")
            detalle.CONTEO2 = cantidadFinal;
        else
            detalle.CONTEO3 = cantidadFinal;

        detalle.BATCH = batchReal;
        detalle.FECHALOTE = fechaLote;

        /*
         * Replica ACTUALIZA_CONTEO del FMB.
         *
         * TIPO P/1:
         *   pcant = CONTEO1
         *   actualiza CONTEO1
         *
         * TIPO 2:
         *   pcant = NVL(CONTEO2, CONTEO1)
         *   actualiza CONTEO1 y CONTEO2
         *
         * TIPO 3:
         *   pcant = NVL(CONTEO3, CONTEO2)
         *   actualiza CONTEO2 y CONTEO3
         */
        AplicarActualizaConteo(
            detalle,
            (toma.TIPO ?? "").Trim());

        try
        {
            await _context.SaveChangesAsync(
                cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _context.ChangeTracker.Clear();

            return StatusCode(500, new
            {
                ok = false,
                mensaje =
                    "No fue posible guardar el conteo.",
                detalle =
                    ex.GetBaseException().Message
            });
        }

        decimal? conteoGuardado = modoKey switch
        {
            "1" => detalle.CONTEO1,
            "2" => detalle.CONTEO2,
            "3" => detalle.CONTEO3,
            _ => null
        };

        return Json(new
        {
            ok = true,
            mensaje =
                esNuevo
                    ? "Conteo registrado."
                    : "Conteo actualizado.",
            codigoArticulo = articulo.Codigo,
            descripcion = articulo.Descripcion,
            medida = articulo.Medida,
            localiza = localizaKey,
            lote = LotePredeterminado,
            batch = detalle.BATCH,
            fechaLote = detalle.FECHALOTE,
            cantidad = conteoGuardado,
            conteo1 = detalle.CONTEO1,
            conteo2 = detalle.CONTEO2,
            conteo3 = detalle.CONTEO3,
            esNuevo
        });
    }


    /*
     * Consulta los conteos existentes de una toma.
     *
     * Se puede filtrar por:
     * - localización
     * - artículo
     * - código de barras
     *
     * Siempre se excluye LOCALIZA='001', igual que la pantalla de captura.
     * La consulta muestra CONTEO1, CONTEO2 y CONTEO3, pero la opción
     * "Eliminar" únicamente limpia el conteo correspondiente al modo activo.
     */
    [HttpGet]
    public async Task<IActionResult> ConsultarConteos(
        int secuencia,
        string? modo,
        string? localiza,
        string? codigo,
        CancellationToken cancellationToken)
    {
        var validacion = await ObtenerTomaValidaAsync(
            secuencia,
            modo,
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
        var modoKey = validacion.Modo!;

        var localizaKey = Normalizar(localiza);
        var codigoEntrada = (codigo ?? "").Trim();

        if (localizaKey == LotePredeterminado)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje =
                    "La localización 001 no se procesa en esta pantalla."
            });
        }

        string? articuloKey = null;

        if (!string.IsNullOrWhiteSpace(codigoEntrada))
        {
            var articulo = await ResolverArticuloAsync(
                (toma.COD_CIA ?? "").Trim(),
                codigoEntrada,
                cancellationToken);

            if (articulo == null)
            {
                return NotFound(new
                {
                    ok = false,
                    mensaje =
                        $"El código {codigoEntrada} no corresponde a un artículo existente."
                });
            }

            articuloKey = articulo.CodigoNormalizado;
        }

        var query = _context.INV_DETTOMAFIs
            .AsNoTracking()
            .Where(x =>
                x.SECUENCIA == secuencia &&
                x.COD_LOTE == LotePredeterminado &&
                x.LOCALIZA != null &&
                x.LOCALIZA.Trim() != LotePredeterminado);

        if (!string.IsNullOrWhiteSpace(localizaKey))
        {
            query = query.Where(x =>
                x.LOCALIZA != null &&
                x.LOCALIZA.Trim().ToUpper() == localizaKey);
        }

        if (!string.IsNullOrWhiteSpace(articuloKey))
        {
            query = query.Where(x =>
                x.COD_ARTICULO != null &&
                x.COD_ARTICULO.Trim().ToUpper() == articuloKey);
        }

        /*
         * Se limita la respuesta para que una consulta sin filtros
         * no cargue miles de filas en un handheld.
         */
        var detalles = await query
            .OrderBy(x => x.LOCALIZA)
            .ThenBy(x => x.COD_ARTICULO)
            .Take(200)
            .Select(x => new
            {
                x.LOCALIZA,
                x.COD_ARTICULO,
                x.CONTEO1,
                x.CONTEO2,
                x.CONTEO3,
                x.BATCH,
                x.FECHALOTE
            })
            .ToListAsync(cancellationToken);

        var codigosArticulo = detalles
            .Select(x => Normalizar(x.COD_ARTICULO))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var descripciones = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        if (codigosArticulo.Count > 0)
        {
            var articulos = await _context.INV_ARTICULOs
                .AsNoTracking()
                .Where(x =>
                    x.COD_ARTICULO != null &&
                    codigosArticulo.Contains(
                        x.COD_ARTICULO.Trim().ToUpper()))
                .Select(x => new
                {
                    x.COD_ARTICULO,
                    x.DESCRIPCION
                })
                .ToListAsync(cancellationToken);

            descripciones = articulos
                .GroupBy(
                    x => Normalizar(x.COD_ARTICULO),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => (x.First().DESCRIPCION ?? "").Trim(),
                    StringComparer.OrdinalIgnoreCase);
        }

        var filas = detalles.Select(x =>
        {
            var codArticulo = (x.COD_ARTICULO ?? "").Trim();
            var localizacion = (x.LOCALIZA ?? "").Trim();

            descripciones.TryGetValue(
                Normalizar(codArticulo),
                out var descripcion);

            decimal? conteoActual = modoKey switch
            {
                "1" => x.CONTEO1,
                "2" => x.CONTEO2,
                "3" => x.CONTEO3,
                _ => null
            };

            return new
            {
                localiza = localizacion,
                codigoArticulo = codArticulo,
                descripcion = descripcion ?? "",
                conteo1 = x.CONTEO1,
                conteo2 = x.CONTEO2,
                conteo3 = x.CONTEO3,
                conteoActual,
                batch = (x.BATCH ?? "").Trim(),
                fechaLote = x.FECHALOTE,
                puedeEliminar = conteoActual.HasValue
            };
        }).ToList();

        return Json(new
        {
            ok = true,
            modo = modoKey,
            cantidad = filas.Count,
            limitado = filas.Count >= 200,
            filas
        });
    }

    /*
     * "Eliminar conteo" NO elimina la fila de INV_DETTOMAFIS.
     *
     * Solo pone en NULL el conteo correspondiente al modo activo:
     *
     * modo 1 -> CONTEO1 = NULL
     * modo 2 -> CONTEO2 = NULL
     * modo 3 -> CONTEO3 = NULL
     *
     * Los demás conteos, CAN_EXISTE, COSTO, BATCH, FECHALOTE,
     * ORIGEN, RESERVA, etc. se conservan.
     */
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarConteo(
        int secuencia,
        string? modo,
        string? localiza,
        string? codigoArticulo,
        CancellationToken cancellationToken)
    {
        var validacion = await ObtenerTomaValidaAsync(
            secuencia,
            modo,
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
        var modoKey = validacion.Modo!;
        var localizaKey = Normalizar(localiza);
        var articuloKey = Normalizar(codigoArticulo);

        if (string.IsNullOrWhiteSpace(localizaKey) ||
            string.IsNullOrWhiteSpace(articuloKey))
        {
            return BadRequest(new
            {
                ok = false,
                mensaje =
                    "La localización y el artículo son obligatorios."
            });
        }

        var validacionLocaliza =
            await ValidarLocalizacionAsync(
                toma,
                localizaKey,
                cancellationToken);

        if (!validacionLocaliza.Ok)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = validacionLocaliza.Mensaje
            });
        }

        var detalle = await _context.INV_DETTOMAFIs
            .FirstOrDefaultAsync(x =>
                x.SECUENCIA == secuencia &&
                x.COD_LOTE == LotePredeterminado &&
                x.LOCALIZA != null &&
                x.COD_ARTICULO != null &&
                x.LOCALIZA.Trim().ToUpper() == localizaKey &&
                x.COD_ARTICULO.Trim().ToUpper() == articuloKey,
                cancellationToken);

        if (detalle == null)
        {
            return NotFound(new
            {
                ok = false,
                mensaje =
                    "El registro de conteo ya no existe."
            });
        }

        decimal? valorAnterior;

        if (modoKey == "1")
        {
            valorAnterior = detalle.CONTEO1;

            if (!valorAnterior.HasValue)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje =
                        "El Conteo 1 ya está vacío."
                });
            }

            detalle.CONTEO1 = null;
        }
        else if (modoKey == "2")
        {
            valorAnterior = detalle.CONTEO2;

            if (!valorAnterior.HasValue)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje =
                        "El Conteo 2 ya está vacío."
                });
            }

            detalle.CONTEO2 = null;
        }
        else
        {
            valorAnterior = detalle.CONTEO3;

            if (!valorAnterior.HasValue)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje =
                        "El Conteo de Verificación ya está vacío."
                });
            }

            detalle.CONTEO3 = null;
        }

        try
        {
            await _context.SaveChangesAsync(
                cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _context.ChangeTracker.Clear();

            return StatusCode(500, new
            {
                ok = false,
                mensaje =
                    "No fue posible eliminar el conteo.",
                detalle =
                    ex.GetBaseException().Message
            });
        }

        return Json(new
        {
            ok = true,
            mensaje =
                $"Se eliminó el conteo {modoKey} del artículo {(detalle.COD_ARTICULO ?? "").Trim()}.",
            localiza = (detalle.LOCALIZA ?? "").Trim(),
            codigoArticulo = (detalle.COD_ARTICULO ?? "").Trim(),
            valorAnterior,
            conteo1 = detalle.CONTEO1,
            conteo2 = detalle.CONTEO2,
            conteo3 = detalle.CONTEO3
        });
    }

    private async Task<TomaValidacionResult>
        ObtenerTomaValidaAsync(
            int secuencia,
            string? modo,
            CancellationToken cancellationToken)
    {
        var modoKey = NormalizarModo(modo);

        if (modoKey == null)
        {
            return TomaValidacionResult.Fallo(
                "Modo de conteo inválido.");
        }

        var codCia = ObtenerCodCia();
        var codSucursal = ObtenerCodSucursal();

        if (string.IsNullOrWhiteSpace(codCia) ||
            string.IsNullOrWhiteSpace(codSucursal))
        {
            return TomaValidacionResult.Fallo(
                "No se encuentra configurada la compañía o sucursal del usuario.");
        }

        var toma = await _context.INV_TOMAFIs
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.SECUENCIA == secuencia &&
                x.COD_CIA != null &&
                x.COD_CIA.Trim() == codCia,
                cancellationToken);

        if (toma == null)
        {
            return TomaValidacionResult.Fallo(
                $"No existe la toma física {secuencia} para la compañía {codCia}.");
        }

        var codBodega =
            (toma.COD_BODEGA ?? "").Trim();

        if (string.IsNullOrWhiteSpace(codBodega))
        {
            return TomaValidacionResult.Fallo(
                "La toma física no tiene una bodega definida.");
        }

        var bodega = await _context.INV_BODEGAs
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.COD_CIA != null &&
                x.COD_BODEGA != null &&
                x.SUCURSAL != null &&
                x.COD_CIA.Trim() == codCia &&
                x.COD_BODEGA.Trim() == codBodega &&
                x.SUCURSAL.Trim() == codSucursal,
                cancellationToken);

        if (bodega == null)
        {
            return TomaValidacionResult.Fallo(
                $"La bodega {codBodega} no pertenece a la sucursal {codSucursal}.");
        }

        var estado = Normalizar(toma.ESTADO);
        var tipo = Normalizar(toma.TIPO);

        /*
         * WHEN-NEW-BLOCK-INSTANCE del FMB:
         * solo permite actualizar el detalle cuando ESTADO='L'.
         */
        if (estado != "L")
        {
            return TomaValidacionResult.Fallo(
                $"La toma {secuencia} no está lista para conteo. Estado actual: {estado}.");
        }

        /*
         * La forma deshabilita Insert/Update/Delete del detalle si TIPO='D'.
         */
        if (tipo == "D")
        {
            return TomaValidacionResult.Fallo(
                "La toma es de tipo Conteo Dinámico (D) y esta pantalla no permite modificarla.");
        }

        if (modoKey == "2" &&
            tipo != "2" &&
            tipo != "3")
        {
            return TomaValidacionResult.Fallo(
                "Esta toma no admite Conteo 2.");
        }

        if (modoKey == "3" &&
            tipo != "3")
        {
            return TomaValidacionResult.Fallo(
                "Esta toma no admite Conteo de Verificación.");
        }

        return TomaValidacionResult.Exito(
            toma,
            bodega,
            modoKey);
    }

    private async Task<ValidacionSimpleResult>
        ValidarLocalizacionAsync(
            INV_TOMAFI toma,
            string localizaKey,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(localizaKey))
        {
            return ValidacionSimpleResult.Fallo(
                "Debe indicar la localización.");
        }

        /*
         * La forma consulta detalles con LOCALIZA <> '001'.
         */
        if (localizaKey == LotePredeterminado)
        {
            return ValidacionSimpleResult.Fallo(
                "La localización 001 no se procesa en esta pantalla.");
        }

        var codCia =
            (toma.COD_CIA ?? "").Trim();

        var codBodega =
            (toma.COD_BODEGA ?? "").Trim();

        var existe = await _context.INV_LOCs
            .AsNoTracking()
            .AnyAsync(x =>
                x.COD_CIA != null &&
                x.COD_BODEGA != null &&
                x.LOCALIZA != null &&
                x.COD_CIA.Trim() == codCia &&
                x.COD_BODEGA.Trim() == codBodega &&
                x.LOCALIZA.Trim().ToUpper() == localizaKey,
                cancellationToken);

        if (!existe)
        {
            return ValidacionSimpleResult.Fallo(
                $"La localización {localizaKey} no existe en la bodega {codBodega}.");
        }

        return ValidacionSimpleResult.Exito();
    }

    private async Task<ArticuloResuelto?>
        ResolverArticuloAsync(
            string codCia,
            string codigoEntrada,
            CancellationToken cancellationToken)
    {
        var codigoKey = Normalizar(codigoEntrada);

        if (string.IsNullOrWhiteSpace(codigoKey))
            return null;

        /*
         * TraduceBar:
         * si existe como código de barras, se utiliza su COD_ARTICULO.
         * Si no existe, se intenta utilizar el valor como COD_ARTICULO.
         */
        var articuloPorBarra = await _context
            .INV_ARTIC_CODBARs
            .AsNoTracking()
            .Where(x =>
                x.COD_CIA != null &&
                x.COD_BARRAS != null &&
                x.COD_CIA.Trim() == codCia &&
                x.COD_BARRAS.Trim() == codigoEntrada.Trim())
            .Select(x => x.COD_ARTICULO)
            .FirstOrDefaultAsync(cancellationToken);

        var articuloKey =
            !string.IsNullOrWhiteSpace(articuloPorBarra)
                ? Normalizar(articuloPorBarra)
                : codigoKey;

        var articulo = await _context.INV_ARTICULOs
            .AsNoTracking()
            .Where(x =>
                x.COD_ARTICULO != null &&
                x.COD_ARTICULO.Trim().ToUpper() == articuloKey)
            .Select(x => new
            {
                x.COD_ARTICULO,
                x.DESCRIPCION,
                x.MEDIDA
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (articulo == null)
            return null;

        var codigoReal =
            (articulo.COD_ARTICULO ?? "").Trim();

        return new ArticuloResuelto
        {
            Codigo = codigoReal,
            CodigoNormalizado =
                Normalizar(codigoReal),
            Descripcion =
                (articulo.DESCRIPCION ?? "").Trim(),
            Medida =
                (articulo.MEDIDA ?? "").Trim()
        };
    }

    private CodigoInterpretadoResult
        InterpretarCodigoEscaneado(
            string? codigo)
    {
        var entrada =
            (codigo ?? "").Trim();

        if (string.IsNullOrWhiteSpace(entrada))
        {
            return CodigoInterpretadoResult.Fallo(
                "Debe escanear o digitar el artículo.");
        }

        /*
         * El FMB hace REPLACE(',', '.') antes de interpretar la cantidad.
         * Esto permite cantidades decimales recibidas con coma.
         */
        entrada = entrada.Replace(',', '.');

        var delimitador =
            _configuration[
                "TomaFisica:DELIMITADOR"]?.Trim();

        if (string.IsNullOrWhiteSpace(delimitador))
            delimitador = "%";

        var posicion =
            entrada.IndexOf(
                delimitador,
                StringComparison.Ordinal);

        if (posicion > 0)
        {
            var codigoParte =
                entrada[..posicion].Trim();

            var cantidadParte =
                entrada[
                    (posicion + delimitador.Length)..]
                .Trim();

            if (string.IsNullOrWhiteSpace(
                    codigoParte))
            {
                return CodigoInterpretadoResult.Fallo(
                    "El código del artículo está vacío.");
            }

            if (!TryParseCantidad(
                    cantidadParte,
                    out var cantidad))
            {
                return CodigoInterpretadoResult.Fallo(
                    $"La cantidad incluida en el código ({cantidadParte}) no es válida.");
            }

            return CodigoInterpretadoResult.Exito(
                codigoParte,
                cantidad);
        }

        return CodigoInterpretadoResult.Exito(
            entrada,
            null);
    }

    private static void AplicarActualizaConteo(
        INV_DETTOMAFI detalle,
        string? tipoToma)
    {
        var tipo = Normalizar(tipoToma);

        decimal cantidad;

        if (tipo == "2")
        {
            cantidad =
                detalle.CONTEO2 ??
                detalle.CONTEO1 ??
                0;

            detalle.CONTEO1 = cantidad;
            detalle.CONTEO2 = cantidad;
        }
        else if (tipo == "3")
        {
            cantidad =
                detalle.CONTEO3 ??
                detalle.CONTEO2 ??
                0;

            detalle.CONTEO2 = cantidad;
            detalle.CONTEO3 = cantidad;
        }
        else
        {
            cantidad =
                detalle.CONTEO1 ?? 0;

            detalle.CONTEO1 = cantidad;
        }
    }

    private string ObtenerCodCia()
    {
        var claim =
            User.FindFirst("CodCia")
                ?.Value
                ?.Trim();

        if (!string.IsNullOrWhiteSpace(claim))
            return claim;

        return _configuration[
            "TomaFisica:COD_CIA"]?.Trim() ?? "";
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

    private static string? NormalizarModo(
        string? modo)
    {
        var valor = Normalizar(modo);

        return valor is "1" or "2" or "3"
            ? valor
            : null;
    }

    private static bool TryParseCantidad(
        string? valor,
        out decimal cantidad)
    {
        cantidad = 0;

        var normalizado =
            (valor ?? "")
                .Trim()
                .Replace(',', '.');

        return decimal.TryParse(
            normalizado,
            NumberStyles.Number |
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out cantidad);
    }

    private static string Normalizar(
        string? valor)
    {
        return (valor ?? "")
            .Trim()
            .ToUpperInvariant();
    }

    private sealed class TomaValidacionResult
    {
        public bool Ok { get; init; }
        public string Mensaje { get; init; } = "";
        public INV_TOMAFI? Toma { get; init; }
        public INV_BODEGA? Bodega { get; init; }
        public string? Modo { get; init; }

        public static TomaValidacionResult Exito(
            INV_TOMAFI toma,
            INV_BODEGA bodega,
            string modo)
        {
            return new TomaValidacionResult
            {
                Ok = true,
                Toma = toma,
                Bodega = bodega,
                Modo = modo
            };
        }

        public static TomaValidacionResult Fallo(
            string mensaje)
        {
            return new TomaValidacionResult
            {
                Ok = false,
                Mensaje = mensaje
            };
        }
    }

    private sealed class ValidacionSimpleResult
    {
        public bool Ok { get; init; }
        public string Mensaje { get; init; } = "";

        public static ValidacionSimpleResult Exito()
        {
            return new ValidacionSimpleResult
            {
                Ok = true
            };
        }

        public static ValidacionSimpleResult Fallo(
            string mensaje)
        {
            return new ValidacionSimpleResult
            {
                Ok = false,
                Mensaje = mensaje
            };
        }
    }

    private sealed class CodigoInterpretadoResult
    {
        public bool Ok { get; init; }
        public string Mensaje { get; init; } = "";
        public string? Codigo { get; init; }
        public decimal? Cantidad { get; init; }

        public static CodigoInterpretadoResult Exito(
            string codigo,
            decimal? cantidad)
        {
            return new CodigoInterpretadoResult
            {
                Ok = true,
                Codigo = codigo,
                Cantidad = cantidad
            };
        }

        public static CodigoInterpretadoResult Fallo(
            string mensaje)
        {
            return new CodigoInterpretadoResult
            {
                Ok = false,
                Mensaje = mensaje
            };
        }
    }

    private sealed class ArticuloResuelto
    {
        public string Codigo { get; init; } = "";
        public string CodigoNormalizado { get; init; } = "";
        public string Descripcion { get; init; } = "";
        public string Medida { get; init; } = "";
    }
}
