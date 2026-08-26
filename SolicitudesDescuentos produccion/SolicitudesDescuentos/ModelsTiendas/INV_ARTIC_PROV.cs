using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsTiendas
{
    public partial class INV_ARTIC_PROV
    {
        public string COD_CIA { get; set; } = null!;
        public string COD_PROVEEDOR { get; set; } = null!;
        public string COD_ARTICULO { get; set; } = null!;
        public string COD_ARTIC_PROV { get; set; } = null!;
        public string? CODIGO_BARRAS { get; set; }
        public string MEDIDA { get; set; } = null!;
        public long MINIMO_DESPACHO { get; set; }
        public byte DIAS_TRANSITO { get; set; }
        public string COD_MONEDA { get; set; } = null!;
        public decimal COSTO_ULT_COMPRA { get; set; }
        public string IND_DESCONTINUADO { get; set; } = null!;
        public DateTime FECHA_ULT_COMPRA { get; set; }
        public decimal DESC_FIJO { get; set; }
        public DateTime? FEC_ULTMOVTO { get; set; }
        public string BONIFICA { get; set; } = null!;
        public string PERMITEDESCTO { get; set; } = null!;
    }
}
