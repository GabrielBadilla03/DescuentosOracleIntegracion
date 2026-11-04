using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SolicitudesDescuentos.Data;
using SolicitudesDescuentos.ModelsOracle;
using SolicitudesDescuentos.Services;
using SolicitudesDescuentos.Services.Tiendas;

await Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = "DescuentosOracleWorker")
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
