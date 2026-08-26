namespace SolicitudesDescuentos.ModelsOracle.ViewModels.Reportes
{
    public class ResumenCobrosAgenteFiltroVm
    {
        public string BuNombre { get; set; } = "LANCO_CR";

        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }

        public decimal TipoCambio { get; set; } = 1;

        public string? VendedorDesde { get; set; }
        public string? VendedorHasta { get; set; }

        public string? ClienteDesde { get; set; }
        public string? ClienteHasta { get; set; }

        public string? GrupoAgente { get; set; }

        // Los dejo por si después los ocupás, pero en la vista del resumen no hace falta mostrarlos.
        public string? Moneda { get; set; }
        public string? ChequeDevuelto { get; set; }
    }

    public class ResumenCobrosAgentePageVm
    {
        public ResumenCobrosAgenteFiltroVm Filtro { get; set; } = new();

        public List<string> GruposAgente { get; set; } = new();
    }

    public class ResumenCobrosAgenteFilaVm
    {
        public string GrupoCodigo { get; set; } = "";
        public string GrupoDescripcion { get; set; } = "";

        public string CodVendedor { get; set; } = "";
        public string NombreVendedor { get; set; } = "";

        public decimal Monto { get; set; }
        public decimal Descuento { get; set; }
        public decimal MontoFacturaSinImpuesto { get; set; }
        public decimal MontoComision { get; set; }
    }
}