using System;
using System.Collections.Generic;

namespace TomaFisica.ModelsTomaFis
{
    public partial class AUD_INV_ARTICULO
    {
        public long ID_AUDITORIA { get; set; }
        public string COD_ARTICULO { get; set; } = null!;
        public string COD_USUARIO { get; set; } = null!;
        public string? NOMBRE_USUARIO { get; set; }
        public string? COD_CIA { get; set; }
        public string? COD_SUCURSAL { get; set; }
        public DateTime FECHA_CAMBIO { get; set; }
        public string TIPO_ACCION { get; set; } = null!;
        public string DESCRIPCION { get; set; } = null!;
    }
}
