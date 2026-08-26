using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsOracle
{
    public partial class GEN_MAS_COMISION
    {
        public string BU_NOMBRE { get; set; } = null!;
        public string COD_COMISION { get; set; } = null!;
        public string TIPO_COMISION { get; set; } = null!;
        public string VALOR { get; set; } = null!;
        public string TIPO_CALCULO { get; set; } = null!;
        public string PROPORCIONAL { get; set; } = null!;
        public string DES_COMISION { get; set; } = null!;
        public string COD_MONEDA { get; set; } = null!;
        public string LOCAL1 { get; set; } = null!;
        public string REPLICA1 { get; set; } = null!;
    }
}
