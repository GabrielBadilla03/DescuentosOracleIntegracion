using System.Diagnostics;
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
                "[JOB DESCUENTOS] IntervalSeconds={IntervalSeconds} inválido. " +
                "Se utilizarán {DefaultSeconds} segundos.",
                intervalSeconds,
                IntervaloPredeterminadoSegundos);

            intervalSeconds = IntervaloPredeterminadoSegundos;
        }

        var intervalo = TimeSpan.FromSeconds(intervalSeconds);

        _logger.LogInformation(
            "[JOB DESCUENTOS] Worker iniciado. Intervalo={Intervalo}.",
            intervalo);

        while (!stoppingToken.IsCancellationRequested)
        {
            var reloj = Stopwatch.StartNew();

            try
            {
                using var jobLock =
                    CrossProcessJobLock.TryAcquire("PredescuentosHostedService");

                if (!jobLock.Acquired)
                {
                    _logger.LogInformation(
                        "[JOB DESCUENTOS] Ciclo omitido: otra instancia ya está ejecutándose.");
                }
                else
                {
                    _logger.LogInformation(
                        "[JOB DESCUENTOS] ===== INICIO CICLO =====");

                    using var scope = _scopeFactory.CreateScope();

                    var service = scope.ServiceProvider
                        .GetRequiredService<IDescuentosBatchService>();

                    await service.ProcesarPendientesAsync(stoppingToken);

                    reloj.Stop();

                    _logger.LogInformation(
                        "[JOB DESCUENTOS] ===== FIN CICLO ===== Duración={DuracionMs} ms.",
                        reloj.ElapsedMilliseconds);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                reloj.Stop();

                _logger.LogError(
                    ex,
                    "[JOB DESCUENTOS] ERROR GENERAL después de {DuracionMs} ms.",
                    reloj.ElapsedMilliseconds);
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

        _logger.LogInformation(
            "[JOB DESCUENTOS] Worker detenido.");
    }
}
