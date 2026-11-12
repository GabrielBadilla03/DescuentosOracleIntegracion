using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.Modelslanco
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
    }
}
