using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using SolicitudesDescuentos.Data;
using SolicitudesDescuentos.ModelsOracle;
using SolicitudesDescuentos.Services;
using SolicitudesDescuentos.Services.Tiendas;

//dotnet publish .\DescuentosOracleWorker.csproj -c Release -f net6.0 -r win-x64 --self-contained true /p:PublishSingleFile=false -o "C:\Servicios\DescuentosOracleWorker_Publicado"

// Los archivos se guardan junto al ejecutable, tambien al correr como servicio Windows.
var logsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logsDirectory);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Error)
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logsDirectory, "worker-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        shared: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Iniciando DescuentosOracleWorker. Carpeta de logs: {LogsDirectory}", logsDirectory);
    await Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = "DescuentosOracleWorker")
    .UseSerilog()
    .ConfigureAppConfiguration((context, configuration) =>
    {
        // Configuración privada local no publicada en Git.
        // Variables de entorno y línea de comandos tienen prioridad.
        configuration.AddJsonFile("appsettings.Private.json", optional: true, reloadOnChange: true)
                     .AddEnvironmentVariables()
                     .AddCommandLine(args);
    })
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;
        var contentRoot = context.HostingEnvironment.ContentRootPath;

        string ResolverRuta(string? ruta)
        {
            if (string.IsNullOrWhiteSpace(ruta)) return string.Empty;
            ruta = ruta.Replace("{ContentRoot}", contentRoot);
            return Path.GetFullPath(Path.IsPathRooted(ruta)
                ? ruta : Path.Combine(contentRoot, ruta));
        }

        string ObtenerConexion(string nombre)
        {
            var cadena = configuration.GetConnectionString(nombre);
            if (string.IsNullOrWhiteSpace(cadena))
                throw new InvalidOperationException(
                    $"Falta ConnectionStrings:{nombre} en la configuración privada del Worker.");
            return cadena.Replace("{ContentRoot}", contentRoot);
        }

        var lanco = ObtenerConexion("LANCO");
        var oracle = ObtenerConexion("Oracle");
        var tiendas = ObtenerConexion("LANCOTIENDAS");

        foreach (var clave in new[] {
            "DescuentosSftp:PrivateKeyPath",
            "DescuentosSftp:FingerprintCacheFile",
            "DescuentosWorker:OutputFolder"
        })
        {
            var ruta = configuration[clave];
            if (!string.IsNullOrWhiteSpace(ruta))
                configuration[clave] = ResolverRuta(ruta);
        }

        services.AddDbContext<LancoDbContext>(options =>
            options.UseOracle(lanco,
                oracleOptions => oracleOptions.UseOracleSQLCompatibility("11")));
        services.AddDbContext<OracleContext>(options =>
            options.UseOracle(oracle));
        services.AddDbContext<LancoTiendasContext>(options =>
            options.UseOracle(tiendas));

        services.Configure<DescuentosWorkerOptions>(configuration.GetSection("DescuentosWorker"));
        services.Configure<DescuentosSftpOptions>(configuration.GetSection("DescuentosSftp"));
        services.Configure<TiendasDescuentosWorkerOptions>(configuration.GetSection("TiendasDescuentosWorker"));

        services.AddScoped<IArchivosDescuentosService, ArchivosDescuentosService>();
        services.AddScoped<IDescuentosBatchService, DescuentosBatchService>();
        services.AddScoped<ITiendasDescuentosService, TiendasDescuentosService>();
        services.AddSingleton<ISftpFingerprintProvider, SftpFingerprintProvider>();

        services.AddHostedService<SftpFingerprintStartupService>();
        services.AddHostedService<PredescuentosHostedService>();
        services.AddHostedService<TiendasDescuentosHostedService>();
    })
    .Build()
    .RunAsync();

}
catch (Exception ex)
{
    Log.Fatal(ex, "DescuentosOracleWorker finalizo por un error no controlado.");
    throw;
}
finally
{
    Log.Information("DescuentosOracleWorker detenido.");
    Log.CloseAndFlush();
}
