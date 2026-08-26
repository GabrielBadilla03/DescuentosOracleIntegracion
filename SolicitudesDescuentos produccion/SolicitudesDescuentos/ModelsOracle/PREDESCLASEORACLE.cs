using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsOracle
{
    public partial class PREDESCLASEORACLE
    {
        public string ORGANIZATION_CODE { get; set; } = null!;
        public string IDCLIENTE { get; set; } = null!;
        public string? CATEGORY_CODE { get; set; }
        public string? SUBCATEGORY_CODE { get; set; }
        public string? ITEM_NUMBER { get; set; }
        public decimal PORCENTAJE { get; set; }
        public DateTime? FECHA_INICIO { get; set; }
        public DateTime? FECHA_FIN { get; set; }
    }
}
