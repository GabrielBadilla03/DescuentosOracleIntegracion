using Microsoft.Extensions.Options;
using SolicitudesDescuentos.ModelsOracle;

namespace SolicitudesDescuentos.Services;

public class PredescuentosHostedService : BackgroundService
{
    private const int IntervaloPredeterminadoSegundos = 120;

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
        var intervalSeconds = _options.IntervalSeconds;

        if (intervalSeconds <= 0)
        {
            _logger.LogWarning(
                "DescuentosWorker: IntervalSeconds={IntervalSeconds} es inválido. " +
                "Se utilizará el valor seguro de {DefaultSeconds} segundos.",
                intervalSeconds,
                IntervaloPredeterminadoSegundos);

            intervalSeconds = IntervaloPredeterminadoSegundos;
        }

        var intervalo = TimeSpan.FromSeconds(intervalSeconds);

        _logger.LogInformation(
            "PredescuentosHostedService iniciado. Intervalo={Intervalo}.",
            intervalo);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                /*
                 * Evita que durante un recycle de IIS o si el App Pool tiene
                 * más de un proceso se ejecuten dos ciclos de descuentos al
                 * mismo tiempo en el mismo servidor.
                 *
                 * Este lock NO modifica estados de negocio ni nomenclatura.
                 */
                using var jobLock =
                    CrossProcessJobLock.TryAcquire("PredescuentosHostedService");

                if (!jobLock.Acquired)
                {
                    _logger.LogInformation(
                        "Se omitió este ciclo de PredescuentosHostedService porque " +
                        "otra instancia del job ya se encuentra ejecutándose.");
                }
                else
                {
                    using var scope = _scopeFactory.CreateScope();

                    var service = scope.ServiceProvider
                        .GetRequiredService<IDescuentosBatchService>();

                    await service.ProcesarPendientesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando solicitudes pendientes.");
            }

            try
            {
                await Task.Delay(intervalo, stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("PredescuentosHostedService detenido.");
    }
}
