using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using SolicitudesDescuentos.Data;
using SolicitudesDescuentos.Services;
using System.IO;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var contentRoot = builder.Environment.ContentRootPath;

// Resolver wallet Oracle
var oracleConnection = builder.Configuration.GetConnectionString("Oracle");
if (!string.IsNullOrWhiteSpace(oracleConnection))
{
    oracleConnection = oracleConnection.Replace("{ContentRoot}", contentRoot);
    builder.Configuration["ConnectionStrings:Oracle"] = oracleConnection;
}

// Las acciones de ReportesCobros y SeguimientoFacturas requieren PRICE_EDITOR,
// tanto en navegación como en accesos directos por URL, AJAX, Excel y PDF.
builder.Services.AddControllersWithViews(options =>
{
    options.Conventions.Add(new PriceEditorControllersConvention());
});

builder.Services.AddDbContext<LancoDbContext>(options =>
{
    options.UseOracle(
        builder.Configuration.GetConnectionString("LANCO"),
        oracleOptions => oracleOptions.UseOracleSQLCompatibility("11"));
});

builder.Services.AddDbContext<OracleContext>(options =>
    options.UseOracle(builder.Configuration.GetConnectionString("Oracle")));

builder.Services.AddDbContext<LancoTiendasContext>(options =>
    options.UseOracle(builder.Configuration.GetConnectionString("LANCOTIENDAS")));

// La web conserva la generación manual de archivos.
// Todos los procesos automáticos se hospedan solamente en DescuentosOracleWorker.
builder.Services.AddScoped<IArchivosDescuentosService, ArchivosDescuentosService>();
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
        policy.RequireAuthenticatedUser().RequireRole("PRICE_EDITOR"));

    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser().Build();
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
