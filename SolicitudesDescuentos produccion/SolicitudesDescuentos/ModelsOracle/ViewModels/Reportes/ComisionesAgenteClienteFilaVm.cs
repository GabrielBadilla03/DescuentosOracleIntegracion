namespace SolicitudesDescuentos.ModelsOracle.ViewModels.Reportes
{
    public class ComisionesAgenteClienteFilaVm
    {
        public string GrupoCodigo { get; set; } = "";
        public string GrupoDescripcion { get; set; } = "";

        public string CodVendedor { get; set; } = "";
        public string NombreVendedor { get; set; } = "";

        public string CodCliente { get; set; } = "";
        public string NombreCliente { get; set; } = "";

        public decimal Monto { get; set; }
        public decimal Descuento { get; set; }
        public decimal MontoFacturaSinImpuesto { get; set; }
        public decimal MontoComision { get; set; }
    }
}
