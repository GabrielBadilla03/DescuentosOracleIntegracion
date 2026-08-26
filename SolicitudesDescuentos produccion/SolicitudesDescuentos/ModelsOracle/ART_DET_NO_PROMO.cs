using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsOracle
{
    public partial class ART_DET_NO_PROMO
    {
        public string BU_NAME { get; set; } = null!;
        public string ORGANIZATION_CODE { get; set; } = null!;
        public string ITEM_NUMBER { get; set; } = null!;
        public string RULE_DISCOUNT_NAME { get; set; } = null!;
        public string PARTY_NUMBER { get; set; } = null!;
        public decimal DISCOUNT_PRICE { get; set; }
        public DateTime START_DATE { get; set; }
        public DateTime? END_DATE { get; set; }
        public string? PRICING_UOM_CODE { get; set; }

        public virtual ART_NO_PROMO ART_NO_PROMO { get; set; } = null!;
    }
}
