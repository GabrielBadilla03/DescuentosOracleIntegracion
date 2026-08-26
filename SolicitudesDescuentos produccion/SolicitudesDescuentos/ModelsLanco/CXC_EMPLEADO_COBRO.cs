using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.Modelslanco
{
    public partial class CXC_EMPLEADO_COBRO
    {
        public string COD_CIA { get; set; } = null!;
        public string COD_AGENTE { get; set; } = null!;
        public string COD_CLIENTE { get; set; } = null!;
        public string EMPLEADO { get; set; } = null!;
        public short ANO_FISCAL { get; set; }
        public short PER_PROCESO { get; set; }
        public decimal PORCENTAJE { get; set; }
        public decimal COBROBRUTO { get; set; }
        public decimal MON_COBRADO { get; set; }
        public decimal MON_COMISION { get; set; }
    }
}
