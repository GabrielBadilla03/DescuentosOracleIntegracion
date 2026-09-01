using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SolicitudesDescuentos.Data;
using SolicitudesDescuentos.Services;

namespace SolicitudesDescuentos.Controllers;

[Authorize]
public class ArchivosDescuentosController : Controller
{
    private readonly IArchivosDescuentosService _service;
    private readonly OracleContext _oracleContext;

    public ArchivosDescuentosController(
        IArchivosDescuentosService service,
        OracleContext oracleContext)
    {
        _service = service;
        _oracleContext = oracleContext;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IniciarFlujoItem(string itemNumber)
    {
        var result = await _service.IniciarFlujoItemAsync(itemNumber);

        if (!result.Ok)
        {
            TempData["InfoFlujo"] = result.Mensaje;
            return RedirectToAction("Index", "Predescuentos");
        }

        return File(
            result.ArchivoBytes!,
            result.ContentType,
            result.NombreArchivo);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReactivarFlujoItem(
        string itemNumber,
        DateTime? startDate,
        DateTime? endDate,
        decimal descuento)
    {
        var result = await _service.ReactivarFlujoItemAsync(
            itemNumber,
            startDate,
            endDate,
            descuento);

        if (!result.Ok)
        {
            TempData["InfoFlujo"] = result.Mensaje;
            return RedirectToAction("Index", "Predescuentos");
        }

        return File(
            result.ArchivoBytes!,
            result.ContentType,
            result.NombreArchivo);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DescargarExcel(
        List<string> seleccionados,
        string tipoFiltro)
    {
        /*
         * El ZIP se construye primero SIN modificar GENERADO.
         *
         * De esta forma, si la generación del archivo falla, la solicitud
         * sigue pendiente y no queda marcada como generada prematuramente.
         * La nomenclatura y el contenido generado por el servicio no cambian.
         */
        var result = await _service.DescargarExcelAsync(
            seleccionados,
            tipoFiltro,
            marcarComoGenerado: false,
            forzarVencimientoDiaAnterior: false);

        if (!result.Ok ||
            result.ArchivoBytes == null ||
            result.ArchivoBytes.Length == 0)
        {
            TempData["ErrorMessage"] = result.Ok
                ? "El archivo se generó sin contenido. La solicitud no fue marcada como generada."
                : result.Mensaje;

            return RedirectToAction("Index", "Predescuentos");
        }

        /*
         * Solo después de que el ZIP ya existe completamente en memoria se
         * confirma GENERADO='S'. Si esta actualización falla, no se devuelve
         * el archivo y la solicitud permanece disponible para reintento.
         */
        await MarcarSolicitudesComoGeneradasAsync(seleccionados);

        return File(
            result.ArchivoBytes,
            result.ContentType,
            result.NombreArchivo);
    }

    private async Task MarcarSolicitudesComoGeneradasAsync(
        IEnumerable<string>? seleccionados,
        CancellationToken cancellationToken = default)
    {
        static string T(string? value) => (value ?? string.Empty).Trim();

        static bool Eq(string? left, string? right) =>
            string.Equals(
                T(left),
                T(right),
                StringComparison.OrdinalIgnoreCase);

        var pares = new List<(string Bu, string Consecutivo)>();

        foreach (var seleccionado in seleccionados ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(seleccionado))
                continue;

            var parts = seleccionado.Split(
                '|',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

            if (parts.Length < 2)
                continue;

            var bu = T(parts[0]);
            var consecutivo = T(parts[1]);

            if (bu == string.Empty || consecutivo == string.Empty)
                continue;

            pares.Add((bu, consecutivo));
        }

        pares = pares
            .Distinct()
            .ToList();

        if (pares.Count == 0)
        {
            throw new InvalidOperationException(
                "No fue posible identificar las solicitudes que deben marcarse como generadas.");
        }

        var buSet = pares
            .Select(x => x.Bu.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var consecutivoSet = pares
            .Select(x => x.Consecutivo.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var candidatos = await _oracleContext.PREDESCUENTOs
            .Where(p =>
                p.BU_NOMBRE != null &&
                p.CONSECUTIVO != null &&
                buSet.Contains(p.BU_NOMBRE.Trim().ToUpper()) &&
                consecutivoSet.Contains(p.CONSECUTIVO.Trim().ToUpper()))
            .ToListAsync(cancellationToken);

        var encontrados = candidatos
            .Where(p => pares.Any(k =>
                Eq(p.BU_NOMBRE, k.Bu) &&
                Eq(p.CONSECUTIVO, k.Consecutivo)))
            .ToList();

        if (encontrados.Count == 0)
        {
            throw new InvalidOperationException(
                "El ZIP se generó, pero no se encontraron las solicitudes para confirmar GENERADO='S'.");
        }

        foreach (var solicitud in encontrados)
            solicitud.GENERADO = "S";

        await _oracleContext.SaveChangesAsync(cancellationToken);
    }
}
