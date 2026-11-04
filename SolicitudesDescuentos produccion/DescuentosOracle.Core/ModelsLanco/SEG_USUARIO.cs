using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.Modelslanco
{
    public partial class SEG_USUARIO
    {
        public SEG_USUARIO()
        {
            COD_GRUPOs = new HashSet<SEG_GRUPO>();
        }

        public string COD_USUARIO { get; set; } = null!;
        public string DES_USUARIO { get; set; } = null!;
        public DateTime? CAMBIOCLAVE { get; set; }
        public string? TIPO { get; set; }
        public string? CIA { get; set; }
        public string? SUCURSAL { get; set; }
        public string? PASSWORD { get; set; }
        public string? COD_CLIENTE { get; set; }
        public string? IDIOMA { get; set; }
        public string? CAJA { get; set; }
        public string? CLAVE_USUARIO { get; set; }
        public string? COD_CIA { get; set; }

        public virtual ICollection<SEG_GRUPO> COD_GRUPOs { get; set; }
    }
}
