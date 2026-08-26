namespace SolicitudesDescuentos.ModelsOracle.ViewModels.Reportes
{
    public class DetalleCobrosAgenteFilaVm
    {
        public string GrupoCodigo { get; set; } = "";
        public string GrupoDescripcion { get; set; } = "";

        public string CodVendedor { get; set; } = "";
        public string NombreVendedor { get; set; } = "";

        public string CodCliente { get; set; } = "";
        public string NombreCliente { get; set; } = "";

        public decimal PorcentajeComision { get; set; }

        public DateTime? FechaRecibo { get; set; }
        public string Recibo { get; set; } = "";

        public string TipoDocumento { get; set; } = "";

        public string Factura { get; set; } = "";
        public DateTime? FechaFactura { get; set; }

        public decimal Monto { get; set; }
        public decimal Descuento { get; set; }
        public decimal MontoFactura { get; set; }
        public decimal MontoComision { get; set; }
    }
}
