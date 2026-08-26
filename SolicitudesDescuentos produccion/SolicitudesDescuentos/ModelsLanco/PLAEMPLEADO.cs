using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.Modelslanco
{
    public partial class PLAEMPLEADO
    {
        public string CIA { get; set; } = null!;
        public string EMPLEADO { get; set; } = null!;
        public string NOMBRE { get; set; } = null!;
        public DateTime FECHANAC { get; set; }
        public string SEXO { get; set; } = null!;
        public string CEDULA { get; set; } = null!;
        public bool TIPOID { get; set; }
        public string? SEGUROSOCIAL { get; set; }
        public string ESTADOCIVIL { get; set; } = null!;
        public string DIRECCION { get; set; } = null!;
        public string? TELEFONO { get; set; }
        public string CONYUGUE { get; set; } = null!;
        public byte HIJOS { get; set; }
        public byte HIJOSMAY { get; set; }
        public byte DEPENDIENTES { get; set; }
        public string PLANILLA { get; set; } = null!;
        public string SUCURSAL { get; set; } = null!;
        public string DEPARTAMENTO { get; set; } = null!;
        public string PUESTO { get; set; } = null!;
        public string CATEGORIA { get; set; } = null!;
        public string? JEFEINMEDIATO { get; set; }
        public string HORARIO { get; set; } = null!;
        public string GANAEXTRAS { get; set; } = null!;
        public string GANACOMIS { get; set; } = null!;
        public string MARCATARJETA { get; set; } = null!;
        public string TIPOPAGO { get; set; } = null!;
        public string? BANCO { get; set; }
        public string? CUENTA { get; set; }
        public string ASOCIACION { get; set; } = null!;
        public DateTime FECHAINGRESO { get; set; }
        public DateTime? FECHASALIDA { get; set; }
        public string ESTADO { get; set; } = null!;
        public string PENSIONADO { get; set; } = null!;
        public string MONEDASALARIO { get; set; } = null!;
        public decimal? SALARIOPRD { get; set; }
        public string? PRODUC { get; set; }
        public decimal? ORDEN { get; set; }
        public string? PROCESO { get; set; }
        public string? PROCESO2 { get; set; }
        public string? CEDULANUEVA { get; set; }
        public string MONEDACOMPROBANTE { get; set; } = null!;
        public string? CLAVE { get; set; }
        public string? USUARIO { get; set; }
        public string LOCAL1 { get; set; } = null!;
        public string REPLICA1 { get; set; } = null!;
        public string? EMAIL { get; set; }
        public string? IND_RECIBCORR { get; set; }
        public decimal? PENSIONVOLUN { get; set; }
        public string? COD_CLIENTE { get; set; }
        public byte[]? FOTO { get; set; }
        public string? HUELLA { get; set; }
        public decimal ALQUILERVEHICULO { get; set; }
        public string? FACEID { get; set; }
        public string? PLANILLAWEB { get; set; }
        public string ACTUALIZA { get; set; } = null!;
        public string? ACTUALIZACLAVE { get; set; }
        public string? ID_ORACLE { get; set; }
    }
}
