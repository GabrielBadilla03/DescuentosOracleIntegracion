using System;
using System.Collections.Generic;

namespace TomaFisica.ModelsTomaFis
{
    public partial class INV_DETTOMAFI
    {
        public int SECUENCIA { get; set; }
        public string COD_LOTE { get; set; } = null!;
        public string COD_ARTICULO { get; set; } = null!;
        public string LOCALIZA { get; set; } = null!;
        public decimal CAN_EXISTE { get; set; }
        public decimal COSTO { get; set; }
        public decimal? CONTEO1 { get; set; }
        public decimal? CONTEO2 { get; set; }
        public decimal? CONTEO3 { get; set; }
        public string LOCAL1 { get; set; } = null!;
        public string REPLICA1 { get; set; } = null!;
        public byte? ORIGEN { get; set; }
        public string? BATCH { get; set; }
        public DateTime? FECHALOTE { get; set; }
        public string? RESERVA { get; set; }

        public virtual INV_TOMAFI SECUENCIANavigation { get; set; } = null!;
    }
}
