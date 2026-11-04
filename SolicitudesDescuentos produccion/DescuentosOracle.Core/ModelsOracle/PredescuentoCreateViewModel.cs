namespace SolicitudesDescuentos.ModelsOracle
{
    public class PredescuentoCreateViewModel
    {
        public string CodCliente { get; set; }
        public string CodCia { get; set; }
        public DateTime Fechasolicitud { get; set; }
        public string Tipodescuento { get; set; }
        public DateTime Fechainicio { get; set; }
        public DateTime Fechafin { get; set; }
        public string? Observaciones { get; set; }
        public string Estado { get; set; }
        public string? AutorizadoPor { get; set; }

        // Fix: Initialize the Detalles property to avoid null reference issues.  
        public List<PREDETDESCUENTO> Detalles { get; set; } = new List<PREDETDESCUENTO>();
    }
}
