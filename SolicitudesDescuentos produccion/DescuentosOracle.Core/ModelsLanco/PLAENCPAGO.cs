using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.Modelslanco
{
    public partial class PLAENCPAGO
    {
        public string CIA { get; set; } = null!;
        public string PLANILLA { get; set; } = null!;
        public int PERIODO { get; set; }
        public string INDPLANILLA { get; set; } = null!;
        public DateTime? FECHAPAGO { get; set; }
        public string ESTADO { get; set; } = null!;
        public string? ASIENTO { get; set; }
        public string FINDEMES { get; set; } = null!;
    }
}
