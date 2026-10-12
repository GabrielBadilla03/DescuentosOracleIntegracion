using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using SolicitudesDescuentos.Data;
using SolicitudesDescuentos.ModelsOracle;
using SolicitudesDescuentos.Services;
using SolicitudesDescuentos.Services.Tiendas;
using System.IO;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var contentRoot = builder.Environment.ContentRootPath;

var descuentosHabilitado =
    builder.Configuration.GetValue(
        "Aplicaciones:DescuentosHabilitado",
        true);

var tomaFisicaHabilitada =
    builder.Configuration.GetValue(
        "Aplicaciones:TomaFisicaHabilitada",
        true);

string ResolverRutaProyecto(string? ruta)
{
    if (string.IsNullOrWhiteSpace(ruta))
        return "";

    ruta = ruta.Replace("{ContentRoot}", contentRoot);

    if (Path.IsPathRooted(ruta))
        return Path.GetFullPath(ruta);

    return Path.GetFullPath(Path.Combine(contentRoot, ruta));
}

// Resolver wallet Oracle
var oracleConnection = builder.Configuration.GetConnectionString("Oracle");

if (!string.IsNullOrWhiteSpace(oracleConnection))
{
    oracleConnection = oracleConnection.Replace("{ContentRoot}", contentRoot);
    builder.Configuration["ConnectionStrings:Oracle"] = oracleConnection;
}

// Resolver llave privada SFTP
var privateKeyPath = builder.Configuration["DescuentosSftp:PrivateKeyPath"];

if (!string.IsNullOrWhiteSpace(privateKeyPath))
{
    builder.Configuration["DescuentosSftp:PrivateKeyPath"] =
        ResolverRutaProyecto(privateKeyPath);
}

// Add services to the container.
builder.Services.AddControllersWithViews();

/*
 * LANCO se registra siempre:
 * - Descuentos usa sus tablas Navius.
 * - Toma Física usa SEG_USUARIO / SEG_GRUPO / SEG_GRPUSUARIO.
 *
 * Que el contexto tenga otros modelos NO obliga a que todas esas
 * tablas existan mientras no sean consultadas.
 */
builder.Services.AddDbContext<LancoDbContext>(options =>
{
    options.UseOracle(
        builder.Configuration.GetConnectionString("LANCO"),
        oracleOptions =>
        {
            oracleOptions.UseOracleSQLCompatibility("11");
        });
});

/*
 * Todo lo que es exclusivo de Descuentos se registra solamente
 * cuando la aplicación está habilitada.
 *
 * Esto evita que una instalación "solo Toma Física" arranque workers
 * que intenten consultar PLAEMPLEADO, CXC_*, etc.
 */
if (descuentosHabilitado)
{
    builder.Services.AddDbContext<OracleContext>(options =>
        options.UseOracle(
            builder.Configuration.GetConnectionString("Oracle")));

    builder.Services.AddDbContext<LancoTiendasContext>(options =>
        options.UseOracle(
            builder.Configuration.GetConnectionString("LANCOTIENDAS")));

    builder.Services.Configure<TiendasDescuentosWorkerOptions>(
        builder.Configuration.GetSection("TiendasDescuentosWorker"));

    builder.Services.AddScoped<
        ITiendasDescuentosService,
        TiendasDescuentosService>();

    builder.Services.AddHostedService<
        TiendasDescuentosHostedService>();

    builder.Services.Configure<DescuentosWorkerOptions>(
        builder.Configuration.GetSection("DescuentosWorker"));

    builder.Services.Configure<DescuentosSftpOptions>(
        builder.Configuration.GetSection("DescuentosSftp"));

    builder.Services.AddScoped<
        IArchivosDescuentosService,
        ArchivosDescuentosService>();

    builder.Services.AddScoped<
        IDescuentosBatchService,
        DescuentosBatchService>();

    builder.Services.AddHostedService<
        PredescuentosHostedService>();

    builder.Services.AddSingleton<
        ISftpFingerprintProvider,
        SftpFingerprintProvider>();

    builder.Services.AddHostedService<
        SftpFingerprintStartupService>();
}

/*
 * TOMAFIS se usa solamente para la funcionalidad de Toma Física.
 * El login de Toma Física NO usa esta conexión: usa LANCO.
 */
if (tomaFisicaHabilitada)
{
    builder.Services.AddDbContext<TomaFisDbContext>(options =>
    {
        options.UseOracle(
            builder.Configuration.GetConnectionString("TOMAFIS"),
            oracleOptions =>
            {
                oracleOptions.UseOracleSQLCompatibility("11");
            });
    });
}

builder.Services.AddAuthentication(
        CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";

        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;

        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

builder.Services.AddAuthorization(options =>
{
    /*
     * Toma Física tiene su propia política explícita.
     */
    options.AddPolicy("TomaFisicaApp", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("AppMode", "TOMAFISICA"));

    /*
     * Se conserva la política de edición de precios, pero solo para
     * sesiones autenticadas en Descuentos.
     */
    options.AddPolicy("CanEditPrice", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("AppMode", "DESCUENTOS")
              .RequireRole("PRICE_EDITOR"));

    /*
     * Toda la aplicación existente queda, por defecto, reservada
     * a una sesión de Descuentos.
     *
     * TomaFisicaController llevará una política explícita diferente.
     */
    var descuentosPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim("AppMode", "DESCUENTOS")
        .Build();

    options.DefaultPolicy = descuentosPolicy;
    options.FallbackPolicy = descuentosPolicy;
});

builder.Services.AddHttpClient("BlancoAuth", client =>
{
    client.BaseAddress = new Uri("https://sales.blanco.group");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.Configure<FormOptions>(options =>
{
    options.ValueCountLimit = 5000;
});

builder.Services.AddMemoryCache();

QuestPDF.Settings.License = LicenseType.Community;

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

/*
 * El inicio pasa por Account/Login:
 * - sin sesión: selector Descuentos / Toma Física
 * - sesión Descuentos: redirige a Predescuentos
 * - sesión Toma Física: redirige a TomaFisica/Menu
 */
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
