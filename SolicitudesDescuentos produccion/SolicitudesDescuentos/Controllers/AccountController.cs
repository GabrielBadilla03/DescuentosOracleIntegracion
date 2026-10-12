using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SolicitudesDescuentos.Data;
using SolicitudesDescuentos.Models;

namespace SolicitudesDescuentos.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
    private const string AppModeDescuentos = "DESCUENTOS";
    private const string AppModeTomaFisica = "TOMAFISICA";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LancoDbContext _lancoContext;
    private readonly IConfiguration _configuration;

    public AccountController(
        IHttpClientFactory httpClientFactory,
        LancoDbContext lancoContext,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _lancoContext = lancoContext;
        _configuration = configuration;
    }

    private bool DescuentosHabilitado =>
        _configuration.GetValue("Aplicaciones:DescuentosHabilitado", true);

    private bool TomaFisicaHabilitada =>
        _configuration.GetValue("Aplicaciones:TomaFisicaHabilitada", true);

    [HttpGet]
    public async Task<IActionResult> Login()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var appMode = User.FindFirst("AppMode")?.Value;

            if (string.Equals(
                    appMode,
                    AppModeDescuentos,
                    StringComparison.OrdinalIgnoreCase) &&
                DescuentosHabilitado)
            {
                return RedirectToAction("Index", "Predescuentos");
            }

            if (string.Equals(
                    appMode,
                    AppModeTomaFisica,
                    StringComparison.OrdinalIgnoreCase) &&
                TomaFisicaHabilitada)
            {
                return RedirectToAction("Menu", "TomaFisica");
            }

            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);
        }

        CargarDisponibilidadAplicaciones();
        return View();
    }

    [HttpGet]
    public IActionResult DescuentosLogin()
    {
        if (!DescuentosHabilitado)
        {
            TempData["LoginError"] =
                "La aplicación de descuentos está deshabilitada en esta instalación.";

            return RedirectToAction(nameof(Login));
        }

        if (User.Identity?.IsAuthenticated == true)
            return RedirigirSegunModoActual();

        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DescuentosLogin(LoginViewModel model)
    {
        if (!DescuentosHabilitado)
        {
            TempData["LoginError"] =
                "La aplicación de descuentos está deshabilitada en esta instalación.";

            return RedirectToAction(nameof(Login));
        }

        if (!ModelState.IsValid)
            return View(model);

        var username = model.CodUsuario?.Trim();
        var password = model.Password?.Trim();

        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            ModelState.AddModelError(
                "",
                "Debe ingresar usuario y contraseña.");

            return View(model);
        }

        try
        {
            var client =
                _httpClientFactory.CreateClient("BlancoAuth");

            client.DefaultRequestHeaders.Accept.Clear();

            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/json"));

            var requestBody = new
            {
                username,
                password
            };

            var json =
                JsonSerializer.Serialize(requestBody);

            using var content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json");

            var response =
                await client.PostAsync(
                    "validate-user/",
                    content);

            var responseContent =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError(
                    "",
                    "Usuario o contraseña incorrectos.");

                return View(model);
            }

            var loginResponse =
                JsonSerializer.Deserialize<LoginApiResponse>(
                    responseContent,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

            if (loginResponse is null ||
                !loginResponse.Valid)
            {
                ModelState.AddModelError(
                    "",
                    "Usuario o contraseña incorrectos.");

                return View(model);
            }

            var usernameFinal =
                string.IsNullOrWhiteSpace(loginResponse.Username)
                    ? username
                    : loginResponse.Username.Trim();

            var canEditPrice =
                loginResponse.CanEditPrice;

            var assignId =
                loginResponse.AssignId?.Trim();

            if (string.IsNullOrWhiteSpace(assignId))
            {
                ModelState.AddModelError(
                    "",
                    "El usuario no tiene un assign_id configurado para acceder al sistema.");

                return View(model);
            }

            var claims = new List<Claim>
            {
                new Claim(
                    ClaimTypes.NameIdentifier,
                    usernameFinal),

                new Claim(
                    ClaimTypes.Name,
                    usernameFinal),

                new Claim(
                    "Username",
                    usernameFinal),

                new Claim(
                    "CanEditPrice",
                    canEditPrice
                        .ToString()
                        .ToLowerInvariant()),

                new Claim(
                    "AssignId",
                    assignId),

                new Claim(
                    "AppMode",
                    AppModeDescuentos),

                new Claim(
                    ClaimTypes.Role,
                    "USER")
            };

            if (canEditPrice)
            {
                claims.Add(
                    new Claim(
                        ClaimTypes.Role,
                        "PRICE_EDITOR"));
            }

            var claimsIdentity =
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
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            return RedirectToAction(
                "Index",
                "Predescuentos");
        }
        catch (HttpRequestException)
        {
            ModelState.AddModelError(
                "",
                "No fue posible conectar con el servicio de autenticación.");

            return View(model);
        }
        catch (TaskCanceledException)
        {
            ModelState.AddModelError(
                "",
                "El servicio de autenticación tardó demasiado en responder.");

            return View(model);
        }
        catch (JsonException)
        {
            ModelState.AddModelError(
                "",
                "La respuesta del servicio de autenticación no tiene un formato válido.");

            return View(model);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(
                "",
                $"Ocurrió un error inesperado: {ex.Message}");

            return View(model);
        }
    }

    [HttpGet]
    public IActionResult TomaFisicaLogin()
    {
        if (!TomaFisicaHabilitada)
        {
            TempData["LoginError"] =
                "La aplicación de Toma Física está deshabilitada en esta instalación.";

            return RedirectToAction(nameof(Login));
        }

        if (User.Identity?.IsAuthenticated == true)
            return RedirigirSegunModoActual();

        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TomaFisicaLogin(
        LoginViewModel model)
    {
        if (!TomaFisicaHabilitada)
        {
            TempData["LoginError"] =
                "La aplicación de Toma Física está deshabilitada en esta instalación.";

            return RedirectToAction(nameof(Login));
        }

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
                "Falta configurar TomaFisica:COD_CIA o TomaFisica:COD_SUCURSAL.");

            return View(model);
        }

        var usernameKey =
            username.ToUpperInvariant();

        try
        {
            var usuario =
                await _lancoContext.SEG_USUARIOs
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

            var codigoUsuarioReal =
                usuario.COD_USUARIO.Trim();

            var grupos =
                await _lancoContext.SEG_GRUPOs
                    .AsNoTracking()
                    .Where(g =>
                        g.COD_USUARIOs.Any(u =>
                            u.COD_USUARIO ==
                            codigoUsuarioReal))
                    .Select(g =>
                        g.COD_GRUPO)
                    .ToListAsync();

            var claims =
                new List<Claim>
                {
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        codigoUsuarioReal),

                    new Claim(
                        ClaimTypes.Name,
                        codigoUsuarioReal),

                    new Claim(
                        "Username",
                        codigoUsuarioReal),

                    new Claim(
                        "DisplayName",
                        string.IsNullOrWhiteSpace(
                            usuario.DES_USUARIO)
                                ? codigoUsuarioReal
                                : usuario.DES_USUARIO.Trim()),

                    new Claim(
                        "AppMode",
                        AppModeTomaFisica),

                    new Claim(
                        "CodCia",
                        codCia),

                    new Claim(
                        "CodSucursal",
                        codSucursal)
                };

            foreach (var grupo in grupos
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase))
            {
                claims.Add(
                    new Claim(
                        "TomaFisicaGrupo",
                        grupo));
            }

            var claimsIdentity =
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
                new ClaimsPrincipal(claimsIdentity),
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

    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

        return RedirectToAction(
            nameof(Login));
    }

    public IActionResult AccessDenied()
    {
        return View();
    }

    private void CargarDisponibilidadAplicaciones()
    {
        ViewBag.DescuentosHabilitado =
            DescuentosHabilitado;

        ViewBag.TomaFisicaHabilitada =
            TomaFisicaHabilitada;
    }

    private IActionResult RedirigirSegunModoActual()
    {
        var appMode =
            User.FindFirst("AppMode")?.Value;

        if (string.Equals(
                appMode,
                AppModeTomaFisica,
                StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction(
                "Menu",
                "TomaFisica");
        }

        return RedirectToAction(
            "Index",
            "Predescuentos");
    }

    private sealed class LoginApiResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("can_edit_price")]
        public bool CanEditPrice { get; set; }

        [JsonPropertyName("assign_id")]
        public string? AssignId { get; set; }
    }
}
