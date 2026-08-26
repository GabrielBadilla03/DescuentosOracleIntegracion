using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsOracle
{
    public partial class XXORA_ITEM_MASTER
    {
        public string BU_NAME { get; set; } = null!;
        public string ORGANIZATION_CODE { get; set; } = null!;
        public string ITEM_NUMBER { get; set; } = null!;
        public string DESCRIPTION { get; set; } = null!;
        public string? LONG_DESCRIPTION { get; set; }
        public decimal? CASE_PACK_QUANTITY { get; set; }
        public string PRIMARY_UOM_CODE { get; set; } = null!;
        public string? TAX_CLASSIFICATION_CODE { get; set; }
        public decimal? TAX_RATE { get; set; }
        public string? CATEGORY_CODE { get; set; }
        public string? CATEGORY_NAME { get; set; }
        public string? SUBCATEGORY_CODE { get; set; }
        public string? SUBCATEGORY_NAME { get; set; }
        public string? ORIGIN_COUNTRY { get; set; }
        public string STATUS { get; set; } = null!;
        public DateTime CREATION_DATE { get; set; }
        public string CREATED_BY { get; set; } = null!;
        public DateTime LAST_UPDATE_DATE { get; set; }
        public string LAST_UPDATED_BY { get; set; } = null!;
        public decimal? UNIT_WEIGHT { get; set; }
        public string? WEIGHT_UOM_CODE { get; set; }
        public string? SECONDARY_UOM_CODE { get; set; }
        public string? ITEM_STATUS_CODE { get; set; }
        public string? ACCEPTADESCUENTO { get; set; }
        public string? INDTRASLADO { get; set; }
        public string? ITEM_CODE { get; set; }
        public string? ITEM_TYPE { get; set; }
        public decimal? ATTRIBUTE_DENSIDAD { get; set; }
    }
}
