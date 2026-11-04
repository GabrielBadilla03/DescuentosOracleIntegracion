using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsOracle
{
    public partial class PREDESCUENTO
    {
        public PREDESCUENTO()
        {
            PREDETDESCUENTOs = new HashSet<PREDETDESCUENTO>();
        }

        public string BU_NOMBRE { get; set; } = null!;
        public string COD_CLIENTE { get; set; } = null!;
        public string CONSECUTIVO { get; set; } = null!;
        public DateTime FECHASOLICITUD { get; set; }
        public string TIPODESCUENTO { get; set; } = null!;
        public DateTime? FECHAINICIO { get; set; }
        public DateTime? FECHAFIN { get; set; }
        public string? OBSERVACIONES { get; set; }
        public string INGRESADO_POR { get; set; } = null!;
        public DateTime FECHAREGISTRO { get; set; }
        public string ESTADO { get; set; } = null!;
        public string? AUTORIZADO_POR { get; set; }
        public DateTime? FECHA_APLICACION { get; set; }
        public string GENERADO { get; set; } = null!;

        public virtual ICollection<PREDETDESCUENTO> PREDETDESCUENTOs { get; set; }
    }
}
