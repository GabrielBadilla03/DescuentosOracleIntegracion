using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsTomaFis
{
    public partial class INV_ARTIC_CODBAR
    {
        public string COD_CIA { get; set; } = null!;
        public string COD_ARTICULO { get; set; } = null!;
        public string COD_BARRAS { get; set; } = null!;
        public string? TITULAR { get; set; }
        public string LOCAL1 { get; set; } = null!;
        public string REPLICA1 { get; set; } = null!;
        public string? CLASE { get; set; }

        public virtual INV_ARTICULO COD_ARTICULONavigation { get; set; } = null!;
    }
}
