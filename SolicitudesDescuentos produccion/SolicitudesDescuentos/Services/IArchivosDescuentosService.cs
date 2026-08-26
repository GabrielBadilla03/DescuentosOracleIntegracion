namespace SolicitudesDescuentos.Services;

public interface IArchivosDescuentosService
{
    Task<ArchivoProcesoResult> IniciarFlujoItemAsync(string itemNumber, CancellationToken ct = default);

    Task<ArchivoProcesoResult> ReactivarFlujoItemAsync(
        string itemNumber,
        DateTime? startDate,
        DateTime? endDate,
        decimal descuento,
        CancellationToken ct = default);

    Task<ArchivoProcesoResult> DescargarExcelAsync(
        List<string> seleccionados,
        string tipoFiltro,
        bool marcarComoGenerado = true,
        bool forzarVencimientoDiaAnterior = false,
        CancellationToken ct = default);

    Task<ArchivoProcesoResult> DescargarExcelDesdeParesAsync(
        List<(string CodCia, string Consecutivo)> pares,
        string tipoFiltro,
        bool marcarComoGenerado = true,
        bool forzarVencimientoDiaAnterior = false,
        CancellationToken ct = default);

    Task<ArchivoProcesoResult> GenerarNoPromoPendienteAsync(
    string bu,
    string org,
    string itemNumber,
    bool marcarComoGenerado = false,
    CancellationToken ct = default);
}