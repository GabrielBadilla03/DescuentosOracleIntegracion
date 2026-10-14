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

        public virtual ICollection<INV_LOC> INV_LOCs { get; set; }
    }
}
