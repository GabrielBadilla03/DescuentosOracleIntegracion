using Microsoft.Extensions.Options;
using SolicitudesDescuentos.ModelsOracle;

namespace SolicitudesDescuentos.Services;

public class PredescuentosHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PredescuentosHostedService> _logger;
    private readonly DescuentosWorkerOptions _options;

    public PredescuentosHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<PredescuentosHostedService> logger,
        IOptions<DescuentosWorkerOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PredescuentosHostedService iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IDescuentosBatchService>();

                await service.ProcesarPendientesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando solicitudes pendientes.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), stoppingToken);
        }
    }
}