using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.Modelslanco
{
    public partial class SEG_GRUPO
    {
        public SEG_GRUPO()
        {
            COD_USUARIOs = new HashSet<SEG_USUARIO>();
        }

        public string COD_GRUPO { get; set; } = null!;
        public string DES_GRUPO { get; set; } = null!;

        public virtual ICollection<SEG_USUARIO> COD_USUARIOs { get; set; }
    }
}
