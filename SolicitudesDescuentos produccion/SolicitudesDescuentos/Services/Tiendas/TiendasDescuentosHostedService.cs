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
                "TiendasDescuentosHostedService está deshabilitado.");

            return;
        }

        var intervalHours = _options.IntervalHours > 0
            ? _options.IntervalHours
            : 8;

        var intervalo = TimeSpan.FromHours(intervalHours);

        _logger.LogInformation(
            "TiendasDescuentosHostedService iniciado. Intervalo={Intervalo}.",
            intervalo);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var service = scope.ServiceProvider
                    .GetRequiredService<ITiendasDescuentosService>();

                var resultado = await service.SincronizarAsync(
                    stoppingToken);

                if (!resultado.Ok)
                {
                    _logger.LogWarning(
                        "La sincronización de descuentos de tiendas no se completó: {Mensaje}",
                        resultado.Mensaje);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error sincronizando descuentos con INV_ARTIC_PROV.");
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
            "TiendasDescuentosHostedService detenido.");
    }
}