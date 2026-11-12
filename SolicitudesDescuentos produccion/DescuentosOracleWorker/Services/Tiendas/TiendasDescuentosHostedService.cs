using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace SolicitudesDescuentos.Services.Tiendas;

public sealed class TiendasDescuentosHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TiendasDescuentosHostedService> _logger;
    private readonly IOptionsMonitor<TiendasDescuentosWorkerOptions> _options;

    public TiendasDescuentosHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<TiendasDescuentosHostedService> logger,
        IOptionsMonitor<TiendasDescuentosWorkerOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[JOB TIENDAS] Worker iniciado.");
        bool? estadoAnterior = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            var configuracion = _options.CurrentValue;
            bool habilitado = configuracion.Habilitado;

            if (estadoAnterior != habilitado)
            {
                _logger.LogInformation(
                    habilitado
                        ? "[JOB TIENDAS] Habilitado en appsettings.json."
                        : "[JOB TIENDAS] Deshabilitado en appsettings.json: no se sincronizan descuentos con tiendas.");
                estadoAnterior = habilitado;
            }

            if (habilitado)
            {
                var reloj = Stopwatch.StartNew();

                try
                {
                    using var jobLock = CrossProcessJobLock.TryAcquire("TiendasDescuentosHostedService");
                    if (!jobLock.Acquired)
                    {
                        _logger.LogInformation(
                            "[JOB TIENDAS] Ciclo omitido: otra instancia ya está ejecutándose.");
                    }
                    else
                    {
                        _logger.LogInformation("[JOB TIENDAS] ===== INICIO CICLO =====");

                        using var scope = _scopeFactory.CreateScope();
                        var service = scope.ServiceProvider.GetRequiredService<ITiendasDescuentosService>();
                        var resultado = await service.SincronizarAsync(stoppingToken);

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
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    reloj.Stop();
                    _logger.LogError(ex,
                        "[JOB TIENDAS] ERROR después de {DuracionMs} ms.",
                        reloj.ElapsedMilliseconds);
                }
            }

            try
            {
                var horas = _options.CurrentValue.IntervalHours > 0
                    ? _options.CurrentValue.IntervalHours
                    : 8;
                await Task.Delay(TimeSpan.FromHours(horas), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("[JOB TIENDAS] Worker detenido.");
    }
}
