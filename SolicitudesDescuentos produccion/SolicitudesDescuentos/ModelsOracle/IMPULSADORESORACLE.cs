using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsOracle
{
    public partial class IMPULSADORESORACLE
    {
        public string BU_NOMBRE { get; set; } = null!;
        public string CLIENTE { get; set; } = null!;
        public string EMPLEADO { get; set; } = null!;
        public decimal PORCENTAJE { get; set; }
    }
}
