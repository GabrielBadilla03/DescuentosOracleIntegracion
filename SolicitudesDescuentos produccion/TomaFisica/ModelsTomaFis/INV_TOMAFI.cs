using System;
using System.Collections.Generic;

namespace TomaFisica.ModelsTomaFis
{
    public partial class INV_TOMAFI
    {
        public INV_TOMAFI()
        {
            INV_DETTOMAFIs = new HashSet<INV_DETTOMAFI>();
        }

        public int SECUENCIA { get; set; }
        public string COD_CIA { get; set; } = null!;
        public string? COD_BODEGA { get; set; }
        public string ESTADO { get; set; } = null!;
        public string TIPO { get; set; } = null!;
        public string? OBSERVACION { get; set; }
        public DateTime FEC_MOVTO { get; set; }
        public DateTime? FEC_APLICA { get; set; }
        public string USUARIO { get; set; } = null!;
        public string LOCAL1 { get; set; } = null!;
        public string REPLICA1 { get; set; } = null!;

        public virtual ICollection<INV_DETTOMAFI> INV_DETTOMAFIs { get; set; }
    }
}
