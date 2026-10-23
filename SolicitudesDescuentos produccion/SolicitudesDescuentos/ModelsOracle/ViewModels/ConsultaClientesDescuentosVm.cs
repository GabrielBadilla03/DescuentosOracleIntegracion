using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsOracle.ViewModels
{
    public sealed class ConsultaClientesDescuentosVm
    {
        public string Cliente { get; set; } = "";
        public string NombreCliente { get; set; } = "";
        public string Tipo { get; set; } = "";
        public string Linea { get; set; } = "";
        public string Clase { get; set; } = "";
        public string Articulo { get; set; } = "";
        public int Pagina { get; set; } = 1;
        public int TamanoPagina { get; set; } = 200;
        public int Total { get; set; }
        public bool Consultado { get; set; }
        public bool IncluyeCobertura { get; set; }
        public string? Advertencia { get; set; }
        public List<ConsultaDescuentoFilaVm> Descuentos { get; set; } = new();
        public int TotalPaginas => Total == 0 ? 1 : (int)Math.Ceiling((double)Total / TamanoPagina);
    }

    public sealed class ConsultaDescuentoFilaVm
    {
        public string Tipo { get; set; } = "";
        public string Categoria { get; set; } = "";
        public string Subcategoria { get; set; } = "";
        public string Articulo { get; set; } = "";
        public string NombreLinea { get; set; } = "";
        public string NombreClase { get; set; } = "";
        public string NombreArticulo { get; set; } = "";
        public decimal Porcentaje { get; set; }
        public DateTime? Inicio { get; set; }
        public DateTime? Fin { get; set; }
        public string TipoMostrado => Tipo.Equals("CLIENTE", StringComparison.OrdinalIgnoreCase)
            ? "Descuento Fijo"
            : Tipo.Equals("PROMOCION", StringComparison.OrdinalIgnoreCase)
                ? "Descuento Promocional"
                : Tipo;
        public string Nivel => !string.IsNullOrWhiteSpace(Articulo) ? "Artículo" :
            !string.IsNullOrWhiteSpace(Subcategoria) ? "Clase" : "Línea";
    }
}
