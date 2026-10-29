namespace SolicitudesDescuentos.Services;

/// <summary>
/// Servicio de inicialización, no job recurrente. Se puede desactivar
/// independientemente sin deshabilitar las cargas SFTP del job de descuentos.
/// </summary>
public class SftpFingerprintStartupService : IHostedService
{
    private readonly ISftpFingerprintProvider _provider;
    private readonly ILogger<SftpFingerprintStartupService> _logger;
    private readonly IConfiguration _configuration;

    public SftpFingerprintStartupService(
        ISftpFingerprintProvider provider,
        ILogger<SftpFingerprintStartupService> logger,
        IConfiguration configuration)
    {
        _provider = provider;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_configuration.GetValue("SftpFingerprintStartup:Habilitado", true))
        {
            _logger.LogInformation("[SFTP] Inicialización de huella SSH deshabilitada.");
            return;
        }

        _logger.LogInformation("[SFTP] Inicializando huella SSH...");
        await _provider.GetFingerprintAsync(cancellationToken);
        _logger.LogInformation("[SFTP] Huella SSH lista.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
