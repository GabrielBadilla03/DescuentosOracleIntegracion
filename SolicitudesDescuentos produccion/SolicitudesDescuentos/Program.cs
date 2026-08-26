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
    builder.Configuration["DescuentosSftp:PrivateKeyPath"] = ResolverRutaProyecto(privateKeyPath);
}

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<LancoDbContext>(options =>
{
    options.UseOracle(
        builder.Configuration.GetConnectionString("LANCO"),
        oracleOptions =>
        {
            oracleOptions.UseOracleSQLCompatibility("11");
        });
});

builder.Services.AddDbContext<OracleContext>(options =>
    options.UseOracle(builder.Configuration.GetConnectionString("Oracle")));

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

builder.Services.AddScoped<IArchivosDescuentosService, ArchivosDescuentosService>();
builder.Services.AddScoped<IDescuentosBatchService, DescuentosBatchService>();

builder.Services.AddHostedService<PredescuentosHostedService>();

builder.Services.AddSingleton<ISftpFingerprintProvider, SftpFingerprintProvider>();
builder.Services.AddHostedService<SftpFingerprintStartupService>();

/*
builder.Services.AddDbContext<DescuentosMasterContext>(options =>
    options.UseOracle(builder.Configuration.GetConnectionString("OracleDb")));
*/

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
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
    options.AddPolicy("CanEditPrice", policy =>
        policy.RequireAuthenticatedUser()
              .RequireRole("PRICE_EDITOR"));

    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
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

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Predescuentos}/{action=Index}/{id?}");

app.Run();