namespace SolicitudesDescuentos.Services;

public class SftpFingerprintStartupService : IHostedService
{
    private readonly ISftpFingerprintProvider _provider;
    private readonly ILogger<SftpFingerprintStartupService> _logger;

    public SftpFingerprintStartupService(
        ISftpFingerprintProvider provider,
        ILogger<SftpFingerprintStartupService> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var fp = await _provider.GetFingerprintAsync(cancellationToken);
        _logger.LogInformation("Huella SSH inicializada al arrancar: {Fingerprint}", fp);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}