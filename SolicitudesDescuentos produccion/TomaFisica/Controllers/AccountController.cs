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

        /*
         * Tanto usuario como contraseña se normalizan:
         *
         * TRIM
         * +
         * MAYÚSCULAS
         *
         * De esta manera:
         *
         * adelgado  = ADELGADO
         * Adelgado  = ADELGADO
         * ADELGADO  = ADELGADO
         */
        var usernameKey =
            model.CodUsuario?
                .Trim()
                .ToUpperInvariant();

        var passwordKey =
            model.Password?
                .Trim()
                .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(usernameKey) ||
            string.IsNullOrWhiteSpace(passwordKey))
        {
            ModelState.AddModelError(
                "",
                "Debe ingresar usuario y contraseña.");

            return View(model);
        }

        /*
         * También normalizamos la configuración.
         * En este momento son valores numéricos como 001,
         * pero así queda consistente.
         */
        var codCiaKey =
            _configuration[
                "TomaFisica:COD_CIA"]?
                .Trim()
                .ToUpperInvariant();

        var codSucursalKey =
            _configuration[
                "TomaFisica:COD_SUCURSAL"]?
                .Trim()
                .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(codCiaKey) ||
            string.IsNullOrWhiteSpace(codSucursalKey))
        {
            ModelState.AddModelError(
                "",
                "Falta configurar COD_CIA o COD_SUCURSAL para Toma Física.");

            return View(model);
        }

        try
        {
            /*
             * VALIDACIÓN DEL LOGIN
             *
             * Todos los campos se comparan normalizados:
             *
             * Usuario ingresado
             *      =
             * UPPER(TRIM(SEG_USUARIO.COD_USUARIO))
             *
             * Contraseña ingresada
             *      =
             * UPPER(TRIM(SEG_USUARIO.PASSWORD))
             *
             * COD_CIA appsettings
             *      =
             * UPPER(TRIM(SEG_USUARIO.COD_CIA))
             *
             * COD_SUCURSAL appsettings
             *      =
             * UPPER(TRIM(SEG_USUARIO.SUCURSAL))
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
                            .Trim()
                            .ToUpper() == passwordKey &&

                        x.COD_CIA
                            .Trim()
                            .ToUpper() == codCiaKey &&

                        x.SUCURSAL
                            .Trim()
                            .ToUpper() == codSucursalKey)
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

            /*
             * El usuario que queda guardado en la sesión
             * también queda siempre en mayúsculas.
             */
            var codigoUsuario =
                usuario.COD_USUARIO
                    .Trim()
                    .ToUpperInvariant();

            /*
             * Se utiliza el mismo valor normalizado
             * para buscar sus grupos.
             */
            var codigoUsuarioKey =
                codigoUsuario;

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
                        .ToUpper() ==
                        codigoUsuarioKey

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
                        codCiaKey),

                    new Claim(
                        "CodSucursal",
                        codSucursalKey),

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
                    CookieAuthenticationDefaults
                        .AuthenticationScheme);

            var authProperties =
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    AllowRefresh = true
                };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults
                    .AuthenticationScheme,
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
            CookieAuthenticationDefaults
                .AuthenticationScheme);

        return RedirectToAction(
            nameof(Login));
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }
}