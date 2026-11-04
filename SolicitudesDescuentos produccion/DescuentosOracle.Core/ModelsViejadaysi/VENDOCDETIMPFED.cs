using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsViejadaysi
{
    public partial class VENDOCDETIMPFED
    {
        public string CIA { get; set; } = null!;
        public string CLAVE { get; set; } = null!;
        public byte LINEADETALLE { get; set; }
        public string CODIGO { get; set; } = null!;
        public decimal TARIFA { get; set; }
        public decimal MONTO { get; set; }
        public string? EXONERA_TIPODOC { get; set; }
        public string? EXONERA_NUMDOC { get; set; }
        public string? EXONERA_NOMINSTIT { get; set; }
        public string? EXONERA_FECHAEMISION { get; set; }
        public decimal? EXONERA_MONTOIMP { get; set; }
        public byte? EXONERA_PORCCOMPRA { get; set; }
        public string? CODIGOTARIFA { get; set; }
        public decimal? FACTORIVA { get; set; }
        public string? CODIGO_COMBO { get; set; }
        public string? CODIGO_BONIF { get; set; }
        public string? EXONERA_ARTICULO { get; set; }
        public string? EXONERA_INCISO { get; set; }

        public virtual VENDOCDETFED VENDOCDETFED { get; set; } = null!;
    }
}
