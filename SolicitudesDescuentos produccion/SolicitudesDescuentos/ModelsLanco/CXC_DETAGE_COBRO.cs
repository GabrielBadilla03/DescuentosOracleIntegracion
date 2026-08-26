using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.Modelslanco
{
    public partial class CXC_DETAGE_COBRO
    {
        public string COD_CIA { get; set; } = null!;
        public string SUCURSAL { get; set; } = null!;
        public string LINEA { get; set; } = null!;
        public string COD_MONEDA { get; set; } = null!;
        public string? TIP_DOC { get; set; }
        public string? NUM_DOC { get; set; }
        public string? COD_CLIENTE { get; set; }
        public string? DOCUMENTO { get; set; }
        public string? FACTURA { get; set; }
        public DateTime? FECHAFACTURA { get; set; }
        public decimal MONTO { get; set; }
        public decimal MONTOFACTURA { get; set; }
        public string COD_AGENTE { get; set; } = null!;
        public short ANO_FISCAL { get; set; }
        public short PER_PROCESO { get; set; }
        public string COD_COMISION { get; set; } = null!;
        public decimal MON_COBRADO { get; set; }
        public decimal MON_COMISION { get; set; }
        public decimal? IMPUESTO { get; set; }
        public decimal? DESCUENTO { get; set; }
        public DateTime? FECHADOC { get; set; }
    }
}
