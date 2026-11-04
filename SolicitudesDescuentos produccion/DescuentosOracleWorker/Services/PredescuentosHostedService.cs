using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SolicitudesDescuentos.Data;
using SolicitudesDescuentos.ModelsOracle;

namespace SolicitudesDescuentos.Services;

public class PredescuentosHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PredescuentosHostedService> _logger;
    private readonly IOptionsMonitor<DescuentosWorkerOptions> _options;
    private const int CommandTimeoutSeconds = 900;

    public PredescuentosHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<PredescuentosHostedService> logger,
        IOptionsMonitor<DescuentosWorkerOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PredescuentosHostedService iniciado.");
        bool? estadoAnterior = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            // IOptionsMonitor lee el valor actualizado de appsettings.json.
            var configuracion = _options.CurrentValue;
            bool habilitado = configuracion.Habilitado;

            if (estadoAnterior != habilitado)
            {
                _logger.LogInformation(
                    habilitado
                        ? "[JOB DESCUENTOS] Habilitado: ZIP/SFTP y GENERAR_PREDESCLASEORACLE."
                        : "[JOB DESCUENTOS] Deshabilitado en appsettings.json: no se procesan descuentos ni se ejecuta el procedimiento.");
                estadoAnterior = habilitado;
            }

            if (habilitado)
            {
                try
                {
                    // El bloqueo cubre tanto la subida como la reconstrucción en Oracle.
                    using var jobLock = CrossProcessJobLock.TryAcquire("PredescuentosHostedService");
                    if (jobLock.Acquired)
                    {
                        await EjecutarCicloAsync(stoppingToken);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[JOB DESCUENTOS] Ciclo omitido: otra instancia está en ejecución.");
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[JOB DESCUENTOS] Error inesperado.");
                }
            }

            try
            {
                // Los segundos se mantienen tal como están configurados.
                await Task.Delay(
                    TimeSpan.FromSeconds(Math.Max(1, _options.CurrentValue.IntervalSeconds)),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("PredescuentosHostedService detenido.");
    }

    private async Task EjecutarCicloAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IDescuentosBatchService>();
            await service.ProcesarPendientesAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error procesando solicitudes pendientes. Se actualizará PREDESCLASEORACLE igualmente.");
        }

        stoppingToken.ThrowIfCancellationRequested();

        // Siempre ejecutar el procedimiento cuando este job está HABILITADO,
        // aunque no haya ZIP pendientes o falle SFTP.
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var oracleContext = scope.ServiceProvider.GetRequiredService<OracleContext>();
            oracleContext.Database.SetCommandTimeout(CommandTimeoutSeconds);

            _logger.LogInformation("Ejecutando BG_INTUSER.GENERAR_PREDESCLASEORACLE.");
            await oracleContext.Database.ExecuteSqlRawAsync(
                "BEGIN BG_INTUSER.GENERAR_PREDESCLASEORACLE; END;",
                stoppingToken);
            _logger.LogInformation("BG_INTUSER.GENERAR_PREDESCLASEORACLE finalizó correctamente.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Falló BG_INTUSER.GENERAR_PREDESCLASEORACLE. Se reintentará en el siguiente ciclo.");
        }
    }
}
