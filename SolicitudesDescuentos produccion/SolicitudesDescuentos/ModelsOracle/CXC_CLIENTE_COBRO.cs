using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsOracle
{
    public partial class CXC_CLIENTE_COBRO
    {
        public string COD_CIA { get; set; } = null!;
        public string COD_AGENTE { get; set; } = null!;
        public string COD_CLIENTE { get; set; } = null!;
        public short ANO_FISCAL { get; set; }
        public short PER_PROCESO { get; set; }
        public decimal MON_COBRADO { get; set; }
        public decimal MON_COMISION { get; set; }
        public decimal COBROBRUTO { get; set; }
    }
}
