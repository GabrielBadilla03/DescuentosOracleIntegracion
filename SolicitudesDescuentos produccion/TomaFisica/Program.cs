using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TomaFisica.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

/*
 * LANCO:
 * Solo se utiliza para autenticación/seguridad:
 * SEG_USUARIO
 * SEG_GRUPO
 * SEG_GRPUSUARIO
 */
builder.Services.AddDbContext<SeguridadDbContext>(options =>
{
    options.UseOracle(
        builder.Configuration.GetConnectionString("LANCO"),
        oracleOptions =>
        {
            oracleOptions.UseOracleSQLCompatibility("11");
        });
});

/*
 * TOMAFIS:
 * Base operativa de Toma Física:
 * INV_ARTICULO
 * INV_ARTIC_CODBAR
 * y futuras tablas de Toma Física.
 */
builder.Services.AddDbContext<TomaFisDbContext>(options =>
{
    options.UseOracle(
        builder.Configuration.GetConnectionString("TOMAFIS"),
        oracleOptions =>
        {
            oracleOptions.UseOracleSQLCompatibility("11");
        });
});

builder.Services
    .AddAuthentication(
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
    options.FallbackPolicy =
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
});

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
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
