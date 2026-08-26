using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsOracle
{
    public partial class GENCLIENTEIMPULSADOR
    {
        public string? BU_NOMBRE { get; set; }
        public string CLIENTE { get; set; } = null!;
        public string EMPLEADO { get; set; } = null!;
        public decimal PORCENTAJE { get; set; }
        public string NOM_EMPLEADO { get; set; } = null!;
    }
}
