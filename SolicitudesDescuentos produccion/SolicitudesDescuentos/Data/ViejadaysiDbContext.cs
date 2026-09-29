using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SolicitudesDescuentos.ModelsViejadaysi;

namespace SolicitudesDescuentos.Data
{
    public partial class ViejadaysiDbContext : DbContext
    {
        public ViejadaysiDbContext()
        {
        }

        public ViejadaysiDbContext(DbContextOptions<ViejadaysiDbContext> options)
            : base(options)
        {
        }

        public virtual DbSet<VENDOCDETFED> VENDOCDETFEDs { get; set; } = null!;
        public virtual DbSet<VENDOCDETIMPFED> VENDOCDETIMPFEDs { get; set; } = null!;
        public virtual DbSet<VENDOCENCFED> VENDOCENCFEDs { get; set; } = null!;
        public virtual DbSet<XXORA_VENDOCDETFED> XXORA_VENDOCDETFEDs { get; set; } = null!;
        public virtual DbSet<XXORA_VENDOCDETIMPFED> XXORA_VENDOCDETIMPFEDs { get; set; } = null!;
        public virtual DbSet<XXORA_VENDOCENCFED> XXORA_VENDOCENCFEDs { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("NUEVO");

            modelBuilder.Entity<VENDOCDETFED>(entity =>
            {
                entity.HasKey(e => new { e.CIA, e.CLAVE, e.LINEADETALLE })
                    .HasName("PK_CLAVELINEADETALLE");

                entity.ToTable("VENDOCDETFED");

                entity.Property(e => e.CIA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.CLAVE)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.LINEADETALLE).HasPrecision(4);

                entity.Property(e => e.BASEIMPONIBLE)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0 ");

                entity.Property(e => e.BULTOS)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.CANTIDAD).HasColumnType("NUMBER(18,4)");

                entity.Property(e => e.CLASIFICACION)
                    .HasMaxLength(1)
                    .IsUnicode(false);

                entity.Property(e => e.CODIGO_BONIF)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.CODIGO_COD)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.CODIGO_COMBO)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.CODIGO_DESCUENTO)
                    .HasMaxLength(6)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'06'");

                entity.Property(e => e.CODIGO_TIPO)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.COD_BARRAS)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.COD_CABYS)
                    .HasMaxLength(13)
                    .IsUnicode(false);

                entity.Property(e => e.COMENTARIOS)
                    .HasMaxLength(500)
                    .IsUnicode(false);

                entity.Property(e => e.DETALLE)
                    .HasMaxLength(160)
                    .IsUnicode(false);

                entity.Property(e => e.DETALLE_NOTA)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.FOR_FARMACEUTICA)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.IVAASUMIDOEMISOR)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.IVACOBRADOFABRICA)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.LINEACOMBO).HasPrecision(5);

                entity.Property(e => e.LOTE)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.MED_IMP)
                    .HasMaxLength(15)
                    .IsUnicode(false);

                entity.Property(e => e.MONTODESCUENTO).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.MONTOTOTAL).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.MONTOTOTALLINEA).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.NATURALEZADESCUENTO)
                    .HasMaxLength(80)
                    .IsUnicode(false);

                entity.Property(e => e.PARTIDAARANCELARIA)
                    .HasMaxLength(12)
                    .IsUnicode(false);

                entity.Property(e => e.PRECIOUNITARIO).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.PRECIO_IMP)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.REG_MEDICAMENTO)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.SOURCE_SCHEDULE_NUMBER)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.SUBTOTAL).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.SUGERIDO).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALIMPUESTOS).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.UNIDADMEDIDA)
                    .HasMaxLength(15)
                    .IsUnicode(false);

                entity.Property(e => e.UNIDADMEDIDACOMERCIAL)
                    .HasMaxLength(20)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<VENDOCDETIMPFED>(entity =>
            {
                entity.HasKey(e => new { e.CIA, e.CLAVE, e.LINEADETALLE, e.CODIGO })
                    .HasName("PK_CLAVELINEADETCOD");

                entity.ToTable("VENDOCDETIMPFED");

                entity.Property(e => e.CIA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.CLAVE)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.LINEADETALLE).HasPrecision(4);

                entity.Property(e => e.CODIGO)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.CODIGOTARIFA)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.CODIGO_BONIF)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.CODIGO_COMBO)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.EXONERA_ARTICULO)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.EXONERA_FECHAEMISION)
                    .HasMaxLength(40)
                    .IsUnicode(false);

                entity.Property(e => e.EXONERA_INCISO)
                    .HasMaxLength(6)
                    .IsUnicode(false);

                entity.Property(e => e.EXONERA_MONTOIMP).HasColumnType("NUMBER(13,5)");

                entity.Property(e => e.EXONERA_NOMINSTIT)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.EXONERA_NUMDOC)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.EXONERA_PORCCOMPRA).HasPrecision(3);

                entity.Property(e => e.EXONERA_TIPODOC)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.FACTORIVA)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0 ");

                entity.Property(e => e.MONTO).HasColumnType("NUMBER(13,5)");

                entity.Property(e => e.TARIFA).HasColumnType("NUMBER(4,2)");

                entity.HasOne(d => d.VENDOCDETFED)
                    .WithMany(p => p.VENDOCDETIMPFEDs)
                    .HasForeignKey(d => new { d.CIA, d.CLAVE, d.LINEADETALLE })
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("FK_CLAVEDETALLE");
            });

            modelBuilder.Entity<VENDOCENCFED>(entity =>
            {
                entity.HasKey(e => new { e.CIA, e.CLAVE })
                    .HasName("VENDOCENCFED_PK");

                entity.ToTable("VENDOCENCFED");

                entity.HasIndex(e => new { e.CIA, e.REIMPRIME, e.CLAVE }, "IDX_VENDOCENCFED_01");

                entity.HasIndex(e => new { e.CIA, e.ESTADO_HACIENDA }, "IDX_VENDOCENCFED_CIA_ESTADO");

                entity.HasIndex(e => new { e.CIA, e.SUCURSAL, e.DOCUMENTO, e.TIPODOC }, "VENDOCENCFED_CONSE");

                entity.HasIndex(e => new { e.CIA, e.SUCURSAL, e.TIPODOC, e.DOCUMENTO, e.COD_CLIENTE, e.COD_PROVEEDOR }, "VENDOCENCFED_DOC")
                    .IsUnique();

                entity.Property(e => e.CIA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.CLAVE)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.ACT_ORACLE)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'")
                    .IsFixedLength();

                entity.Property(e => e.AGENTE)
                    .HasMaxLength(500)
                    .IsUnicode(false);

                entity.Property(e => e.BULTOS_PK)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.BULTOS_PK_TMP)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.CODIGOMONEDA)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.COD_BARRAS)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.COD_CLIENTE)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.COD_PROVEEDOR)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.COD_RUTA)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.COMENTARIOS)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.CONDICIONVENTA)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.DE_RECHAZO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'");

                entity.Property(e => e.DOCCREDITO)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.DOCDEBITO)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.DOCUMENTO)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_COD_ACTIVIDAD)
                    .HasMaxLength(6)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_CORREOELECTRONICO)
                    .HasMaxLength(60)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_FAX_CODIGOPAIS).HasPrecision(3);

                entity.Property(e => e.EMISOR_FAX_NUMTELEFONO).HasColumnType("NUMBER(20)");

                entity.Property(e => e.EMISOR_ID_NUMERO)
                    .HasMaxLength(12)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_ID_TIPO)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_NOMBRE)
                    .HasMaxLength(80)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_NOMBRECOMERCIAL)
                    .HasMaxLength(80)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_TEL_CODIGOPAIS).HasPrecision(3);

                entity.Property(e => e.EMISOR_TEL_NUMTELEFONO).HasColumnType("NUMBER(20)");

                entity.Property(e => e.EMISOR_UBIC_BARRIO)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_UBIC_CANTON)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_UBIC_DISTRITO)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_UBIC_OTRASSENAS)
                    .HasMaxLength(160)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_UBIC_PROVINCIA)
                    .HasMaxLength(1)
                    .IsUnicode(false);

                entity.Property(e => e.ENVIO_CORREO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'")
                    .IsFixedLength();

                entity.Property(e => e.ESTADO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'P'                   ")
                    .IsFixedLength();

                entity.Property(e => e.ESTADO_CLIENTE)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.ESTADO_HACIENDA)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.EXPORTADO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'");

                entity.Property(e => e.FECHAEMISION)
                    .HasMaxLength(40)
                    .IsUnicode(false);

                entity.Property(e => e.FECHA_VENCIMIENTO).HasColumnType("DATE");

                entity.Property(e => e.FORMATO)
                    .HasMaxLength(2)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'AM'")
                    .IsFixedLength();

                entity.Property(e => e.FORMULARIO)
                    .HasMaxLength(500)
                    .IsUnicode(false);

                entity.Property(e => e.IMPRESO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'")
                    .IsFixedLength();

                entity.Property(e => e.IMPRESORA)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.INDORACLE)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'");

                entity.Property(e => e.INFOREF_CODIGO)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.INFOREF_DOC)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.INFOREF_FECHAEMISION)
                    .HasMaxLength(40)
                    .IsUnicode(false);

                entity.Property(e => e.INFOREF_NUMERO)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.INFOREF_RAZON)
                    .HasMaxLength(180)
                    .IsUnicode(false);

                entity.Property(e => e.INFOREF_TIPODOC)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.LOTE).HasColumnType("CLOB");

                entity.Property(e => e.MEDIOSPAGO)
                    .HasMaxLength(250)
                    .IsUnicode(false);

                entity.Property(e => e.MENSAJE_HACIENDA).IsUnicode(false);

                entity.Property(e => e.MOTIVO_NC)
                    .HasMaxLength(255)
                    .IsUnicode(false);

                entity.Property(e => e.NOMBRE_VENDEDOR)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.NORMAVIGENTE_FECHARESOLUCION)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.NORMAVIGENTE_NUMRESOLUCION)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.NUMEROCONSECUTIVO)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.NUMEROS_FORMULARIO)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.NUMERO_BOLETA)
                    .HasMaxLength(255)
                    .IsUnicode(false);

                entity.Property(e => e.NUMERO_INFORME_GASTO)
                    .HasMaxLength(250)
                    .IsUnicode(false);

                entity.Property(e => e.NUMERO_LINEA_FACTURA)
                    .HasMaxLength(250)
                    .IsUnicode(false);

                entity.Property(e => e.OBSERVACION)
                    .HasMaxLength(1024)
                    .IsUnicode(false);

                entity.Property(e => e.OBSERVACIONES)
                    .HasMaxLength(500)
                    .IsUnicode(false);

                entity.Property(e => e.ORDEN_COMPRA)
                    .HasMaxLength(500)
                    .IsUnicode(false);

                entity.Property(e => e.PAIS_ORIGEN)
                    .HasMaxLength(500)
                    .IsUnicode(false);

                entity.Property(e => e.PDFORALCE)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'");

                entity.Property(e => e.PESOBRUTO).HasColumnType("NUMBER(15,2)");

                entity.Property(e => e.PESOBRUTO_KG).HasColumnType("NUMBER(15,2)");

                entity.Property(e => e.PESONETO).HasColumnType("NUMBER(15,2)");

                entity.Property(e => e.PESONETO_KG).HasColumnType("NUMBER(15,2)");

                entity.Property(e => e.PLAZOCREDITO)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.PROVEEDOR_SISTEMAS)
                    .HasMaxLength(25)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'3101555844'");

                entity.Property(e => e.RECEPTOR_COD_ACTIVIDAD)
                    .HasMaxLength(6)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_CORREOELECTRONICO)
                    .HasMaxLength(60)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_FAX_CODIGOPAIS).HasPrecision(3);

                entity.Property(e => e.RECEPTOR_FAX_NUMTELEFONO).HasColumnType("NUMBER(20)");

                entity.Property(e => e.RECEPTOR_IDEXTRANJERO)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_ID_NUMERO)
                    .HasMaxLength(12)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_ID_TIPO)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_NOMBRE)
                    .HasMaxLength(80)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_NOMBRECOMERCIAL)
                    .HasMaxLength(80)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_TEL_CODIGOPAIS).HasPrecision(3);

                entity.Property(e => e.RECEPTOR_TEL_NUMTELEFONO).HasColumnType("NUMBER(20)");

                entity.Property(e => e.RECEPTOR_UBIC_BARRIO)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_UBIC_CANTON)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_UBIC_DISTRITO)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_UBIC_OTRASSENAS)
                    .HasMaxLength(160)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_UBIC_PROVINCIA)
                    .HasMaxLength(1)
                    .IsUnicode(false);

                entity.Property(e => e.REGENERADO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'")
                    .IsFixedLength();

                entity.Property(e => e.REIMPRIME)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'")
                    .IsFixedLength();

                entity.Property(e => e.REPORTE)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.SUCURSAL)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.TARIMAS)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.TERMINO_ENVIO)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.TIENDA_WALMART)
                    .HasMaxLength(255)
                    .IsUnicode(false);

                entity.Property(e => e.TIPOCAMBIO).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TIPODOC)
                    .HasMaxLength(2)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.TIPO_DOC)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.TOTALCOMPROBANTE).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALDESCUENTOS).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALEXENTO).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALEXONERADO)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.TOTALGRAVADO).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALIMPUESTO).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALIVADEVUELTO)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.TOTALMERCANCIASEXENTAS).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALMERCANCIASGRAVADAS).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALMERCEXONERADA)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.TOTALMERCNOSUJETA)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.TOTALNOSUJETO)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.TOTALOTROSCARGOS)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.TOTALSERVEXENTOS).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALSERVEXONERADO)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.TOTALSERVGRAVADOS).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALSERVNOSUJETO)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.TOTALVENTA).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALVENTANETA).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TRAMITAFACTURA)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'\n")
                    .IsFixedLength();

                entity.Property(e => e.TRANSPORTISTA)
                    .HasMaxLength(250)
                    .IsUnicode(false);

                entity.Property(e => e.TRASLADO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'")
                    .IsFixedLength();

                entity.Property(e => e.VENDEDOR_WALMART)
                    .HasMaxLength(255)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<XXORA_VENDOCDETFED>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("XXORA_VENDOCDETFED");

                entity.Property(e => e.BASEIMPONIBLE)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.BULTOS)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.CANTIDAD).HasColumnType("NUMBER(18,4)");

                entity.Property(e => e.CIA)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.CLASIFICACION)
                    .HasMaxLength(1)
                    .IsUnicode(false);

                entity.Property(e => e.CLAVE)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.CODIGO_BONIF)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.CODIGO_COD)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.CODIGO_COMBO)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.CODIGO_DESCUENTO)
                    .HasMaxLength(6)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'06'");

                entity.Property(e => e.CODIGO_IMPUESTO)
                    .HasMaxLength(255)
                    .IsUnicode(false);

                entity.Property(e => e.CODIGO_TIPO)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.COD_CABYS)
                    .HasMaxLength(14)
                    .IsUnicode(false);

                entity.Property(e => e.COMENTARIOS)
                    .HasMaxLength(500)
                    .IsUnicode(false);

                entity.Property(e => e.DETALLE)
                    .HasMaxLength(160)
                    .IsUnicode(false);

                entity.Property(e => e.DETALLE_NOTA)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.FOR_FARMACEUTICA)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.ID_FACTURA_ORACLE).HasColumnType("NUMBER");

                entity.Property(e => e.IVAASUMIDOEMISOR)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.IVACOBRADOFABRICA)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.LINEACOMBO).HasPrecision(5);

                entity.Property(e => e.LINEADETALLE).HasPrecision(4);

                entity.Property(e => e.LOTE)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.MED_IMP)
                    .HasMaxLength(15)
                    .IsUnicode(false);

                entity.Property(e => e.MONTODESCUENTO).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.MONTOTOTAL).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.MONTOTOTALLINEA).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.NATURALEZADESCUENTO)
                    .HasMaxLength(80)
                    .IsUnicode(false);

                entity.Property(e => e.NUM_FACTURA_ORACLE)
                    .HasMaxLength(250)
                    .IsUnicode(false);

                entity.Property(e => e.PARTIDAARANCELARIA)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.PRECIOUNITARIO).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.PRECIO_IMP)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.REG_MEDICAMENTO)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.SOURCE_SCHEDULE_NUMBER)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.SUBTOTAL).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.SUGERIDO).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TASA_IMPUESTO)
                    .HasMaxLength(255)
                    .IsUnicode(false);

                entity.Property(e => e.TOTALIMPUESTOS).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.UNIDADMEDIDA)
                    .HasMaxLength(15)
                    .IsUnicode(false);

                entity.Property(e => e.UNIDADMEDIDACOMERCIAL)
                    .HasMaxLength(20)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<XXORA_VENDOCDETIMPFED>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("XXORA_VENDOCDETIMPFED");

                entity.Property(e => e.CIA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.CLAVE)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.CODIGO)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.CODIGOTARIFA)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.CODIGO_BONIF)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.CODIGO_COMBO)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.EXONERA_ARTICULO)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.EXONERA_FECHAEMISION)
                    .HasMaxLength(40)
                    .IsUnicode(false);

                entity.Property(e => e.EXONERA_INCISO)
                    .HasMaxLength(6)
                    .IsUnicode(false);

                entity.Property(e => e.EXONERA_MONTOIMP).HasColumnType("NUMBER(13,5)");

                entity.Property(e => e.EXONERA_NOMINSTIT)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.EXONERA_NUMDOC)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.EXONERA_PORCCOMPRA).HasPrecision(3);

                entity.Property(e => e.EXONERA_TIPODOC)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.FACTORIVA)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.LINEADETALLE).HasPrecision(4);

                entity.Property(e => e.MONTO).HasColumnType("NUMBER(13,5)");

                entity.Property(e => e.NUM_FACTURA_ORACLE)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.TARIFA).HasColumnType("NUMBER(4,2)");
            });

            modelBuilder.Entity<XXORA_VENDOCENCFED>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("XXORA_VENDOCENCFED");

                entity.Property(e => e.AGENTE)
                    .HasMaxLength(500)
                    .IsUnicode(false);

                entity.Property(e => e.BULTOS_PK)
                    .HasMaxLength(255)
                    .IsUnicode(false);

                entity.Property(e => e.BULTOS_PK_TMP)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.CIA)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.CLAVE)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.CODIGOMONEDA)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.COD_CLIENTE)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.COD_PROVEEDOR)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.COD_RUTA)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.COMENTARIOS)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.CONDICIONVENTA)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.DOCCREDITO)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.DOCDEBITO)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.DOCUMENTO)
                    .HasMaxLength(250)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_COD_ACTIVIDAD)
                    .HasMaxLength(6)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_CORREOELECTRONICO)
                    .HasMaxLength(60)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_FAX_CODIGOPAIS).HasColumnType("NUMBER(22)");

                entity.Property(e => e.EMISOR_FAX_NUMTELEFONO)
                    .HasMaxLength(255)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_FAX_NUMTELEFONO_TMP)
                    .HasMaxLength(255)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_ID_NUMERO)
                    .HasMaxLength(12)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_ID_TIPO)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_NOMBRE)
                    .HasMaxLength(80)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_NOMBRECOMERCIAL)
                    .HasMaxLength(80)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_TEL_CODIGOPAIS).HasColumnType("NUMBER(22)");

                entity.Property(e => e.EMISOR_TEL_NUMTELEFONO)
                    .HasMaxLength(255)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_TEL_NUMTELEFONO_TMP)
                    .HasMaxLength(255)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_UBIC_BARRIO)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_UBIC_CANTON)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_UBIC_DISTRITO)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_UBIC_OTRASSENAS)
                    .HasMaxLength(160)
                    .IsUnicode(false);

                entity.Property(e => e.EMISOR_UBIC_PROVINCIA)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.ENVIO_CORREO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'")
                    .IsFixedLength();

                entity.Property(e => e.ESTADO)
                    .HasMaxLength(50)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'P'");

                entity.Property(e => e.ESTADO_CLIENTE)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.ESTADO_HACIENDA)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.EXPORTADO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'");

                entity.Property(e => e.FECHAEMISION)
                    .HasMaxLength(40)
                    .IsUnicode(false);

                entity.Property(e => e.FECHA_VENCIMIENTO).HasColumnType("DATE");

                entity.Property(e => e.FORMATO)
                    .HasMaxLength(2)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'AM'")
                    .IsFixedLength();

                entity.Property(e => e.FORMULARIO)
                    .HasMaxLength(500)
                    .IsUnicode(false);

                entity.Property(e => e.ID_FACTURA_ORACLE).HasColumnType("NUMBER");

                entity.Property(e => e.IMPRESO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'")
                    .IsFixedLength();

                entity.Property(e => e.INFOREF_CODIGO)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.INFOREF_DOC)
                    .HasMaxLength(200)
                    .IsUnicode(false);

                entity.Property(e => e.INFOREF_FECHAEMISION)
                    .HasMaxLength(40)
                    .IsUnicode(false);

                entity.Property(e => e.INFOREF_NUMERO)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.INFOREF_RAZON)
                    .HasMaxLength(180)
                    .IsUnicode(false);

                entity.Property(e => e.INFOREF_TIPODOC)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.LOTE).HasColumnType("CLOB");

                entity.Property(e => e.MEDIOSPAGO)
                    .HasMaxLength(250)
                    .IsUnicode(false);

                entity.Property(e => e.MENSAJE_HACIENDA).IsUnicode(false);

                entity.Property(e => e.MOTIVO_NC)
                    .HasMaxLength(255)
                    .IsUnicode(false);

                entity.Property(e => e.NOMBRE_VENDEDOR)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.NORMAVIGENTE_FECHARESOLUCION)
                    .HasMaxLength(250)
                    .IsUnicode(false);

                entity.Property(e => e.NORMAVIGENTE_NUMRESOLUCION)
                    .HasMaxLength(250)
                    .IsUnicode(false);

                entity.Property(e => e.NUMEROCONSECUTIVO)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.NUMEROS_FORMULARIO)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.NUMERO_BOLETA)
                    .HasMaxLength(255)
                    .IsUnicode(false);

                entity.Property(e => e.NUMERO_INFORME_GASTO)
                    .HasMaxLength(250)
                    .IsUnicode(false);

                entity.Property(e => e.NUMERO_LINEA_FACTURA)
                    .HasMaxLength(250)
                    .IsUnicode(false);

                entity.Property(e => e.NUM_FACTURA_ORACLE)
                    .HasMaxLength(250)
                    .IsUnicode(false);

                entity.Property(e => e.OBSERVACION)
                    .HasMaxLength(1024)
                    .IsUnicode(false);

                entity.Property(e => e.OBSERVACIONES)
                    .HasMaxLength(500)
                    .IsUnicode(false);

                entity.Property(e => e.ORDEN_COMPRA)
                    .HasMaxLength(500)
                    .IsUnicode(false);

                entity.Property(e => e.PAIS_ORIGEN)
                    .HasMaxLength(500)
                    .IsUnicode(false);

                entity.Property(e => e.PESOBRUTO).HasColumnType("NUMBER(15,2)");

                entity.Property(e => e.PESOBRUTO_KG).HasColumnType("NUMBER(15,2)");

                entity.Property(e => e.PESONETO).HasColumnType("NUMBER(15,2)");

                entity.Property(e => e.PESONETO_KG).HasColumnType("NUMBER(15,2)");

                entity.Property(e => e.PLAZOCREDITO)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.PROVEEDOR_SISTEMAS)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_COD_ACTIVIDAD)
                    .HasMaxLength(6)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_CORREOELECTRONICO)
                    .HasMaxLength(500)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_FAX_CODIGOPAIS).HasColumnType("NUMBER(22)");

                entity.Property(e => e.RECEPTOR_FAX_NUMTELEFONO)
                    .HasMaxLength(255)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_FAX_NUMTELEFONO_TMP)
                    .HasMaxLength(255)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_IDEXTRANJERO)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_ID_NUMERO)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_ID_TIPO)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_NOMBRE)
                    .HasMaxLength(200)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_NOMBRECOMERCIAL)
                    .HasMaxLength(200)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_TEL_CODIGOPAIS).HasColumnType("NUMBER(22)");

                entity.Property(e => e.RECEPTOR_TEL_NUMTELEFONO)
                    .HasMaxLength(255)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_TEL_NUMTELEFONO_TMP)
                    .HasMaxLength(255)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_UBIC_BARRIO)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_UBIC_CANTON)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_UBIC_DISTRITO)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_UBIC_OTRASSENAS)
                    .HasMaxLength(500)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_UBIC_PROVINCIA)
                    .HasMaxLength(2)
                    .IsUnicode(false);

                entity.Property(e => e.REGENERADO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'")
                    .IsFixedLength();

                entity.Property(e => e.REIMPRIME)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'")
                    .IsFixedLength();

                entity.Property(e => e.SUCURSAL)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.TARIMAS)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.TERMINO_ENVIO)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.TIENDA_WALMART)
                    .HasMaxLength(255)
                    .IsUnicode(false);

                entity.Property(e => e.TIPOCAMBIO).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TIPODOC)
                    .HasMaxLength(2)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.TIPO_DOC)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.TOTALCOMPROBANTE).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALDESCUENTOS).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALEXENTO).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALEXONERADO)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.TOTALGRAVADO).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALIMPUESTO).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALIVADEVUELTO)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.TOTALMERCANCIASEXENTAS).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALMERCANCIASGRAVADAS).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALMERCEXONERADA)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.TOTALMERCNOSUJETA)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.TOTALNOSUJETO)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.TOTALOTROSCARGOS)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.TOTALSERVEXENTOS).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALSERVEXONERADO)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.TOTALSERVGRAVADOS).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALSERVNOSUJETO)
                    .HasColumnType("NUMBER(18,5)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.TOTALVENTA).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TOTALVENTANETA).HasColumnType("NUMBER(18,5)");

                entity.Property(e => e.TRAMITAFACTURA)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'")
                    .IsFixedLength();

                entity.Property(e => e.TRANSPORTISTA)
                    .HasMaxLength(250)
                    .IsUnicode(false);

                entity.Property(e => e.TRASLADO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'")
                    .IsFixedLength();

                entity.Property(e => e.VENDEDOR_WALMART)
                    .HasMaxLength(255)
                    .IsUnicode(false);
            });

            modelBuilder.HasSequence("CXC_SEQ_AUTORIZA");

            modelBuilder.HasSequence("CXP_GEN_PAGO");

            modelBuilder.HasSequence("DESCTOESPECIAL");

            modelBuilder.HasSequence("GENRASTREOSEQ");

            modelBuilder.HasSequence("NLOG_FACTURA_ELEC_SEQ");

            modelBuilder.HasSequence("PLAPAGOSEC");

            modelBuilder.HasSequence("PLAREPORTESEQ");

            modelBuilder.HasSequence("RHACCION");

            modelBuilder.HasSequence("RHSOLIC");

            modelBuilder.HasSequence("SEQ_DEPOSITOS");

            modelBuilder.HasSequence("SEQ_ECF_CARGA_LOG");

            modelBuilder.HasSequence("SEQ_ECF_DESC");

            modelBuilder.HasSequence("SEQ_ECF_DETALLE");

            modelBuilder.HasSequence("SEQ_ECF_ENCABEZADO");

            modelBuilder.HasSequence("SEQ_HCM_ACCIONES_PERSONALES");

            modelBuilder.HasSequence("SEQ_HCM_CONTROL_ARCHIVOS");

            modelBuilder.HasSequence("SEQ_HCM_DEMOGRAFICOS");

            modelBuilder.HasSequence("SEQ_ORDEN");

            modelBuilder.HasSequence("SEQ_RECIBOS");

            modelBuilder.HasSequence("SEQ_RECLAMO");

            modelBuilder.HasSequence("SEQ_SOLICITUD");

            modelBuilder.HasSequence("SEQ_TOMAFIS");

            modelBuilder.HasSequence("VENBITFACQ");

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
