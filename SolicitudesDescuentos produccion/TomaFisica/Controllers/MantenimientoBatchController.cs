using System.Data.Common;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using TomaFisica.Data;

namespace TomaFisica.Controllers;

[Authorize]
public class MantenimientoBatchController : Controller
{
    private const int TamanoPagina = 30;
    private readonly TomaFisDbContext _context;
    private readonly ILogger<MantenimientoBatchController> _logger;

    public MantenimientoBatchController(
        TomaFisDbContext context, ILogger<MantenimientoBatchController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index() => View();

    // PRDBATCH está mapeada como HasNoKey. Se emplea ROWID únicamente para
    // identificar la fila física al editar, sin modificar tablas ni su mapeo.
    [HttpGet]
    public async Task<IActionResult> Buscar(
        string? buscar, int pagina = 1, CancellationToken cancellationToken = default)
    {
        var texto = (buscar ?? "").Trim();
        if (texto.Length > 100)
            return BadRequest(new { ok = false, mensaje = "La búsqueda no puede superar 100 caracteres." });

        pagina = Math.Clamp(pagina, 1, 1000);

        try
        {
            await _context.Database.OpenConnectionAsync(cancellationToken);
            var conexion = (OracleConnection)_context.Database.GetDbConnection();
            using var comando = conexion.CreateCommand();
            comando.BindByName = true;
            comando.CommandText = @"
SELECT FILAS.*
FROM (
    SELECT DATOS.*, ROWNUM NUM_FILA
    FROM (
        SELECT ROWIDTOCHAR(ROWID) AS ROW_ID, BATCH, ARTICULO, DESCRIPCION,
               PESO, FECHA, COSTOTOTAL, VOLUMENORDER, CANTIDAD
        FROM TOMAFIS.PRDBATCH
        WHERE (:sin_filtro = 1
            OR UPPER(BATCH) LIKE :filtro ESCAPE '\'
            OR UPPER(ARTICULO) LIKE :filtro ESCAPE '\'
            OR UPPER(DESCRIPCION) LIKE :filtro ESCAPE '\')
        ORDER BY BATCH DESC, ARTICULO, ROWID
    ) DATOS
    WHERE ROWNUM <= :hasta
) FILAS
WHERE FILAS.NUM_FILA > :desde";

            var filtrado = texto.Length > 0;
            Parametro(comando, "sin_filtro", OracleDbType.Int32, filtrado ? 0 : 1);
            var filtro = texto.ToUpperInvariant()
                .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            Parametro(comando, "filtro", OracleDbType.Varchar2, "%" + filtro + "%");
            Parametro(comando, "hasta", OracleDbType.Int32, pagina * TamanoPagina + 1);
            Parametro(comando, "desde", OracleDbType.Int32, (pagina - 1) * TamanoPagina);

            var registros = new List<object>();
            using (var lector = await comando.ExecuteReaderAsync(cancellationToken))
            {
                while (await lector.ReadAsync(cancellationToken))
                    registros.Add(LeerBatch(lector));
            }

            return Json(new
            {
                ok = true,
                pagina,
                hayMas = registros.Count > TamanoPagina,
                registros = registros.Take(TamanoPagina)
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error al buscar PRDBATCH.");
            return StatusCode(500, new { ok = false, mensaje = "No fue posible consultar los batches." });
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    [HttpGet]
    public async Task<IActionResult> Detalle(string? fila, CancellationToken cancellationToken)
    {
        if (!FilaValida(fila))
            return BadRequest(new { ok = false, mensaje = "Identificador de batch inválido." });

        try
        {
            await _context.Database.OpenConnectionAsync(cancellationToken);
            var conexion = (OracleConnection)_context.Database.GetDbConnection();
            using var comando = conexion.CreateCommand();
            comando.BindByName = true;
            comando.CommandText = @"
SELECT ROWIDTOCHAR(ROWID) AS ROW_ID, BATCH, ARTICULO, DESCRIPCION,
       PESO, FECHA, COSTOTOTAL, VOLUMENORDER, CANTIDAD
FROM TOMAFIS.PRDBATCH
WHERE ROWID = CHARTOROWID(:fila)";
            Parametro(comando, "fila", OracleDbType.Varchar2, fila!);

            using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken))
                return NotFound(new { ok = false, mensaje = "El registro de batch no existe." });
            return Json(new { ok = true, registro = LeerBatch(lector) });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error al consultar detalle de PRDBATCH.");
            return StatusCode(500, new { ok = false, mensaje = "No fue posible consultar el batch." });
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Guardar(
        [FromForm] BatchRequest datos, CancellationToken cancellationToken)
    {
        var nuevo = string.IsNullOrWhiteSpace(datos.Fila);
        var batch = Normalizar(datos.Batch);
        var articulo = Normalizar(datos.Articulo);
        var descripcion = (datos.Descripcion ?? "").Trim();

        if (!nuevo && !FilaValida(datos.Fila))
            return BadRequest(new { ok = false, mensaje = "Identificador del registro inválido." });
        if (batch.Length == 0 || batch.Length > 30)
            return BadRequest(new { ok = false, mensaje = "El batch es obligatorio (máximo 30 caracteres)." });
        if (articulo.Length == 0 || articulo.Length > 40)
            return BadRequest(new { ok = false, mensaje = "El artículo es obligatorio (máximo 40 caracteres)." });
        if (descripcion.Length > 200)
            return BadRequest(new { ok = false, mensaje = "La descripción no puede superar 200 caracteres." });

        if (!NumeroValido(datos.Peso, out var peso) ||
            !NumeroValido(datos.CostoTotal, out var costoTotal) ||
            !NumeroValido(datos.VolumenOrder, out var volumenOrder) ||
            !NumeroValido(datos.Cantidad, out var cantidad))
            return BadRequest(new { ok = false, mensaje = "Los valores numéricos deben ser positivos, con hasta 10 enteros y 4 decimales." });

        DateTime? fecha = null;
        if (!string.IsNullOrWhiteSpace(datos.Fecha))
        {
            if (!DateTime.TryParseExact(datos.Fecha.Trim(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var fechaLeida))
                return BadRequest(new { ok = false, mensaje = "La fecha no es válida." });
            fecha = fechaLeida;
        }

        try
        {
            await _context.Database.OpenConnectionAsync(cancellationToken);
            var conexion = (OracleConnection)_context.Database.GetDbConnection();
            using var transaccion = await conexion.BeginTransactionAsync(cancellationToken);

            // En ediciones se bloquea exactamente la fila elegida.
            if (!nuevo)
            {
                using var bloqueo = conexion.CreateCommand();
                bloqueo.BindByName = true;
                bloqueo.Transaction = (OracleTransaction)transaccion;
                bloqueo.CommandText = @"SELECT ROWIDTOCHAR(ROWID)
FROM TOMAFIS.PRDBATCH WHERE ROWID = CHARTOROWID(:fila) FOR UPDATE";
                Parametro(bloqueo, "fila", OracleDbType.Varchar2, datos.Fila!.Trim());
                var encontrado = await bloqueo.ExecuteScalarAsync(cancellationToken);
                if (encontrado == null || encontrado is DBNull)
                    return NotFound(new { ok = false, mensaje = "El registro de batch ya no existe." });
            }

            // No crear duplicados de la misma combinación de batch y artículo.
            using (var duplicado = conexion.CreateCommand())
            {
                duplicado.BindByName = true;
                duplicado.Transaction = (OracleTransaction)transaccion;
                duplicado.CommandText = nuevo
                    ? @"SELECT COUNT(*) FROM TOMAFIS.PRDBATCH
                        WHERE UPPER(TRIM(BATCH)) = :batch
                          AND UPPER(TRIM(ARTICULO)) = :articulo"
                    : @"SELECT COUNT(*) FROM TOMAFIS.PRDBATCH
                        WHERE UPPER(TRIM(BATCH)) = :batch
                          AND UPPER(TRIM(ARTICULO)) = :articulo
                          AND ROWID <> CHARTOROWID(:fila)";
                Parametro(duplicado, "batch", OracleDbType.Varchar2, batch);
                Parametro(duplicado, "articulo", OracleDbType.Varchar2, articulo);
                if (!nuevo) Parametro(duplicado, "fila", OracleDbType.Varchar2, datos.Fila!.Trim());
                var cantidadDuplicados = Convert.ToInt32(
                    await duplicado.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
                if (cantidadDuplicados > 0)
                    return Conflict(new { ok = false, mensaje = "Ya existe ese batch para el artículo indicado." });
            }

            // La relación con artículos es necesaria para utilizar el batch
            // durante la toma física; se valida tanto al insertar como al editar.
            using (var validarArticulo = conexion.CreateCommand())
            {
                validarArticulo.BindByName = true;
                validarArticulo.Transaction = (OracleTransaction)transaccion;
                validarArticulo.CommandText = @"SELECT COUNT(*)
FROM TOMAFIS.INV_ARTICULO
WHERE UPPER(TRIM(COD_ARTICULO)) = :articulo";
                Parametro(validarArticulo, "articulo", OracleDbType.Varchar2, articulo);
                var existeArticulo = Convert.ToInt32(
                    await validarArticulo.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
                if (!existeArticulo)
                    return BadRequest(new { ok = false, mensaje = "El artículo no existe en INV_ARTICULO." });
            }

            using (var guardar = conexion.CreateCommand())
            {
                guardar.BindByName = true;
                guardar.Transaction = (OracleTransaction)transaccion;
                guardar.CommandText = nuevo
                    ? @"INSERT INTO TOMAFIS.PRDBATCH
                        (BATCH, ARTICULO, DESCRIPCION, PESO, FECHA, COSTOTOTAL, VOLUMENORDER, CANTIDAD)
                        VALUES (:batch, :articulo, :descripcion, :peso, :fecha, :costo, :volumen, :cantidad)"
                    : @"UPDATE TOMAFIS.PRDBATCH
                        SET BATCH = :batch, ARTICULO = :articulo, DESCRIPCION = :descripcion,
                            PESO = :peso, FECHA = :fecha, COSTOTOTAL = :costo,
                            VOLUMENORDER = :volumen, CANTIDAD = :cantidad
                        WHERE ROWID = CHARTOROWID(:fila)";
                Parametro(guardar, "batch", OracleDbType.Varchar2, batch);
                Parametro(guardar, "articulo", OracleDbType.Varchar2, articulo);
                Parametro(guardar, "descripcion", OracleDbType.Varchar2,
                    descripcion.Length == 0 ? DBNull.Value : descripcion);
                Parametro(guardar, "peso", OracleDbType.Decimal, peso);
                Parametro(guardar, "fecha", OracleDbType.Date, fecha);
                Parametro(guardar, "costo", OracleDbType.Decimal, costoTotal);
                Parametro(guardar, "volumen", OracleDbType.Decimal, volumenOrder);
                Parametro(guardar, "cantidad", OracleDbType.Decimal, cantidad);
                if (!nuevo) Parametro(guardar, "fila", OracleDbType.Varchar2, datos.Fila!.Trim());

                if (await guardar.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("No se pudo guardar exactamente un registro de PRDBATCH.");
            }

            // PRDBATCH se relaciona con PNORDENES en la validación de conteos.
            // No se modifica PNORDENES desde este mantenimiento.
            bool existeEncabezado;
            using (var cabecera = conexion.CreateCommand())
            {
                cabecera.BindByName = true;
                cabecera.Transaction = (OracleTransaction)transaccion;
                cabecera.CommandText = @"SELECT COUNT(*) FROM TOMAFIS.PNORDENES
WHERE UPPER(TRIM(BATCH)) = :batch";
                Parametro(cabecera, "batch", OracleDbType.Varchar2, batch);
                existeEncabezado = Convert.ToInt32(
                    await cabecera.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
            }

            await transaccion.CommitAsync(cancellationToken);
            var usuario = User.Identity?.Name ?? "(desconocido)";
            _logger.LogInformation(
                "PRDBATCH {Accion}: batch {Batch}, artículo {Articulo}, usuario {Usuario}",
                nuevo ? "creado" : "actualizado", batch, articulo, usuario);
            return Json(new
            {
                ok = true,
                mensaje = nuevo ? "Batch agregado correctamente." : "Batch actualizado correctamente.",
                aviso = existeEncabezado
                    ? ""
                    : "Este batch todavía no existe en PNORDENES. Para utilizarlo en los conteos debe existir también ese encabezado."
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OracleException ex) when (ex.Number == 1)
        {
            _logger.LogWarning(ex, "Batch duplicado en PRDBATCH.");
            return Conflict(new { ok = false, mensaje = "El registro ya existe en la base de datos." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al guardar batch {Batch} para artículo {Articulo}", batch, articulo);
            return StatusCode(500, new { ok = false, mensaje = "No fue posible guardar el batch." });
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    private static void Parametro(OracleCommand comando, string nombre, OracleDbType tipo, object? valor)
    {
        var parametro = comando.Parameters.Add(nombre, tipo);
        parametro.Value = valor ?? DBNull.Value;
    }

    private static bool FilaValida(string? fila) =>
        !string.IsNullOrWhiteSpace(fila) &&
        fila.Length == 18 &&
        fila.All(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
            (c >= '0' && c <= '9') || c == '+' || c == '/');

    private static string Normalizar(string? valor) =>
        (valor ?? "").Trim().ToUpperInvariant();

    private static bool NumeroValido(string? entrada, out decimal? resultado)
    {
        resultado = null;
        if (string.IsNullOrWhiteSpace(entrada)) return true;
        var valor = entrada.Trim().Replace(',', '.');
        if (!decimal.TryParse(valor,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var numero))
            return false;
        if (numero < 0 || numero >= 10000000000m || decimal.Round(numero, 4) != numero)
            return false;
        resultado = numero;
        return true;
    }

    private static object LeerBatch(DbDataReader lector) => new
    {
        fila = lector.GetString(0),
        batch = Texto(lector, 1),
        articulo = Texto(lector, 2),
        descripcion = Texto(lector, 3),
        peso = Numero(lector, 4),
        fecha = lector.IsDBNull(5) ? null : lector.GetDateTime(5).ToString("yyyy-MM-dd"),
        costoTotal = Numero(lector, 6),
        volumenOrder = Numero(lector, 7),
        cantidad = Numero(lector, 8)
    };

    private static string? Texto(DbDataReader lector, int indice) =>
        lector.IsDBNull(indice) ? null : lector.GetString(indice).Trim();

    private static decimal? Numero(DbDataReader lector, int indice) =>
        lector.IsDBNull(indice) ? null : lector.GetDecimal(indice);

    public sealed class BatchRequest
    {
        public string? Fila { get; set; }
        public string? Batch { get; set; }
        public string? Articulo { get; set; }
        public string? Descripcion { get; set; }
        public string? Peso { get; set; }
        public string? Fecha { get; set; }
        public string? CostoTotal { get; set; }
        public string? VolumenOrder { get; set; }
        public string? Cantidad { get; set; }
    }
}
