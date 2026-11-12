using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsOracle
{
    public partial class XXORA_DISCOUNT_LIST
    {
        public long DISCOUNT_LIST_ID { get; set; }
        public string DISCOUNT_LIST_NAME { get; set; } = null!;
        public string BU_NAME { get; set; } = null!;
        public string CURRENCY_CODE { get; set; } = null!;
        public long DISCOUNT_LIST_ITEM_ID { get; set; }
        public string ITEM_NUMBER { get; set; } = null!;
        public string PRICING_UOM_CODE { get; set; } = null!;
        public string RULE_DISCOUNT_NAME { get; set; } = null!;
        public string PRICING_RULE_TYPE_CODE { get; set; } = null!;
        public string? PARTY_NUMBER { get; set; }
        public string DISCOUNT_TYPE { get; set; } = null!;
        public decimal DISCOUNT_PRICE { get; set; }
        public DateTime START_DATE { get; set; }
        public DateTime? END_DATE { get; set; }
        public string STATUS { get; set; } = null!;
        public DateTime CREATION_DATE { get; set; }
        public string CREATED_BY { get; set; } = null!;
        public DateTime LAST_UPDATE_DATE { get; set; }
        public string LAST_UPDATED_BY { get; set; } = null!;
    }
}
