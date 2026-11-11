using System;
using System.Collections.Generic;

namespace TomaFisica.ModelsTomaFis
{
    public partial class PRDBATCH
    {
        public string? BATCH { get; set; }
        public string? ARTICULO { get; set; }
        public string? DESCRIPCION { get; set; }
        public decimal? PESO { get; set; }
        public DateTime? FECHA { get; set; }
        public decimal? COSTOTOTAL { get; set; }
        public decimal? VOLUMENORDER { get; set; }
        public decimal? CANTIDAD { get; set; }
    }
}
