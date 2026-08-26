namespace SolicitudesDescuentos.Services
{
    public interface ISftpFingerprintProvider
    {
        string? CurrentFingerprint { get; }
        Task<string> GetFingerprintAsync(CancellationToken ct = default);
    }
}
