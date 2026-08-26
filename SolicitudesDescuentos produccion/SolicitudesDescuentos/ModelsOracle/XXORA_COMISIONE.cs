using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsOracle
{
    public partial class XXORA_COMISIONE
    {
        public string? BU_NOMBRE { get; set; }
        public string? NUM_RECIBO { get; set; }
        public string? METODO_RECIBO { get; set; }
        public DateTime? FECHA_RECIBO { get; set; }
        public string? ESTATUS { get; set; }
        public string? VENDEDOR { get; set; }
        public string? ID_CLIENTE { get; set; }
        public string? NOMBRE_CLIENTE { get; set; }
        public decimal? TOTAL_RECIBO { get; set; }
        public decimal? PENDIENTE_APLICAR { get; set; }
        public string? MONEDA { get; set; }
        public string? NUM_TRX_APLICADA { get; set; }
        public DateTime? FECHA_APLICADA { get; set; }
        public decimal? CANTIDAD_APLICADA { get; set; }
        public decimal? DESCUENTO { get; set; }
        public string? CHEQUE_DEVUELTO { get; set; }
        public decimal? CANTIDAD_PENDIENTE { get; set; }
        public string? SITIO { get; set; }
        public decimal? MONTO_ORIGINAL_FACTURA { get; set; }
        public decimal? TOTAL_IMPUESTO_FACTURA { get; set; }
        public string? MONEDA_FACTURA { get; set; }
    }
}
