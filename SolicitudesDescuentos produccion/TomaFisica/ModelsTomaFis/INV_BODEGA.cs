using System;
using System.Collections.Generic;

namespace TomaFisica.ModelsTomaFis
{
    public partial class INV_BODEGA
    {
        public INV_BODEGA()
        {
            INV_LOCs = new HashSet<INV_LOC>();
        }

        public string COD_CIA { get; set; } = null!;
        public string COD_BODEGA { get; set; } = null!;
        public string SUCURSAL { get; set; } = null!;
        public string DES_BODEGA { get; set; } = null!;
        public string IND_VENTA { get; set; } = null!;
        public string? COD_CTA { get; set; }
        public string? IMPRESORA { get; set; }
        public string? CCOSTO { get; set; }
        public string LOCAL1 { get; set; } = null!;
        public string REPLICA1 { get; set; } = null!;
        public string TIPOPRODUCTO { get; set; } = null!;
        public string? INDHANDHELD { get; set; }
        public string AJUSTANEGATIVO { get; set; } = null!;
        public string? EXISTELOCALIZA { get; set; }
        public string? NOMBREFUSION { get; set; }
        public string? ALMACENFUSION { get; set; }

        public virtual ICollection<INV_LOC> INV_LOCs { get; set; }
    }
}
