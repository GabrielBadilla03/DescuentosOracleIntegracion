using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsViejadaysi
{
    public partial class VENDOCDETFED
    {
        public VENDOCDETFED()
        {
            VENDOCDETIMPFEDs = new HashSet<VENDOCDETIMPFED>();
        }

        public string CIA { get; set; } = null!;
        public string CLAVE { get; set; } = null!;
        public byte LINEADETALLE { get; set; }
        public string CODIGO_TIPO { get; set; } = null!;
        public string CODIGO_COD { get; set; } = null!;
        public decimal CANTIDAD { get; set; }
        public string UNIDADMEDIDA { get; set; } = null!;
        public string? UNIDADMEDIDACOMERCIAL { get; set; }
        public string? DETALLE { get; set; }
        public decimal PRECIOUNITARIO { get; set; }
        public decimal MONTOTOTAL { get; set; }
        public decimal? MONTODESCUENTO { get; set; }
        public string? NATURALEZADESCUENTO { get; set; }
        public decimal SUBTOTAL { get; set; }
        public decimal TOTALIMPUESTOS { get; set; }
        public decimal MONTOTOTALLINEA { get; set; }
        public decimal? BASEIMPONIBLE { get; set; }
        public string? PARTIDAARANCELARIA { get; set; }
        public string? COD_CABYS { get; set; }
        public decimal? SUGERIDO { get; set; }
        public string? MED_IMP { get; set; }
        public string? PRECIO_IMP { get; set; }
        public string? CLASIFICACION { get; set; }
        public string? CODIGO_DESCUENTO { get; set; }
        public decimal? IVACOBRADOFABRICA { get; set; }
        public decimal? IVAASUMIDOEMISOR { get; set; }
        public string? REG_MEDICAMENTO { get; set; }
        public string? FOR_FARMACEUTICA { get; set; }
        public string? CODIGO_COMBO { get; set; }
        public string? CODIGO_BONIF { get; set; }
        public short? LINEACOMBO { get; set; }
        public string? COD_BARRAS { get; set; }
        public string? DETALLE_NOTA { get; set; }
        public string? COMENTARIOS { get; set; }
        public string? LOTE { get; set; }
        public string? SOURCE_SCHEDULE_NUMBER { get; set; }
        public string? BULTOS { get; set; }

        public virtual ICollection<VENDOCDETIMPFED> VENDOCDETIMPFEDs { get; set; }
    }
}
