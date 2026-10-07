using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.ModelsTomaFis
{
    public partial class INV_ARTICULO
    {
        public INV_ARTICULO()
        {
            INV_ARTIC_CODBARs = new HashSet<INV_ARTIC_CODBAR>();
        }

        public string COD_ARTICULO { get; set; } = null!;
        public bool? UNIDADNEGOCIO { get; set; }
        public decimal MULTIPLOCAJA { get; set; }
        public string MEDIDA { get; set; } = null!;
        public string DESCRIPCION { get; set; } = null!;
        public string TIPOINVENTARIO { get; set; } = null!;
        public string SUBCATEGORIA { get; set; } = null!;
        public string CATEGORIA { get; set; } = null!;

        public virtual ICollection<INV_ARTIC_CODBAR> INV_ARTIC_CODBARs { get; set; }
    }
}
