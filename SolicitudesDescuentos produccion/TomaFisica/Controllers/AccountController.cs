using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TomaFisica.Data;
using TomaFisica.Models;

namespace TomaFisica.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
    private readonly SeguridadDbContext _seguridadContext;
    private readonly IConfiguration _configuration;

    public AccountController(
        SeguridadDbContext seguridadContext,
        IConfiguration configuration)
    {
        _seguridadContext = seguridadContext;
        _configuration = configuration;
    }

    [HttpGet]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction(
                "Menu",
                "TomaFisica");
        }

        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var username =
            model.CodUsuario?.Trim();

        var password =
            model.Password?.Trim();

        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            ModelState.AddModelError(
                "",
                "Debe ingresar usuario y contraseña.");

            return View(model);
        }

        var codCia =
            _configuration[
                "TomaFisica:COD_CIA"]?.Trim();

        var codSucursal =
            _configuration[
                "TomaFisica:COD_SUCURSAL"]?.Trim();

        if (string.IsNullOrWhiteSpace(codCia) ||
            string.IsNullOrWhiteSpace(codSucursal))
        {
            ModelState.AddModelError(
                "",
                "Falta configurar COD_CIA o COD_SUCURSAL para Toma Física.");

            return View(model);
        }

        var usernameKey =
            username.ToUpperInvariant();

        try
        {
            /*
             * Valida los 4 datos solicitados:
             *
             * COD_USUARIO = usuario digitado
             * PASSWORD    = contraseña digitada
             * COD_CIA     = appsettings
             * SUCURSAL    = appsettings
             */
            var usuario =
                await _seguridadContext
                    .SEG_USUARIOs
                    .AsNoTracking()
                    .Where(x =>
                        x.COD_USUARIO != null &&
                        x.PASSWORD != null &&
                        x.COD_CIA != null &&
                        x.SUCURSAL != null &&
                        x.COD_USUARIO
                            .Trim()
                            .ToUpper() == usernameKey &&
                        x.PASSWORD
                            .Trim() == password &&
                        x.COD_CIA
                            .Trim() == codCia &&
                        x.SUCURSAL
                            .Trim() == codSucursal)
                    .Select(x => new
                    {
                        x.COD_USUARIO,
                        x.DES_USUARIO
                    })
                    .FirstOrDefaultAsync();

            if (usuario == null)
            {
                ModelState.AddModelError(
                    "",
                    "Usuario o contraseña incorrectos para la compañía y sucursal configuradas.");

                return View(model);
            }

            var codigoUsuario =
                usuario.COD_USUARIO.Trim();

            var codigoUsuarioKey =
                codigoUsuario.ToUpperInvariant();

            /*
             * Carga grupos para dejarlos disponibles
             * para futuros permisos por opción.
             */
            var grupos =
                await (
                    from gu in _seguridadContext
                        .SEG_GRPUSUARIOs
                        .AsNoTracking()
                    join g in _seguridadContext
                        .SEG_GRUPOs
                        .AsNoTracking()
                        on gu.COD_GRUPO
                        equals g.COD_GRUPO
                    where gu.COD_USUARIO
                        .Trim()
                        .ToUpper() == codigoUsuarioKey
                    select g.COD_GRUPO
                )
                .Distinct()
                .ToListAsync();

            var claims =
                new List<Claim>
                {
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        codigoUsuario),

                    new Claim(
                        ClaimTypes.Name,
                        codigoUsuario),

                    new Claim(
                        "Username",
                        codigoUsuario),

                    new Claim(
                        "DisplayName",
                        string.IsNullOrWhiteSpace(
                            usuario.DES_USUARIO)
                                ? codigoUsuario
                                : usuario.DES_USUARIO.Trim()),

                    new Claim(
                        "CodCia",
                        codCia),

                    new Claim(
                        "CodSucursal",
                        codSucursal),

                    new Claim(
                        ClaimTypes.Role,
                        "TOMAFISICA_USER")
                };

            foreach (var grupo in grupos
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x))
                .Select(x =>
                    x.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase))
            {
                claims.Add(
                    new Claim(
                        "TomaFisicaGrupo",
                        grupo));
            }

            var identity =
                new ClaimsIdentity(
                    claims,
                    CookieAuthenticationDefaults.AuthenticationScheme);

            var authProperties =
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    AllowRefresh = true
                };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                authProperties);

            return RedirectToAction(
                "Menu",
                "TomaFisica");
        }
        catch (Exception)
        {
            ModelState.AddModelError(
                "",
                "No fue posible validar el usuario de Toma Física en este momento.");

            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }
}
