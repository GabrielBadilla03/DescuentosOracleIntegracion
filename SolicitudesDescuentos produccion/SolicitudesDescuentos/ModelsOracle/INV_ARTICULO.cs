using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsOracle
{
    public partial class INV_ARTICULO
    {
        public string COD_ARTICULO { get; set; } = null!;
        public string DES_ARTICULO { get; set; } = null!;
        public string MEDIDA { get; set; } = null!;
        public string? COD_LINEA { get; set; }
        public string? DES_LINEA { get; set; }
        public string? COD_CLASE { get; set; }
        public string? DES_CLASE { get; set; }
        public string? ACEPTADESCUENTO { get; set; }
    }
}
