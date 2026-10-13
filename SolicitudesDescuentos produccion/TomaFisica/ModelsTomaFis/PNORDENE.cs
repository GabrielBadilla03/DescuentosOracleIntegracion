using System;
using System.Collections.Generic;

namespace TomaFisica.ModelsTomaFis
{
    public partial class PNORDENE
    {
        public string BATCH { get; set; } = null!;
        public string? FORMULA { get; set; }
        public string? DESCRIPCION { get; set; }
        public DateTime? FECHA { get; set; }
        public decimal? COSTO { get; set; }
        public decimal? COSTOTOTAL { get; set; }
        public string PASO { get; set; } = null!;
        public DateTime? FECHACIERRE { get; set; }
        public string? USUARIO { get; set; }
        public string? OBSERVACIONES { get; set; }
        public string REPROCESO { get; set; } = null!;
        public decimal? VOLUMENORDER { get; set; }
        public string AFECTAMP { get; set; } = null!;
        public string RESERVADOINV { get; set; } = null!;
        public string ESTADO { get; set; } = null!;
        public decimal? COSTOADICIONAL { get; set; }
        public string LOCAL1 { get; set; } = null!;
        public string REPLICA1 { get; set; } = null!;
        public string LISTACIERRE { get; set; } = null!;
    }
}
