using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SolicitudesDescuentos.Models;

namespace SolicitudesDescuentos.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;

    public AccountController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Predescuentos");

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var username = model.CodUsuario?.Trim();
        var password = model.Password?.Trim();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ModelState.AddModelError("", "Debe ingresar usuario y contraseña.");
            return View(model);
        }

        try
        {
            var client = _httpClientFactory.CreateClient("BlancoAuth");

            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            var requestBody = new
            {
                username,
                password
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("validate-user/", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", "Usuario o contraseña incorrectos.");
                return View(model);
            }

            var loginResponse = JsonSerializer.Deserialize<LoginApiResponse>(
                responseContent,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (loginResponse is null || !loginResponse.Valid)
            {
                ModelState.AddModelError("", "Usuario o contraseña incorrectos.");
                return View(model);
            }

            var usernameFinal = string.IsNullOrWhiteSpace(loginResponse.Username)
                ? username
                : loginResponse.Username.Trim();

            var canEditPrice = loginResponse.CanEditPrice;

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, usernameFinal),
                new Claim(ClaimTypes.Name, usernameFinal),
                new Claim("Username", usernameFinal),
                new Claim("CanEditPrice", canEditPrice.ToString().ToLower())
            };

            claims.Add(new Claim(ClaimTypes.Role, "USER"));

            if (canEditPrice)
            {
                claims.Add(new Claim(ClaimTypes.Role, "PRICE_EDITOR"));
            }

            var claimsIdentity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);

            var authProperties = new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = true
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            return RedirectToAction("Index", "Predescuentos");
        }
        catch (HttpRequestException)
        {
            ModelState.AddModelError("", "No fue posible conectar con el servicio de autenticación.");
            return View(model);
        }
        catch (TaskCanceledException)
        {
            ModelState.AddModelError("", "El servicio de autenticación tardó demasiado en responder.");
            return View(model);
        }
        catch (JsonException)
        {
            ModelState.AddModelError("", "La respuesta del servicio de autenticación no tiene un formato válido.");
            return View(model);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", $"Ocurrió un error inesperado: {ex.Message}");
            return View(model);
        }
    }

    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    public IActionResult AccessDenied()
    {
        return View();
    }

    private sealed class LoginApiResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("can_edit_price")]
        public bool CanEditPrice { get; set; }
    }
}