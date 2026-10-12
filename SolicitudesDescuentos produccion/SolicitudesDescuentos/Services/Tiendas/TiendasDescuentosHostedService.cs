using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace SolicitudesDescuentos.Services.Tiendas;

public sealed class TiendasDescuentosHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TiendasDescuentosHostedService> _logger;
    private readonly TiendasDescuentosWorkerOptions _options;

    public TiendasDescuentosHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<TiendasDescuentosHostedService> logger,
        IOptions<TiendasDescuentosWorkerOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        if (!_options.Habilitado)
        {
            _logger.LogInformation(
                "[JOB TIENDAS] Worker deshabilitado.");

            return;
        }

        var intervalHours =
            _options.IntervalHours > 0
                ? _options.IntervalHours
                : 8;

        var intervalo =
            TimeSpan.FromHours(intervalHours);

        _logger.LogInformation(
            "[JOB TIENDAS] Worker iniciado. Intervalo={Intervalo}.",
            intervalo);

        while (!stoppingToken.IsCancellationRequested)
        {
            var reloj = Stopwatch.StartNew();

            try
            {
                using var jobLock =
                    CrossProcessJobLock.TryAcquire(
                        "TiendasDescuentosHostedService");

                if (!jobLock.Acquired)
                {
                    _logger.LogInformation(
                        "[JOB TIENDAS] Ciclo omitido: otra instancia ya está ejecutándose.");
                }
                else
                {
                    _logger.LogInformation(
                        "[JOB TIENDAS] ===== INICIO CICLO =====");

                    using var scope =
                        _scopeFactory.CreateScope();

                    var service =
                        scope.ServiceProvider
                            .GetRequiredService<ITiendasDescuentosService>();

                    var resultado =
                        await service.SincronizarAsync(
                            stoppingToken);

                    reloj.Stop();

                    if (!resultado.Ok)
                    {
                        _logger.LogWarning(
                            "[JOB TIENDAS] Finalizó con advertencia: {Mensaje}",
                            resultado.Mensaje);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[JOB TIENDAS] ===== FIN CICLO ===== Duración={DuracionMs} ms.",
                            reloj.ElapsedMilliseconds);
                    }
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
                    "[JOB TIENDAS] ERROR después de {DuracionMs} ms.",
                    reloj.ElapsedMilliseconds);
            }

            try
            {
                await Task.Delay(
                    intervalo,
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation(
            "[JOB TIENDAS] Worker detenido.");
    }
}
