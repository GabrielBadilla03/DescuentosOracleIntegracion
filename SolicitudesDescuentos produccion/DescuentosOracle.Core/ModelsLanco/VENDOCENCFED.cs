using System;
using System.Collections.Generic;

namespace SolicitudesDescuentos.Modelslanco
{
    public partial class VENDOCENCFED
    {
        public string CIA { get; set; } = null!;
        public string CLAVE { get; set; } = null!;
        public string TIPODOC { get; set; } = null!;
        public string NUMEROCONSECUTIVO { get; set; } = null!;
        public string FECHAEMISION { get; set; } = null!;
        public string EMISOR_NOMBRE { get; set; } = null!;
        public string EMISOR_ID_TIPO { get; set; } = null!;
        public string EMISOR_ID_NUMERO { get; set; } = null!;
        public string? EMISOR_NOMBRECOMERCIAL { get; set; }
        public string? EMISOR_UBIC_PROVINCIA { get; set; }
        public string? EMISOR_UBIC_CANTON { get; set; }
        public string? EMISOR_UBIC_DISTRITO { get; set; }
        public string? EMISOR_UBIC_BARRIO { get; set; }
        public string EMISOR_UBIC_OTRASSENAS { get; set; } = null!;
        public byte? EMISOR_TEL_CODIGOPAIS { get; set; }
        public decimal? EMISOR_TEL_NUMTELEFONO { get; set; }
        public byte? EMISOR_FAX_CODIGOPAIS { get; set; }
        public decimal? EMISOR_FAX_NUMTELEFONO { get; set; }
        public string EMISOR_CORREOELECTRONICO { get; set; } = null!;
        public string RECEPTOR_NOMBRE { get; set; } = null!;
        public string? RECEPTOR_ID_TIPO { get; set; }
        public string? RECEPTOR_ID_NUMERO { get; set; }
        public string? RECEPTOR_IDEXTRANJERO { get; set; }
        public string? RECEPTOR_NOMBRECOMERCIAL { get; set; }
        public string? RECEPTOR_UBIC_PROVINCIA { get; set; }
        public string? RECEPTOR_UBIC_CANTON { get; set; }
        public string? RECEPTOR_UBIC_DISTRITO { get; set; }
        public string? RECEPTOR_UBIC_BARRIO { get; set; }
        public string? RECEPTOR_UBIC_OTRASSENAS { get; set; }
        public byte? RECEPTOR_TEL_CODIGOPAIS { get; set; }
        public decimal? RECEPTOR_TEL_NUMTELEFONO { get; set; }
        public byte? RECEPTOR_FAX_CODIGOPAIS { get; set; }
        public decimal? RECEPTOR_FAX_NUMTELEFONO { get; set; }
        public string? RECEPTOR_CORREOELECTRONICO { get; set; }
        public string? CONDICIONVENTA { get; set; }
        public string? PLAZOCREDITO { get; set; }
        public string? CODIGOMONEDA { get; set; }
        public decimal? TIPOCAMBIO { get; set; }
        public decimal? TOTALSERVGRAVADOS { get; set; }
        public decimal? TOTALSERVEXENTOS { get; set; }
        public decimal? TOTALMERCANCIASGRAVADAS { get; set; }
        public decimal? TOTALMERCANCIASEXENTAS { get; set; }
        public decimal? TOTALGRAVADO { get; set; }
        public decimal? TOTALEXENTO { get; set; }
        public decimal? TOTALVENTA { get; set; }
        public decimal? TOTALDESCUENTOS { get; set; }
        public decimal TOTALVENTANETA { get; set; }
        public decimal TOTALCOMPROBANTE { get; set; }
        public string? INFOREF_TIPODOC { get; set; }
        public string? INFOREF_NUMERO { get; set; }
        public string? INFOREF_FECHAEMISION { get; set; }
        public string? INFOREF_CODIGO { get; set; }
        public string? INFOREF_RAZON { get; set; }
        public string NORMAVIGENTE_NUMRESOLUCION { get; set; } = null!;
        public string NORMAVIGENTE_FECHARESOLUCION { get; set; } = null!;
        public string? DOCUMENTO { get; set; }
        public string ESTADO { get; set; } = null!;
        public decimal? TOTALIMPUESTO { get; set; }
        public string? IMPRESO { get; set; }
        public string? ESTADO_CLIENTE { get; set; }
        public string? OBSERVACION { get; set; }
        public string? INFOREF_DOC { get; set; }
        public string? SUCURSAL { get; set; }
        public string? DOCCREDITO { get; set; }
        public string? MENSAJE_HACIENDA { get; set; }
        public string? ESTADO_HACIENDA { get; set; }
        public string? COD_RUTA { get; set; }
        public string? DOCDEBITO { get; set; }
        public string? REGENERADO { get; set; }
        public string? REIMPRIME { get; set; }
        public string? COD_CLIENTE { get; set; }
        public decimal? TOTALSERVEXONERADO { get; set; }
        public decimal? TOTALMERCEXONERADA { get; set; }
        public decimal? TOTALEXONERADO { get; set; }
        public decimal? TOTALIVADEVUELTO { get; set; }
        public decimal? TOTALOTROSCARGOS { get; set; }
        public string? ENVIO_CORREO { get; set; }
        public string? COD_PROVEEDOR { get; set; }
        public string? TIPO_DOC { get; set; }
        public string? FORMATO { get; set; }
        public string? EXPORTADO { get; set; }
        public string? TRAMITAFACTURA { get; set; }
        public string? PROVEEDOR_SISTEMAS { get; set; }
        public decimal? TOTALSERVNOSUJETO { get; set; }
        public decimal? TOTALMERCNOSUJETA { get; set; }
        public decimal? TOTALNOSUJETO { get; set; }
        public string? RECEPTOR_COD_ACTIVIDAD { get; set; }
        public string? EMISOR_COD_ACTIVIDAD { get; set; }
        public string? COD_BARRAS { get; set; }
        public string? DE_RECHAZO { get; set; }
        public string? ORDEN_COMPRA { get; set; }
        public string? TRANSPORTISTA { get; set; }
        public string? INDORACLE { get; set; }
        public string? ACT_ORACLE { get; set; }
        public string? NOMBRE_VENDEDOR { get; set; }
        public string? NUMEROS_FORMULARIO { get; set; }
        public string? COMENTARIOS { get; set; }
        public string? MEDIOSPAGO { get; set; }
        public string? PAIS_ORIGEN { get; set; }
        public DateTime? FECHA_VENCIMIENTO { get; set; }
        public string? AGENTE { get; set; }
        public string? OBSERVACIONES { get; set; }
        public string? FORMULARIO { get; set; }
        public decimal? PESOBRUTO { get; set; }
        public decimal? PESONETO { get; set; }
        public string? LOTE { get; set; }
        public string? TRASLADO { get; set; }
        public string? IMPRESORA { get; set; }
        public string? REPORTE { get; set; }
        public decimal? PESOBRUTO_KG { get; set; }
        public decimal? PESONETO_KG { get; set; }
        public string? VENDEDOR_WALMART { get; set; }
        public string? TIENDA_WALMART { get; set; }
        public string? TERMINO_ENVIO { get; set; }
        public string? TARIMAS { get; set; }
        public string? PDFORALCE { get; set; }
        public string? NUMERO_BOLETA { get; set; }
        public string? MOTIVO_NC { get; set; }
        public string? BULTOS_PK_TMP { get; set; }
        public string? BULTOS_PK { get; set; }
        public string? NUMERO_LINEA_FACTURA { get; set; }
        public string? NUMERO_INFORME_GASTO { get; set; }
    }
}
