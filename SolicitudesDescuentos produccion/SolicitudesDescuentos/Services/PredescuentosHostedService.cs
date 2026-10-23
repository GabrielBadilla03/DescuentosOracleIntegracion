using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SolicitudesDescuentos.Data;
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

        // El procedimiento se ejecuta al finalizar CADA ciclo, incluso cuando
        // no se hayan generado archivos o la carga de descuentos haya fallado.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IDescuentosBatchService>();

                await service.ProcesarPendientesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error procesando solicitudes pendientes. Se intentará actualizar PREDESCLASEORACLE igualmente.");
            }

            if (stoppingToken.IsCancellationRequested)
                break;

            // Scope y OracleContext nuevos para no depender del estado de la
            // transaccion o del DbContext del procesamiento de archivos.
            try
            {
                using var scopeOracle = _scopeFactory.CreateScope();
                var oracleContext = scopeOracle.ServiceProvider.GetRequiredService<OracleContext>();

                _logger.LogInformation(
                    "Ejecutando BG_INTUSER.GENERAR_PREDESCLASEORACLE (ciclo de descuentos).");

                await oracleContext.Database.ExecuteSqlRawAsync(
                    "BEGIN BG_INTUSER.GENERAR_PREDESCLASEORACLE; END;",
                    stoppingToken);

                _logger.LogInformation(
                    "BG_INTUSER.GENERAR_PREDESCLASEORACLE finalizó correctamente.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Falló BG_INTUSER.GENERAR_PREDESCLASEORACLE. Se volverá a intentar en el siguiente ciclo.");
            }

            try
            {
                // Evita un bucle continuo si el intervalo está mal configurado.
                var intervalo = TimeSpan.FromSeconds(Math.Max(1, _options.IntervalSeconds));
                await Task.Delay(intervalo, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("PredescuentosHostedService detenido.");
    }
}
