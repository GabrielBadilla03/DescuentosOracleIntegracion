using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsOracle
{
    public partial class CXC_DET_COMISION
    {
        public string BU_NOMBRE { get; set; } = null!;
        public string COD_COMISION { get; set; } = null!;
        public decimal MONTO_COBRADO { get; set; }
        public decimal PORCENTAJE_COMISION { get; set; }
        public string LOCAL1 { get; set; } = null!;
        public string REPLICA1 { get; set; } = null!;
    }
}
