using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.Modelslanco
{
    public partial class CXCDETFACRECBIT
    {
        public string COD_CIA { get; set; } = null!;
        public string SUCURSAL { get; set; } = null!;
        public string DOCUMENTO { get; set; } = null!;
        public short SECUENCIA { get; set; }
        public string CLAVE { get; set; } = null!;
        public string FACTURAELEC { get; set; } = null!;
        public string FACTURAINT { get; set; } = null!;
        public string COD_CLIENTE { get; set; } = null!;
        public DateTime FECHA { get; set; }
        public string? OBSERVACIONES { get; set; }
        public string? RUTA { get; set; }
        public string? NOMBRE_CLIENTE { get; set; }

        public virtual CXCENCFACREC CXCENCFACREC { get; set; } = null!;
    }
}
