using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.Modelslanco
{
    public partial class CXCENCFACREC
    {
        public CXCENCFACREC()
        {
            CXCDETFACRECBITs = new HashSet<CXCDETFACRECBIT>();
            CXCDETFACRECs = new HashSet<CXCDETFACREC>();
        }

        public string COD_CIA { get; set; } = null!;
        public string SUCURSAL { get; set; } = null!;
        public string DOCUMENTO { get; set; } = null!;
        public string CONSIGNATARIO { get; set; } = null!;
        public DateTime? FECHA { get; set; }
        public string? USUARIO { get; set; }
        public string? ESTADO { get; set; }
        public string? OBSERVACIONES { get; set; }
        public string TIPOPERSONA { get; set; } = null!;

        public virtual ICollection<CXCDETFACRECBIT> CXCDETFACRECBITs { get; set; }
        public virtual ICollection<CXCDETFACREC> CXCDETFACRECs { get; set; }
    }
}
