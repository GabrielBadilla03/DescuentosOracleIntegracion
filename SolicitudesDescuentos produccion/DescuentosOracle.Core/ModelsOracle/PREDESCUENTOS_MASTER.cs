using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsOracle
{
    public partial class PREDESCUENTOS_MASTER
    {
        public string CONSECUTIVO { get; set; } = null!;
        public string BU_NOMBRE { get; set; } = null!;
        public string ORGANIZATION_CODE { get; set; } = null!;
        public string COD_CLIENTE { get; set; } = null!;
        public string COD_LINEA { get; set; } = null!;
        public string COD_ARTICULO { get; set; } = null!;
        public string FECHA { get; set; } = null!;
        public decimal PORCENTAJE { get; set; }
        public string COD_USUARIO { get; set; } = null!;
        public string COD_CLASE { get; set; } = null!;
        public string LOCAL1 { get; set; } = null!;
        public string REPLICA1 { get; set; } = null!;
        public string? MEDIDA { get; set; }
        public DateTime? FECHA_INICIO { get; set; }
        public DateTime? FECHA_FIN { get; set; }
    }
}
