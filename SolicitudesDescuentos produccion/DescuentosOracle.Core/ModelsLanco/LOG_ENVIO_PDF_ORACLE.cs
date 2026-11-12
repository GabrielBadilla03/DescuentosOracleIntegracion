using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.Modelslanco
{
    public partial class LOG_ENVIO_PDF_ORACLE
    {
        public decimal ID_LOG { get; set; }
        public string DOCUMENTO { get; set; } = null!;
        public string NOMBRE_ARCHIVO { get; set; } = null!;
        public DateTime FECHA_INTENTO { get; set; }
        public string ESTADO { get; set; } = null!;
        public string? MENSAJE { get; set; }
    }
}
