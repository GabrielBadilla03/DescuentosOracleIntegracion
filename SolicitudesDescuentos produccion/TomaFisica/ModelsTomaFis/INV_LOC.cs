using System;
using System.Collections.Generic;

namespace TomaFisica.ModelsTomaFis
{
    public partial class INV_LOC
    {
        public string COD_CIA { get; set; } = null!;
        public string COD_BODEGA { get; set; } = null!;
        public string LOCALIZA { get; set; } = null!;
        public string DES_LOCALIZA { get; set; } = null!;
        public string LOCAL1 { get; set; } = null!;
        public string REPLICA1 { get; set; } = null!;

        public virtual INV_BODEGA COD_ { get; set; } = null!;
    }
}
