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
        _logger.LogInformation(
            "[SFTP] Inicializando huella SSH...");

        await _provider.GetFingerprintAsync(cancellationToken);

        _logger.LogInformation(
            "[SFTP] Huella SSH lista.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
