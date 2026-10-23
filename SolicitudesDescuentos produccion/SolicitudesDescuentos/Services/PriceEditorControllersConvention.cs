using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;

namespace SolicitudesDescuentos.Services;

/// <summary>
/// Agrega la política de autorización PRICE_EDITOR a todos los endpoints
/// de reportes y seguimiento (pantallas, AJAX, PDF y exportaciones).
/// </summary>
public sealed class PriceEditorControllersConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            if (controller.ControllerName == "ReportesCobros" ||
                controller.ControllerName == "SeguimientoFacturas")
            {
                controller.Filters.Add(new AuthorizeFilter("CanEditPrice"));
            }
        }
    }
}
