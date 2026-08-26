using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsOracle
{
    public partial class CXC_DETAGE_COBRO
    {
        public string COD_CIA { get; set; } = null!;
        public string SUCURSAL { get; set; } = null!;
        public short ANO_FISCAL { get; set; }
        public short PER_PROCESO { get; set; }
        public string COD_AGENTE { get; set; } = null!;
        public string COD_CLIENTE { get; set; } = null!;
        public string COD_COMISION { get; set; } = null!;
        public string DOCUMENTO { get; set; } = null!;
        public int LINEA { get; set; }
        public decimal MON_COBRADO { get; set; }
        public decimal MON_COMISION { get; set; }
        public decimal MONTO { get; set; }
        public string? NUM_DOC { get; set; }
        public string? TIP_DOC { get; set; }
        public decimal DESCUENTO { get; set; }
        public decimal IMPUESTO { get; set; }
        public string? FACTURA { get; set; }
        public DateTime? FECHAFACTURA { get; set; }
        public DateTime FECHADOC { get; set; }
        public string COD_MONEDA { get; set; } = null!;
    }
}
