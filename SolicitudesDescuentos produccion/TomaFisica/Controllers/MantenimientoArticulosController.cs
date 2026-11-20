using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TomaFisica.Data;
using TomaFisica.ModelsTomaFis;

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
        // Las listas nuevas son vistas de los artículos existentes.
        var tipos = await _context.INV_TIPOINVs.AsNoTracking()
            .Select(x => x.TIPOINVENTARIO)
            .ToListAsync(cancellationToken);
        var categorias = await _context.INV_CATEGORIAs.AsNoTracking()
            .Select(x => x.CATEGORIA)
            .ToListAsync(cancellationToken);
        var subcategorias = await _context.INV_SUBCATEGORIAs.AsNoTracking()
            .Select(x => new { x.CATEGORIA, x.SUBCATEGORIA })
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
                .Select(x => x!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x).ToList(),
            categorias = categorias.Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x).ToList(),
            subcategorias = subcategorias
                .Where(x => !string.IsNullOrWhiteSpace(x.CATEGORIA) &&
                            !string.IsNullOrWhiteSpace(x.SUBCATEGORIA))
                .Select(x => new { categoria = x.CATEGORIA!.Trim(), subcategoria = x.SUBCATEGORIA!.Trim() })
                .GroupBy(x => (x.categoria.ToUpperInvariant(), x.subcategoria.ToUpperInvariant()))
                .Select(g => g.First())
                .OrderBy(x => x.categoria).ThenBy(x => x.subcategoria).ToList(),
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
        var descripcion = (datos.Descripcion ?? "").Trim().ToUpperInvariant();
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
        byte? nuevaUnidadNegocio = null;
        if (unidadNegocio.Length > 0)
        {
            if (!byte.TryParse(unidadNegocio, NumberStyles.None, CultureInfo.InvariantCulture,
                    out var unidadNumerica) || unidadNumerica > 9)
                return BadRequest(new { ok = false,
                    mensaje = "La unidad de negocio debe ser un número entero de 0 a 9 o quedar en blanco." });
            nuevaUnidadNegocio = unidadNumerica;
        }
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
        if (!await _context.INV_TIPOINVs.AsNoTracking().AnyAsync(x =>
                x.TIPOINVENTARIO != null && x.TIPOINVENTARIO.Trim().ToUpper() == tipo, cancellationToken))
            return BadRequest(new { ok = false, mensaje = "El tipo de inventario no existe en INV_TIPOINV." });
        if (!await _context.INV_CATEGORIAs.AsNoTracking().AnyAsync(x =>
                x.CATEGORIA != null && x.CATEGORIA.Trim().ToUpper() == categoria.ToUpper(), cancellationToken))
            return BadRequest(new { ok = false, mensaje = "La categoría no existe en INV_CATEGORIA." });
        if (!await _context.INV_SUBCATEGORIAs.AsNoTracking().AnyAsync(x =>
                x.CATEGORIA != null && x.SUBCATEGORIA != null &&
                x.CATEGORIA.Trim().ToUpper() == categoria.ToUpper() &&
                x.SUBCATEGORIA.Trim().ToUpper() == subcategoria.ToUpper(), cancellationToken))
            return BadRequest(new { ok = false, mensaje = "La subcategoría no pertenece a la categoría seleccionada." });

        // Se obtiene la identidad de la sesión, nunca de datos enviados por el navegador.
        var codUsuario = User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(codUsuario))
            return Unauthorized(new { ok = false, mensaje = "No se pudo identificar el usuario autenticado." });
        var nombreUsuario = (User.FindFirst("DisplayName")?.Value ?? codUsuario).Trim();
        var codCia = User.FindFirst("CodCia")?.Value?.Trim();
        var codSucursal = User.FindFirst("CodSucursal")?.Value?.Trim();

        if (codUsuario.Length > 50 || nombreUsuario.Length > 100 ||
            codCia?.Length > 10 || codSucursal?.Length > 10)
            return BadRequest(new { ok = false, mensaje = "Los datos del usuario superan los límites de la auditoría." });

        var esNuevo = datos.EsNuevo;

        try
        {
            // La lectura, actualización y auditoría comparten una sola transacción.
            await using var transaccion = await _context.Database.BeginTransactionAsync(cancellationToken);

            // Para edición: bloquear la fila antes de leer los valores originales.
            // Para creación: el código es la llave única y se comprueba que no exista.
            if (!esNuevo)
            {
                await using var bloqueo = _context.Database.GetDbConnection().CreateCommand();
                bloqueo.Transaction = transaccion.GetDbTransaction();
                bloqueo.CommandText = @"SELECT COD_ARTICULO
FROM TOMAFIS.INV_ARTICULO
WHERE TRIM(UPPER(COD_ARTICULO)) = :codigo
FOR UPDATE";
                var parametro = bloqueo.CreateParameter();
                parametro.ParameterName = "codigo";
                parametro.Value = codigo;
                bloqueo.Parameters.Add(parametro);
                var codigoBloqueado = await bloqueo.ExecuteScalarAsync(cancellationToken);
                if (codigoBloqueado is null or DBNull)
                    return NotFound(new { ok = false, mensaje = "El artículo ya no existe." });
            }
            else if (await _context.INV_ARTICULOs.AsNoTracking().AnyAsync(x =>
                x.COD_ARTICULO != null && x.COD_ARTICULO.Trim().ToUpper() == codigo, cancellationToken))
            {
                return Conflict(new { ok = false, mensaje = "Ya existe un artículo con ese código." });
            }

            INV_ARTICULO? articulo = esNuevo ? null :
                await _context.INV_ARTICULOs.FirstOrDefaultAsync(
                    x => x.COD_ARTICULO != null && x.COD_ARTICULO.Trim().ToUpper() == codigo,
                    cancellationToken);
            if (!esNuevo && articulo == null)
                return NotFound(new { ok = false, mensaje = "El artículo ya no existe." });

            var cambios = new List<string>();
            void AgregarCambio(string campo, string? anterior, string? nuevo)
            {
                var antes = anterior?.Trim() ?? "";
                var despues = nuevo?.Trim() ?? "";
                if (string.Equals(antes, despues, StringComparison.Ordinal))
                    return;

                // Los saltos de línea y las comillas quedan escapados para
                // mantener un cambio por línea de descripción.
                static string Presentar(string valor) => valor.Length == 0
                    ? "(VACIO)"
                    : valor.Replace("'", "''").Replace("\r", "\\r").Replace("\n", "\\n");

                cambios.Add($"{campo}: '{Presentar(antes)}' -> '{Presentar(despues)}'");
            }

            static string Numero(decimal valor) =>
                valor.ToString("0.##", CultureInfo.InvariantCulture);
            static string Unidad(byte? valor) =>
                valor?.ToString(CultureInfo.InvariantCulture) ?? "";

            if (esNuevo)
                AgregarCambio("COD_ARTICULO", null, codigo);
            AgregarCambio("DESCRIPCION", articulo?.DESCRIPCION, descripcion);
            AgregarCambio("MEDIDA", articulo?.MEDIDA, medida);
            AgregarCambio("MULTIPLOCAJA", articulo == null ? null : Numero(articulo.MULTIPLOCAJA), Numero(multiploCaja));
            AgregarCambio("TIPOINVENTARIO", articulo?.TIPOINVENTARIO, tipo);
            AgregarCambio("CATEGORIA", articulo?.CATEGORIA, categoria);
            AgregarCambio("SUBCATEGORIA", articulo?.SUBCATEGORIA, subcategoria);
            AgregarCambio("UNIDADNEGOCIO", Unidad(articulo?.UNIDADNEGOCIO), Unidad(nuevaUnidadNegocio));
            AgregarCambio("ORIGEN", articulo?.ORIGEN?.ToString(CultureInfo.InvariantCulture),
                origen?.ToString(CultureInfo.InvariantCulture));
            AgregarCambio("REQUIEREBATCH", articulo?.REQUIEREBATCH, requiereBatch);

            if (cambios.Count == 0)
                return Json(new { ok = true, mensaje = "No se detectaron cambios en el artículo.", codigoArticulo = articulo!.COD_ARTICULO.Trim() });

            // El mapeo de ID_AUDITORIA usa ValueGeneratedNever: se asigna
            // NEXTVAL explícitamente. SYSDATE evita depender del reloj del servidor web.
            long idAuditoria;
            DateTime fechaCambio;
            await using (var consecutivo = _context.Database.GetDbConnection().CreateCommand())
            {
                consecutivo.Transaction = transaccion.GetDbTransaction();
                consecutivo.CommandText = "SELECT TO_CHAR(TOMAFIS.SEQ_AUD_INV_ARTICULO.NEXTVAL), SYSDATE FROM DUAL";
                await using var lector = await consecutivo.ExecuteReaderAsync(cancellationToken);
                if (!await lector.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("No se pudo obtener el consecutivo de auditoría.");

                idAuditoria = long.Parse(lector.GetString(0), CultureInfo.InvariantCulture);
                fechaCambio = lector.GetDateTime(1);
            }

            // Para crear se inserta una nueva entidad; en edición se conserva la llave.
            articulo ??= new INV_ARTICULO { COD_ARTICULO = codigo };
            if (esNuevo)
                _context.INV_ARTICULOs.Add(articulo);

            articulo.DESCRIPCION = descripcion;
            articulo.MEDIDA = medida;
            articulo.MULTIPLOCAJA = multiploCaja;
            articulo.TIPOINVENTARIO = tipo;
            articulo.CATEGORIA = categoria;
            articulo.SUBCATEGORIA = subcategoria;
            articulo.UNIDADNEGOCIO = nuevaUnidadNegocio;
            articulo.ORIGEN = origen;
            articulo.REQUIEREBATCH = requiereBatch;

            _context.AUD_INV_ARTICULOs.Add(new AUD_INV_ARTICULO
            {
                ID_AUDITORIA = idAuditoria,
                COD_ARTICULO = articulo.COD_ARTICULO.Trim(),
                COD_USUARIO = codUsuario,
                NOMBRE_USUARIO = nombreUsuario,
                COD_CIA = codCia,
                COD_SUCURSAL = codSucursal,
                FECHA_CAMBIO = fechaCambio,
                TIPO_ACCION = esNuevo ? "CREACION" : "MODIFICACION",
                DESCRIPCION = string.Join(Environment.NewLine, cambios)
            });

            await _context.SaveChangesAsync(cancellationToken);
            await transaccion.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Artículo {CodigoArticulo} {Accion} por {Usuario}. Auditoría {IdAuditoria}, campos: {Campos}",
                articulo.COD_ARTICULO.Trim(), esNuevo ? "creado" : "actualizado", codUsuario,
                idAuditoria, string.Join(", ", cambios.Select(x => x.Split(':')[0])));
            return Json(new { ok = true,
                mensaje = esNuevo ? "Artículo creado correctamente." : "Artículo actualizado correctamente.",
                codigoArticulo = articulo.COD_ARTICULO.Trim() });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo actualizar y auditar el artículo {CodigoArticulo}", codigo);
            return StatusCode(500, new { ok = false, mensaje = "No fue posible guardar el artículo y su auditoría. No se aplicaron cambios." });
        }
    }

    private static string Normalizar(string? value) => (value ?? "").Trim().ToUpperInvariant();

    public sealed class EditarArticuloRequest
    {
        public bool EsNuevo { get; set; }
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
