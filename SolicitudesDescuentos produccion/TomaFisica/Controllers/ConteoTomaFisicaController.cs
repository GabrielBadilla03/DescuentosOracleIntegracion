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

    public ConteoTomaFisicaController(TomaFisDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> ListarTomas(string? modo, CancellationToken cancellationToken)
    {
        var modoKey = NormalizarModo(modo);
        if (modoKey == null)
            return BadRequest(new { ok = false, mensaje = "Modo de conteo inválido." });

        var codCia = ObtenerCodCia();
        var codSucursal = ObtenerCodSucursal();
        if (string.IsNullOrWhiteSpace(codCia) || string.IsNullOrWhiteSpace(codSucursal))
            return StatusCode(500, new { ok = false, mensaje = "No está configurada la compañía o la sucursal." });

        var bodegasSucursal = await _context.INV_BODEGAs.AsNoTracking()
            .Where(x => x.COD_CIA != null && x.COD_BODEGA != null && x.SUCURSAL != null &&
                x.COD_CIA.Trim() == codCia && x.SUCURSAL.Trim() == codSucursal)
            .Select(x => new { x.COD_BODEGA, x.DES_BODEGA })
            .ToListAsync(cancellationToken);

        var codigosBodega = bodegasSucursal
            .Select(x => (x.COD_BODEGA ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (codigosBodega.Count == 0)
            return Json(new { ok = true, tomas = Array.Empty<object>(),
                mensaje = $"No existen bodegas para CIA {codCia}, sucursal {codSucursal}." });

        var query = _context.INV_TOMAFIs.AsNoTracking().Where(x =>
            x.COD_CIA != null && x.COD_BODEGA != null && x.ESTADO != null && x.TIPO != null &&
            x.COD_CIA.Trim() == codCia && codigosBodega.Contains(x.COD_BODEGA.Trim()) &&
            x.ESTADO.Trim().ToUpper() == "L" && x.TIPO.Trim().ToUpper() != "D");

        if (modoKey == "2")
            query = query.Where(x => x.TIPO.Trim().ToUpper() == "2" || x.TIPO.Trim().ToUpper() == "3");
        if (modoKey == "3")
            query = query.Where(x => x.TIPO.Trim().ToUpper() == "3");

        var tomasDb = await query.OrderByDescending(x => x.SECUENCIA)
            .Select(x => new { x.SECUENCIA, x.COD_BODEGA, x.FEC_MOVTO, x.ESTADO, x.TIPO, x.OBSERVACION })
            .ToListAsync(cancellationToken);

        var nombres = bodegasSucursal.GroupBy(x => Normalizar(x.COD_BODEGA), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => (x.First().DES_BODEGA ?? "").Trim(), StringComparer.OrdinalIgnoreCase);

        var tomas = tomasDb.Select(t =>
        {
            var bodega = (t.COD_BODEGA ?? "").Trim();
            nombres.TryGetValue(bodega, out var nombre);
            return new { secuencia = t.SECUENCIA, codBodega = bodega, desBodega = nombre ?? "",
                fecha = t.FEC_MOVTO, estado = Normalizar(t.ESTADO), tipo = Normalizar(t.TIPO),
                observacion = (t.OBSERVACION ?? "").Trim() };
        }).ToList();
        return Json(new { ok = true, tomas });
    }

    [HttpGet]
    public async Task<IActionResult> CargarToma(int secuencia, string? modo, CancellationToken cancellationToken)
    {
        var validacion = await ObtenerTomaValidaAsync(secuencia, modo, cancellationToken);
        if (!validacion.Ok)
            return BadRequest(new { ok = false, mensaje = validacion.Mensaje });

        var toma = validacion.Toma!;
        var bodega = validacion.Bodega!;
        var modoKey = validacion.Modo!;
        var detalles = _context.INV_DETTOMAFIs.AsNoTracking().Where(x =>
            x.SECUENCIA == secuencia && x.LOCALIZA != null && x.LOCALIZA.Trim() != LotePredeterminado);
        detalles = modoKey switch
        {
            "1" => detalles.Where(x => x.CONTEO1.HasValue && x.CONTEO1.Value >= 0),
            "2" => detalles.Where(x => x.CONTEO2.HasValue && x.CONTEO2.Value >= 0),
            _ => detalles.Where(x => x.CONTEO3.HasValue && x.CONTEO3.Value >= 0)
        };
        var cantidad = await detalles.CountAsync(cancellationToken);
        var origenes = await _context.INV_ORIGENs.AsNoTracking()
            .Where(x => x.ORIGEN.HasValue)
            .OrderBy(x => x.ORIGEN)
            .Select(x => new { origen = x.ORIGEN, descripcion = x.DESCRIPCION == null ? "" : x.DESCRIPCION.Trim() })
            .ToListAsync(cancellationToken);
        return Json(new
        {
            ok = true,
            toma = new { secuencia = toma.SECUENCIA, codCia = Normalizar(toma.COD_CIA),
                codBodega = Normalizar(toma.COD_BODEGA), desBodega = (bodega.DES_BODEGA ?? "").Trim(),
                fecha = toma.FEC_MOVTO, estado = Normalizar(toma.ESTADO), tipo = Normalizar(toma.TIPO),
                observacion = (toma.OBSERVACION ?? "").Trim(), detallesVisibles = cantidad },
            origenes
        });
    }

    // Independiente de la búsqueda de artículo: se invoca al presionar Enter o salir de Localización.
    [HttpGet]
    public async Task<IActionResult> ValidarLocalizacion(int secuencia, string? modo,
        string? localiza, CancellationToken cancellationToken)
    {
        var validacion = await ObtenerTomaValidaAsync(secuencia, modo, cancellationToken);
        if (!validacion.Ok)
            return BadRequest(new { ok = false, mensaje = validacion.Mensaje });
        var localizaKey = Normalizar(localiza);
        var resultado = await ValidarLocalizacionAsync(validacion.Toma!, localizaKey, cancellationToken);
        if (!resultado.Ok)
            return BadRequest(new { ok = false, mensaje = resultado.Mensaje });
        return Json(new { ok = true, localiza = localizaKey, mensaje = $"Localización {localizaKey} válida." });
    }

    [HttpGet]
    public async Task<IActionResult> ResolverArticulo(int secuencia, string? modo,
        string? localiza, string? codigo, CancellationToken cancellationToken)
    {
        var validacion = await ObtenerTomaValidaAsync(secuencia, modo, cancellationToken);
        if (!validacion.Ok)
            return BadRequest(new { ok = false, mensaje = validacion.Mensaje });
        var toma = validacion.Toma!;
        var localizaKey = Normalizar(localiza);
        var validacionLocaliza = await ValidarLocalizacionAsync(toma, localizaKey, cancellationToken);
        if (!validacionLocaliza.Ok)
            return BadRequest(new { ok = false, mensaje = validacionLocaliza.Mensaje });

        var interpretacion = InterpretarCodigoEscaneado(codigo);
        if (!interpretacion.Ok)
            return BadRequest(new { ok = false, mensaje = interpretacion.Mensaje });
        var articulo = await ResolverArticuloAsync(Normalizar(toma.COD_CIA), interpretacion.Codigo!, cancellationToken);
        if (articulo == null)
            return NotFound(new { ok = false, mensaje = $"El código {interpretacion.Codigo} no corresponde a un artículo existente." });

        var detalle = await _context.INV_DETTOMAFIs.AsNoTracking()
            .Where(x => x.SECUENCIA == secuencia && x.COD_LOTE == LotePredeterminado &&
                x.LOCALIZA != null && x.COD_ARTICULO != null &&
                x.LOCALIZA.Trim().ToUpper() == localizaKey &&
                x.COD_ARTICULO.Trim().ToUpper() == articulo.CodigoNormalizado)
            .Select(x => new { x.CONTEO1, x.CONTEO2, x.CONTEO3, x.ORIGEN, x.BATCH, x.FECHALOTE })
            .FirstOrDefaultAsync(cancellationToken);

        string desOrigen = "";
        if (detalle?.ORIGEN is byte origenActual)
        {
            var origenDb = await _context.INV_ORIGENs.AsNoTracking()
                .Where(x => x.ORIGEN.HasValue && x.ORIGEN.Value == origenActual)
                .Select(x => new { x.ORIGEN, x.DESCRIPCION })
                .FirstOrDefaultAsync(cancellationToken);
            if (origenDb == null)
                return BadRequest(new { ok = false, mensaje = $"El origen {origenActual} no existe en INV_ORIGEN." });
            desOrigen = (origenDb.DESCRIPCION ?? "").Trim();
        }
        decimal? conteoActual = validacion.Modo switch
        {
            "1" => detalle?.CONTEO1, "2" => detalle?.CONTEO2,
            "3" => detalle?.CONTEO3, _ => null
        };
        return Json(new { ok = true, codigoArticulo = articulo.Codigo, descripcion = articulo.Descripcion,
            medida = articulo.Medida, cantidadEscaneada = interpretacion.Cantidad,
            existeDetalle = detalle != null, conteoActual,
            conteo1 = detalle?.CONTEO1, conteo2 = detalle?.CONTEO2, conteo3 = detalle?.CONTEO3,
            origen = detalle?.ORIGEN, desOrigen, batch = (detalle?.BATCH ?? "").Trim(),
            fechaLote = detalle?.FECHALOTE });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarConteo(int secuencia, string? modo, string? localiza,
        string? codigo, string? cantidad, string? origen, string? batch, CancellationToken cancellationToken)
    {
        var validacion = await ObtenerTomaValidaAsync(secuencia, modo, cancellationToken);
        if (!validacion.Ok)
            return BadRequest(new { ok = false, mensaje = validacion.Mensaje });
        var toma = validacion.Toma!;
        var modoKey = validacion.Modo!;
        var localizaKey = Normalizar(localiza);
        var localizacion = await ValidarLocalizacionAsync(toma, localizaKey, cancellationToken);
        if (!localizacion.Ok)
            return BadRequest(new { ok = false, mensaje = localizacion.Mensaje });
        var interpretacion = InterpretarCodigoEscaneado(codigo);
        if (!interpretacion.Ok)
            return BadRequest(new { ok = false, mensaje = interpretacion.Mensaje });

        decimal cantidadFinal;
        if (!string.IsNullOrWhiteSpace(cantidad))
        {
            if (!TryParseCantidad(cantidad, out cantidadFinal))
                return BadRequest(new { ok = false, mensaje = "La cantidad digitada no es válida." });
        }
        else if (interpretacion.Cantidad.HasValue)
            cantidadFinal = interpretacion.Cantidad.Value;
        else
            return BadRequest(new { ok = false, mensaje = "Debe indicar la cantidad del conteo." });
        if (cantidadFinal < 0)
            return BadRequest(new { ok = false, mensaje = "La cantidad no puede ser negativa." });

        var articulo = await ResolverArticuloAsync(Normalizar(toma.COD_CIA), interpretacion.Codigo!, cancellationToken);
        if (articulo == null)
            return NotFound(new { ok = false, mensaje = $"El código {interpretacion.Codigo} no corresponde a un artículo existente." });

        var origenKey = (origen ?? "").Trim();
        if (modoKey == "1" && origenKey.Length == 0)
            return BadRequest(new { ok = false, mensaje = "El origen es obligatorio en Conteo 1." });
        var origenResult = await ResolverOrigenAsync(origenKey, cancellationToken);
        if (!origenResult.Ok)
            return BadRequest(new { ok = false, mensaje = origenResult.Mensaje });

        var batchKey = Normalizar(batch);
        var batchResult = await ResolverBatchAsync(batchKey, cancellationToken);
        if (!batchResult.Ok)
            return BadRequest(new { ok = false, mensaje = batchResult.Mensaje });

        var detalle = await _context.INV_DETTOMAFIs.FirstOrDefaultAsync(x =>
            x.SECUENCIA == secuencia && x.COD_LOTE == LotePredeterminado &&
            x.LOCALIZA != null && x.COD_ARTICULO != null &&
            x.LOCALIZA.Trim().ToUpper() == localizaKey &&
            x.COD_ARTICULO.Trim().ToUpper() == articulo.CodigoNormalizado,
            cancellationToken);
        var esNuevo = detalle == null;
        if (detalle == null)
        {
            detalle = new INV_DETTOMAFI
            {
                SECUENCIA = secuencia, COD_LOTE = LotePredeterminado,
                COD_ARTICULO = articulo.Codigo, LOCALIZA = localizaKey,
                CAN_EXISTE = 0, COSTO = 0, CONTEO1 = null, CONTEO2 = null,
                CONTEO3 = null, LOCAL1 = "S", REPLICA1 = "S",
                ORIGEN = null, BATCH = null, FECHALOTE = null, RESERVA = "N"
            };
            _context.INV_DETTOMAFIs.Add(detalle);
        }

        // En conteos posteriores los campos son opcionales: una selección vacía conserva el dato guardado.
        var origenReal = origenResult.Origen;
        var descripcionOrigen = origenResult.Descripcion;
        var batchReal = batchResult.Batch;
        var fechaLote = batchResult.Fecha;
        if (modoKey != "1" && !esNuevo)
        {
            if (origenKey.Length == 0)
            {
                origenReal = detalle.ORIGEN;
                descripcionOrigen = (await ResolverOrigenAsync(origenReal?.ToString(CultureInfo.InvariantCulture),
                    cancellationToken)).Descripcion;
            }
            if (batchKey.Length == 0)
            {
                batchReal = detalle.BATCH;
                fechaLote = detalle.FECHALOTE;
            }
        }

        if (modoKey == "1") detalle.CONTEO1 = cantidadFinal;
        else if (modoKey == "2") detalle.CONTEO2 = cantidadFinal;
        else detalle.CONTEO3 = cantidadFinal;
        detalle.ORIGEN = origenReal;
        detalle.BATCH = batchReal;
        detalle.FECHALOTE = fechaLote;
        AplicarActualizaConteo(detalle, modoKey);
        try { await _context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex)
        {
            _context.ChangeTracker.Clear();
            return StatusCode(500, new { ok = false, mensaje = "No fue posible guardar el conteo.",
                detalle = ex.GetBaseException().Message });
        }

        decimal? conteoGuardado = modoKey switch
        {
            "1" => detalle.CONTEO1, "2" => detalle.CONTEO2, _ => detalle.CONTEO3
        };
        return Json(new { ok = true, mensaje = esNuevo ? "Conteo registrado." : "Conteo actualizado.",
            codigoArticulo = articulo.Codigo, descripcion = articulo.Descripcion, medida = articulo.Medida,
            localiza = localizaKey, lote = LotePredeterminado, origen = detalle.ORIGEN,
            desOrigen = descripcionOrigen, batch = detalle.BATCH, fechaLote = detalle.FECHALOTE,
            cantidad = conteoGuardado, conteo1 = detalle.CONTEO1, conteo2 = detalle.CONTEO2,
            conteo3 = detalle.CONTEO3, esNuevo });
    }

    [HttpGet]
    public async Task<IActionResult> ConsultarConteos(int secuencia, string? modo, string? localiza,
        string? codigo, CancellationToken cancellationToken)
    {
        var validacion = await ObtenerTomaValidaAsync(secuencia, modo, cancellationToken);
        if (!validacion.Ok)
            return BadRequest(new { ok = false, mensaje = validacion.Mensaje });
        var toma = validacion.Toma!;
        var localizaKey = Normalizar(localiza);
        var codigoEntrada = (codigo ?? "").Trim();
        if (localizaKey == LotePredeterminado)
            return BadRequest(new { ok = false, mensaje = "La localización 001 no se procesa en esta pantalla." });
        if (localizaKey.Length > 0)
        {
            var valido = await ValidarLocalizacionAsync(toma, localizaKey, cancellationToken);
            if (!valido.Ok)
                return BadRequest(new { ok = false, mensaje = valido.Mensaje });
        }
        string? articuloKey = null;
        if (codigoEntrada.Length > 0)
        {
            var articulo = await ResolverArticuloAsync(Normalizar(toma.COD_CIA), codigoEntrada, cancellationToken);
            if (articulo == null)
                return NotFound(new { ok = false, mensaje = $"El código {codigoEntrada} no corresponde a un artículo existente." });
            articuloKey = articulo.CodigoNormalizado;
        }

        var query = _context.INV_DETTOMAFIs.AsNoTracking().Where(x =>
            x.SECUENCIA == secuencia && x.COD_LOTE == LotePredeterminado &&
            x.LOCALIZA != null && x.LOCALIZA.Trim() != LotePredeterminado);
        if (localizaKey.Length > 0)
            query = query.Where(x => x.LOCALIZA != null && x.LOCALIZA.Trim().ToUpper() == localizaKey);
        if (articuloKey != null)
            query = query.Where(x => x.COD_ARTICULO != null && x.COD_ARTICULO.Trim().ToUpper() == articuloKey);

        var detallesDb = await query.OrderBy(x => x.LOCALIZA).ThenBy(x => x.COD_ARTICULO)
            .Take(201)
            .Select(x => new { x.LOCALIZA, x.COD_ARTICULO, x.CONTEO1, x.CONTEO2, x.CONTEO3,
                x.ORIGEN, x.BATCH, x.FECHALOTE })
            .ToListAsync(cancellationToken);
        var limitado = detallesDb.Count > 200;
        var detalles = detallesDb.Take(200).ToList();

        var articulos = detalles.Select(x => Normalizar(x.COD_ARTICULO))
            .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var descripciones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (articulos.Count > 0)
        {
            var db = await _context.INV_ARTICULOs.AsNoTracking()
                .Where(x => x.COD_ARTICULO != null && articulos.Contains(x.COD_ARTICULO.Trim().ToUpper()))
                .Select(x => new { x.COD_ARTICULO, x.DESCRIPCION }).ToListAsync(cancellationToken);
            descripciones = db.GroupBy(x => Normalizar(x.COD_ARTICULO), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => (x.First().DESCRIPCION ?? "").Trim(),
                    StringComparer.OrdinalIgnoreCase);
        }
        var codigosOrigen = detalles.Where(x => x.ORIGEN.HasValue)
            .Select(x => x.ORIGEN!.Value).Distinct().ToList();
        var descripcionesOrigen = new Dictionary<byte, string>();
        if (codigosOrigen.Count > 0)
        {
            var db = await _context.INV_ORIGENs.AsNoTracking()
                .Where(x => x.ORIGEN.HasValue && codigosOrigen.Contains(x.ORIGEN.Value))
                .Select(x => new { x.ORIGEN, x.DESCRIPCION }).ToListAsync(cancellationToken);
            descripcionesOrigen = db.Where(x => x.ORIGEN.HasValue).GroupBy(x => x.ORIGEN!.Value)
                .ToDictionary(x => x.Key, x => (x.First().DESCRIPCION ?? "").Trim());
        }
        var filas = detalles.Select(x =>
        {
            var articulo = (x.COD_ARTICULO ?? "").Trim();
            descripciones.TryGetValue(Normalizar(articulo), out var descripcion);
            var desOrigen = "";
            if (x.ORIGEN.HasValue)
                descripcionesOrigen.TryGetValue(x.ORIGEN.Value, out desOrigen);
            decimal? actual = validacion.Modo switch
            {
                "1" => x.CONTEO1, "2" => x.CONTEO2, "3" => x.CONTEO3, _ => null
            };
            return new { localiza = (x.LOCALIZA ?? "").Trim(), codigoArticulo = articulo,
                descripcion = descripcion ?? "", conteo1 = x.CONTEO1, conteo2 = x.CONTEO2,
                conteo3 = x.CONTEO3, conteoActual = actual, origen = x.ORIGEN,
                desOrigen = desOrigen ?? "", batch = (x.BATCH ?? "").Trim(),
                fechaLote = x.FECHALOTE, puedeEliminar = actual.HasValue };
        }).ToList();
        return Json(new { ok = true, modo = validacion.Modo, cantidad = filas.Count, limitado, filas });
    }

    // Desde la consulta solo modifica ORIGEN/BATCH/FECHALOTE; nunca toca las cantidades.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ActualizarOrigenBatch(int secuencia, string? modo,
        string? localiza, string? codigoArticulo, string? origen, string? batch,
        CancellationToken cancellationToken)
    {
        var validacion = await ObtenerTomaValidaAsync(secuencia, modo, cancellationToken);
        if (!validacion.Ok)
            return BadRequest(new { ok = false, mensaje = validacion.Mensaje });
        var localizaKey = Normalizar(localiza);
        var articuloKey = Normalizar(codigoArticulo);
        var validaLoc = await ValidarLocalizacionAsync(validacion.Toma!, localizaKey, cancellationToken);
        if (!validaLoc.Ok)
            return BadRequest(new { ok = false, mensaje = validaLoc.Mensaje });
        if (articuloKey.Length == 0)
            return BadRequest(new { ok = false, mensaje = "Debe indicar el artículo." });

        var detalle = await _context.INV_DETTOMAFIs.FirstOrDefaultAsync(x =>
            x.SECUENCIA == secuencia && x.COD_LOTE == LotePredeterminado &&
            x.LOCALIZA != null && x.COD_ARTICULO != null &&
            x.LOCALIZA.Trim().ToUpper() == localizaKey &&
            x.COD_ARTICULO.Trim().ToUpper() == articuloKey,
            cancellationToken);
        if (detalle == null)
            return NotFound(new { ok = false, mensaje = "El detalle de la toma ya no existe." });

        var origenKey = (origen ?? "").Trim();
        if (detalle.CONTEO1.HasValue && origenKey.Length == 0)
            return BadRequest(new { ok = false, mensaje = "El origen es obligatorio para registros con Conteo 1." });
        var origenResult = await ResolverOrigenAsync(origenKey, cancellationToken);
        if (!origenResult.Ok)
            return BadRequest(new { ok = false, mensaje = origenResult.Mensaje });
        var batchResult = await ResolverBatchAsync(Normalizar(batch), cancellationToken);
        if (!batchResult.Ok)
            return BadRequest(new { ok = false, mensaje = batchResult.Mensaje });

        detalle.ORIGEN = origenResult.Origen;
        detalle.BATCH = batchResult.Batch;
        detalle.FECHALOTE = batchResult.Fecha;
        try { await _context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex)
        {
            _context.ChangeTracker.Clear();
            return StatusCode(500, new { ok = false,
                mensaje = "No fue posible modificar el origen y batch.",
                detalle = ex.GetBaseException().Message });
        }
        return Json(new { ok = true, mensaje = "Origen y batch actualizados correctamente.",
            origen = detalle.ORIGEN, desOrigen = origenResult.Descripcion,
            batch = detalle.BATCH, fechaLote = detalle.FECHALOTE });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarConteo(int secuencia, string? modo,
        string? localiza, string? codigoArticulo, CancellationToken cancellationToken)
    {
        var validacion = await ObtenerTomaValidaAsync(secuencia, modo, cancellationToken);
        if (!validacion.Ok)
            return BadRequest(new { ok = false, mensaje = validacion.Mensaje });
        var localizaKey = Normalizar(localiza);
        var articuloKey = Normalizar(codigoArticulo);
        if (localizaKey.Length == 0 || articuloKey.Length == 0)
            return BadRequest(new { ok = false, mensaje = "La localización y el artículo son obligatorios." });
        var validaLoc = await ValidarLocalizacionAsync(validacion.Toma!, localizaKey, cancellationToken);
        if (!validaLoc.Ok)
            return BadRequest(new { ok = false, mensaje = validaLoc.Mensaje });

        var detalle = await _context.INV_DETTOMAFIs.FirstOrDefaultAsync(x =>
            x.SECUENCIA == secuencia && x.COD_LOTE == LotePredeterminado &&
            x.LOCALIZA != null && x.COD_ARTICULO != null &&
            x.LOCALIZA.Trim().ToUpper() == localizaKey &&
            x.COD_ARTICULO.Trim().ToUpper() == articuloKey,
            cancellationToken);
        if (detalle == null)
            return NotFound(new { ok = false, mensaje = "El registro de conteo ya no existe." });
        var modoKey = validacion.Modo!;
        decimal? anterior = modoKey switch
        {
            "1" => detalle.CONTEO1, "2" => detalle.CONTEO2, _ => detalle.CONTEO3
        };
        if (!anterior.HasValue)
            return BadRequest(new { ok = false, mensaje = "El conteo seleccionado ya está vacío." });
        if (modoKey == "1") detalle.CONTEO1 = null;
        else if (modoKey == "2") detalle.CONTEO2 = null;
        else detalle.CONTEO3 = null;
        try { await _context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex)
        {
            _context.ChangeTracker.Clear();
            return StatusCode(500, new { ok = false, mensaje = "No fue posible eliminar el conteo.",
                detalle = ex.GetBaseException().Message });
        }
        return Json(new { ok = true, mensaje = $"Se eliminó el conteo {modoKey} del artículo {articuloKey}.",
            localiza = localizaKey, codigoArticulo = articuloKey, valorAnterior = anterior,
            conteo1 = detalle.CONTEO1, conteo2 = detalle.CONTEO2, conteo3 = detalle.CONTEO3 });
    }

    private async Task<TomaValidacionResult> ObtenerTomaValidaAsync(
        int secuencia, string? modo, CancellationToken ct)
    {
        var modoKey = NormalizarModo(modo);
        if (modoKey == null)
            return TomaValidacionResult.Fallo("Modo de conteo inválido.");
        if (secuencia <= 0)
            return TomaValidacionResult.Fallo("La secuencia no es válida.");
        var codCia = ObtenerCodCia();
        var codSucursal = ObtenerCodSucursal();
        if (codCia.Length == 0 || codSucursal.Length == 0)
            return TomaValidacionResult.Fallo("No se encuentra configurada la compañía o sucursal del usuario.");
        var toma = await _context.INV_TOMAFIs.AsNoTracking().FirstOrDefaultAsync(x =>
            x.SECUENCIA == secuencia && x.COD_CIA != null && x.COD_CIA.Trim() == codCia, ct);
        if (toma == null)
            return TomaValidacionResult.Fallo($"No existe la toma física {secuencia} para la compañía {codCia}.");
        var codBodega = Normalizar(toma.COD_BODEGA);
        if (codBodega.Length == 0)
            return TomaValidacionResult.Fallo("La toma no tiene bodega definida.");
        var bodega = await _context.INV_BODEGAs.AsNoTracking().FirstOrDefaultAsync(x =>
            x.COD_CIA != null && x.COD_BODEGA != null && x.SUCURSAL != null &&
            x.COD_CIA.Trim() == codCia && x.COD_BODEGA.Trim() == codBodega &&
            x.SUCURSAL.Trim() == codSucursal, ct);
        if (bodega == null)
            return TomaValidacionResult.Fallo($"La bodega {codBodega} no pertenece a la sucursal {codSucursal}.");
        var estado = Normalizar(toma.ESTADO);
        var tipo = Normalizar(toma.TIPO);
        if (estado != "L")
            return TomaValidacionResult.Fallo($"La toma {secuencia} no está lista para conteo. Estado actual: {estado}.");
        if (tipo == "D")
            return TomaValidacionResult.Fallo("La toma tipo D (conteo dinámico) no puede modificarse desde esta pantalla.");
        if (modoKey == "2" && tipo != "2" && tipo != "3")
            return TomaValidacionResult.Fallo("Esta toma no admite Conteo 2.");
        if (modoKey == "3" && tipo != "3")
            return TomaValidacionResult.Fallo("Esta toma no admite Verificación.");
        return TomaValidacionResult.Exito(toma, bodega, modoKey);
    }

    private async Task<ValidacionSimpleResult> ValidarLocalizacionAsync(
        INV_TOMAFI toma, string localizaKey, CancellationToken ct)
    {
        if (localizaKey.Length == 0)
            return ValidacionSimpleResult.Fallo("Debe indicar la localización.");
        if (localizaKey == LotePredeterminado)
            return ValidacionSimpleResult.Fallo("La localización 001 no se procesa en esta pantalla.");
        var cia = Normalizar(toma.COD_CIA);
        var bodega = Normalizar(toma.COD_BODEGA);
        var existe = await _context.INV_LOCs.AsNoTracking().AnyAsync(x =>
            x.COD_CIA != null && x.COD_BODEGA != null && x.LOCALIZA != null &&
            x.COD_CIA.Trim() == cia && x.COD_BODEGA.Trim() == bodega &&
            x.LOCALIZA.Trim().ToUpper() == localizaKey, ct);
        return existe ? ValidacionSimpleResult.Exito() :
            ValidacionSimpleResult.Fallo($"La localización {localizaKey} no existe en la bodega {bodega}.");
    }

    private async Task<ArticuloResuelto?> ResolverArticuloAsync(
        string codCia, string codigoEntrada, CancellationToken ct)
    {
        var key = Normalizar(codigoEntrada);
        if (key.Length == 0) return null;
        var codigoPorBarra = await _context.INV_ARTIC_CODBARs.AsNoTracking()
            .Where(x => x.COD_CIA != null && x.COD_BARRAS != null &&
                x.COD_CIA.Trim() == codCia && x.COD_BARRAS.Trim() == codigoEntrada.Trim())
            .Select(x => x.COD_ARTICULO).FirstOrDefaultAsync(ct);
        var articuloKey = string.IsNullOrWhiteSpace(codigoPorBarra) ? key : Normalizar(codigoPorBarra);
        var articulo = await _context.INV_ARTICULOs.AsNoTracking()
            .Where(x => x.COD_ARTICULO != null && x.COD_ARTICULO.Trim().ToUpper() == articuloKey)
            .Select(x => new { x.COD_ARTICULO, x.DESCRIPCION, x.MEDIDA }).FirstOrDefaultAsync(ct);
        if (articulo == null) return null;
        var codigo = (articulo.COD_ARTICULO ?? "").Trim();
        return new ArticuloResuelto { Codigo = codigo, CodigoNormalizado = Normalizar(codigo),
            Descripcion = (articulo.DESCRIPCION ?? "").Trim(), Medida = (articulo.MEDIDA ?? "").Trim() };
    }

    private async Task<OrigenResult> ResolverOrigenAsync(string? origen, CancellationToken ct)
    {
        var key = (origen ?? "").Trim();
        if (key.Length == 0) return new OrigenResult(true, "", null, "");
        if (!byte.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero))
            return new OrigenResult(false, "El origen no es válido.", null, "");
        var registro = await _context.INV_ORIGENs.AsNoTracking()
            .Where(x => x.ORIGEN.HasValue && x.ORIGEN.Value == numero)
            .Select(x => new { x.ORIGEN, x.DESCRIPCION }).FirstOrDefaultAsync(ct);
        if (registro == null)
            return new OrigenResult(false, $"El origen {key} no existe.", null, "");
        return new OrigenResult(true, "", registro.ORIGEN, (registro.DESCRIPCION ?? "").Trim());
    }

    private async Task<BatchResult> ResolverBatchAsync(string batch, CancellationToken ct)
    {
        if (batch.Length == 0) return new BatchResult(true, "", null, null);
        if (batch.Length > 30)
            return new BatchResult(false, "El batch no puede superar 30 caracteres.", null, null);
        var orden = await _context.PNORDENEs.AsNoTracking()
            .Where(x => x.BATCH != null && x.BATCH.Trim().ToUpper() == batch)
            .Select(x => new { x.BATCH, x.FECHA }).FirstOrDefaultAsync(ct);
        return orden == null ? new BatchResult(false, $"El batch {batch} no existe.", null, null) :
            new BatchResult(true, "", (orden.BATCH ?? "").Trim(), orden.FECHA);
    }

    private CodigoInterpretadoResult InterpretarCodigoEscaneado(string? codigo)
    {
        var entrada = (codigo ?? "").Trim().Replace(',', '.');
        if (entrada.Length == 0)
            return CodigoInterpretadoResult.Fallo("Debe escanear o digitar el artículo.");
        var delimitador = _configuration["TomaFisica:DELIMITADOR"]?.Trim();
        if (string.IsNullOrWhiteSpace(delimitador)) delimitador = "%";
        var posicion = entrada.IndexOf(delimitador, StringComparison.Ordinal);
        if (posicion > 0)
        {
            var articulo = entrada[..posicion].Trim();
            var cantidadTexto = entrada[(posicion + delimitador.Length)..].Trim();
            if (!TryParseCantidad(cantidadTexto, out var cantidad))
                return CodigoInterpretadoResult.Fallo($"La cantidad incluida en el código ({cantidadTexto}) no es válida.");
            if (cantidad < 0)
                return CodigoInterpretadoResult.Fallo("La cantidad incluida en el código no puede ser negativa.");
            return CodigoInterpretadoResult.Exito(articulo, cantidad);
        }
        return CodigoInterpretadoResult.Exito(entrada, null);
    }

    // Conserva la lógica de ACTUALIZA_CONTEO del controlador original.
    private static void AplicarActualizaConteo(INV_DETTOMAFI detalle, string modo)
    {
        decimal cantidad;
        if (modo == "2")
        {
            cantidad = detalle.CONTEO2 ?? detalle.CONTEO1 ?? 0;
            detalle.CONTEO1 = cantidad;
            detalle.CONTEO2 = cantidad;
        }
        else if (modo == "3")
        {
            cantidad = detalle.CONTEO3 ?? detalle.CONTEO2 ?? 0;
            detalle.CONTEO2 = cantidad;
            detalle.CONTEO3 = cantidad;
        }
        else
        {
            cantidad = detalle.CONTEO1 ?? 0;
            detalle.CONTEO1 = cantidad;
        }
    }

    private string ObtenerCodCia() => User.FindFirst("CodCia")?.Value?.Trim()
        ?? _configuration["TomaFisica:COD_CIA"]?.Trim() ?? "";
    private string ObtenerCodSucursal() => User.FindFirst("CodSucursal")?.Value?.Trim()
        ?? _configuration["TomaFisica:COD_SUCURSAL"]?.Trim() ?? "";
    private static string? NormalizarModo(string? modo) => Normalizar(modo) is "1" or "2" or "3"
        ? Normalizar(modo) : null;
    private static string Normalizar(string? value) => (value ?? "").Trim().ToUpperInvariant();
    private static bool TryParseCantidad(string? valor, out decimal cantidad) =>
        decimal.TryParse((valor ?? "").Trim().Replace(',', '.'),
            NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out cantidad);

    private sealed class TomaValidacionResult
    {
        public bool Ok { get; private init; }
        public string Mensaje { get; private init; } = "";
        public INV_TOMAFI? Toma { get; private init; }
        public INV_BODEGA? Bodega { get; private init; }
        public string? Modo { get; private init; }
        public static TomaValidacionResult Fallo(string m) => new() { Ok = false, Mensaje = m };
        public static TomaValidacionResult Exito(INV_TOMAFI t, INV_BODEGA b, string m) =>
            new() { Ok = true, Toma = t, Bodega = b, Modo = m };
    }
    private sealed class ValidacionSimpleResult
    {
        public bool Ok { get; private init; }
        public string Mensaje { get; private init; } = "";
        public static ValidacionSimpleResult Fallo(string m) => new() { Mensaje = m };
        public static ValidacionSimpleResult Exito() => new() { Ok = true };
    }
    private sealed class CodigoInterpretadoResult
    {
        public bool Ok { get; private init; }
        public string Mensaje { get; private init; } = "";
        public string? Codigo { get; private init; }
        public decimal? Cantidad { get; private init; }
        public static CodigoInterpretadoResult Fallo(string m) => new() { Mensaje = m };
        public static CodigoInterpretadoResult Exito(string c, decimal? v) =>
            new() { Ok = true, Codigo = c, Cantidad = v };
    }
    private sealed class ArticuloResuelto
    {
        public string Codigo { get; init; } = "";
        public string CodigoNormalizado { get; init; } = "";
        public string Descripcion { get; init; } = "";
        public string Medida { get; init; } = "";
    }
    private sealed record OrigenResult(bool Ok, string Mensaje, byte? Origen, string Descripcion);
    private sealed record BatchResult(bool Ok, string Mensaje, string? Batch, DateTime? Fecha);
}
