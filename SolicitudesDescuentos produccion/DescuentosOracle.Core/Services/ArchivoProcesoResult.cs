namespace SolicitudesDescuentos.Services;

public sealed class ArchivoProcesoResult
{
    public bool Ok { get; init; }
    public string Mensaje { get; init; } = "";
    public byte[]? ArchivoBytes { get; init; }
    public string NombreArchivo { get; init; } = "Descuentos_COSTARICA_ALL.zip";
    public string ContentType { get; init; } = "application/zip";

    public static ArchivoProcesoResult Exito(byte[] bytes, string? nombreArchivo = null, string? contentType = null)
        => new()
        {
            Ok = true,
            ArchivoBytes = bytes,
            NombreArchivo = string.IsNullOrWhiteSpace(nombreArchivo)
                ? "Descuentos_COSTARICA_ALL.zip"
                : nombreArchivo.Trim(),
            ContentType = string.IsNullOrWhiteSpace(contentType)
                ? "application/zip"
                : contentType.Trim()
        };

    public static ArchivoProcesoResult Fallo(string mensaje)
        => new()
        {
            Ok = false,
            Mensaje = mensaje ?? "Ocurrió un error."
        };
}