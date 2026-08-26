using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsOracle
{
    public partial class PREDETDESCUENTO
    {
        public string BU_NOMBRE { get; set; } = null!;
        public string COD_CLIENTE { get; set; } = null!;
        public string CONSECUTIVO { get; set; } = null!;
        public DateTime FECHASOLICITUD { get; set; }
        public string COD_LINEA { get; set; } = null!;
        public string? COD_ARTICULO { get; set; }
        public string TIPO { get; set; } = null!;
        public decimal VALOR { get; set; }
        public int CONSECUTIVODETALLE { get; set; }
        public string? COD_CLASE { get; set; }

        public virtual PREDESCUENTO PREDESCUENTO { get; set; } = null!;
    }
}
