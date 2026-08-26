using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SolicitudesDescuentos.Modelslanco;

namespace SolicitudesDescuentos.Data
{
    public partial class LancoDbContext : DbContext
    {
        public LancoDbContext()
        {
        }

        public LancoDbContext(DbContextOptions<LancoDbContext> options)
            : base(options)
        {
        }

        public virtual DbSet<CXCDETFACREC> CXCDETFACRECs { get; set; } = null!;
        public virtual DbSet<CXCDETFACRECBIT> CXCDETFACRECBITs { get; set; } = null!;
        public virtual DbSet<CXCENCFACREC> CXCENCFACRECs { get; set; } = null!;
        public virtual DbSet<CXC_AGE_COBRO> CXC_AGE_COBROs { get; set; } = null!;
        public virtual DbSet<CXC_CLIENTE_COBRO> CXC_CLIENTE_COBROs { get; set; } = null!;
        public virtual DbSet<CXC_DETAGE_COBRO> CXC_DETAGE_COBROs { get; set; } = null!;
        public virtual DbSet<CXC_EMPLEADO_COBRO> CXC_EMPLEADO_COBROs { get; set; } = null!;
        public virtual DbSet<LOG_ENVIO_PDF_ORACLE> LOG_ENVIO_PDF_ORACLEs { get; set; } = null!;
        public virtual DbSet<PLAEMPLEADO> PLAEMPLEADOs { get; set; } = null!;
        public virtual DbSet<VENDOCENCFED> VENDOCENCFEDs { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("NUEVO");

            modelBuilder.Entity<CXCDETFACREC>(entity =>
            {
                entity.HasKey(e => new { e.COD_CIA, e.SUCURSAL, e.DOCUMENTO, e.SECUENCIA, e.CLAVE })
                    .HasName("CXCDETFACREC_PK");

                entity.ToTable("CXCDETFACREC");

                entity.HasIndex(e => new { e.COD_CIA, e.CLAVE }, "CXCDETFACREC_CLAVE_ID");

                entity.Property(e => e.COD_CIA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.SUCURSAL)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.DOCUMENTO)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.SECUENCIA).HasPrecision(5);

                entity.Property(e => e.CLAVE)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.COD_CLIENTE)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.FACTURAELEC)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.FACTURAINT)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.FECHA)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.NOMBRE_CLIENTE)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.RUTA)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.HasOne(d => d.CXCENCFACREC)
                    .WithMany(p => p.CXCDETFACRECs)
                    .HasForeignKey(d => new { d.COD_CIA, d.SUCURSAL, d.DOCUMENTO })
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("CXCDETFACREC_DET_FK");
            });

            modelBuilder.Entity<CXCDETFACRECBIT>(entity =>
            {
                entity.HasKey(e => new { e.COD_CIA, e.SUCURSAL, e.DOCUMENTO, e.SECUENCIA, e.CLAVE })
                    .HasName("CXCDETFACRECBIT_PK");

                entity.ToTable("CXCDETFACRECBIT");

                entity.HasIndex(e => new { e.COD_CIA, e.CLAVE }, "CXCDETFACRECBIT_CLAVE_ID");

                entity.Property(e => e.COD_CIA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.SUCURSAL)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.DOCUMENTO)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.SECUENCIA).HasPrecision(5);

                entity.Property(e => e.CLAVE)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.COD_CLIENTE)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.FACTURAELEC)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.FACTURAINT)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.FECHA).HasColumnType("DATE");

                entity.Property(e => e.NOMBRE_CLIENTE)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.OBSERVACIONES)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.RUTA)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.HasOne(d => d.CXCENCFACREC)
                    .WithMany(p => p.CXCDETFACRECBITs)
                    .HasForeignKey(d => new { d.COD_CIA, d.SUCURSAL, d.DOCUMENTO })
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("CXCDETFACREC_DETBIT_FK");
            });

            modelBuilder.Entity<CXCENCFACREC>(entity =>
            {
                entity.HasKey(e => new { e.COD_CIA, e.SUCURSAL, e.DOCUMENTO })
                    .HasName("CXCENCFACREC_PK");

                entity.ToTable("CXCENCFACREC");

                entity.Property(e => e.COD_CIA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.SUCURSAL)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.DOCUMENTO)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.CONSIGNATARIO)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.ESTADO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'P'")
                    .IsFixedLength();

                entity.Property(e => e.FECHA).HasColumnType("DATE");

                entity.Property(e => e.OBSERVACIONES)
                    .HasMaxLength(200)
                    .IsUnicode(false);

                entity.Property(e => e.TIPOPERSONA)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'T' ")
                    .IsFixedLength();

                entity.Property(e => e.USUARIO)
                    .HasMaxLength(30)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<CXC_AGE_COBRO>(entity =>
            {
                entity.HasKey(e => new { e.COD_CIA, e.SUCURSAL, e.COD_AGENTE, e.ANO_FISCAL, e.PER_PROCESO, e.COD_COMISION })
                    .HasName("CXC_AGE_COBRO_PK");

                entity.ToTable("CXC_AGE_COBRO");

                entity.Property(e => e.COD_CIA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.SUCURSAL)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.COD_AGENTE)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.ANO_FISCAL).HasPrecision(4);

                entity.Property(e => e.PER_PROCESO).HasPrecision(2);

                entity.Property(e => e.COD_COMISION)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.COBROBRUTO)
                    .HasColumnType("NUMBER(14,2)")
                    .HasDefaultValueSql("0 ");

                entity.Property(e => e.LOCAL1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ");

                entity.Property(e => e.MON_COBRADO).HasColumnType("NUMBER(14,2)");

                entity.Property(e => e.MON_COMISION).HasColumnType("NUMBER(14,2)");

                entity.Property(e => e.POSFECOBMES).HasColumnType("NUMBER(14,2)");

                entity.Property(e => e.POSFENOCOB).HasColumnType("NUMBER(14,2)");

                entity.Property(e => e.REPLICA1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ");
            });

            modelBuilder.Entity<CXC_CLIENTE_COBRO>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("CXC_CLIENTE_COBRO");

                entity.Property(e => e.ANO_FISCAL).HasPrecision(4);

                entity.Property(e => e.COBROBRUTO)
                    .HasColumnType("NUMBER(14,2)")
                    .HasDefaultValueSql("0                     ");

                entity.Property(e => e.COD_AGENTE)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.COD_CIA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.COD_CLIENTE)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.MON_COBRADO).HasColumnType("NUMBER(14,2)");

                entity.Property(e => e.MON_COMISION).HasColumnType("NUMBER(14,2)");

                entity.Property(e => e.PER_PROCESO).HasPrecision(2);
            });

            modelBuilder.Entity<CXC_DETAGE_COBRO>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("CXC_DETAGE_COBRO");

                entity.HasIndex(e => new { e.COD_CIA, e.SUCURSAL, e.TIP_DOC, e.NUM_DOC, e.COD_CLIENTE, e.DOCUMENTO, e.FACTURA, e.ANO_FISCAL, e.PER_PROCESO }, "CXC_DETAGE_COBRO");

                entity.Property(e => e.ANO_FISCAL).HasPrecision(4);

                entity.Property(e => e.COD_AGENTE)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.COD_CIA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.COD_CLIENTE)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.COD_COMISION)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.COD_MONEDA)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.DESCUENTO)
                    .HasColumnType("NUMBER(14,2)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.DOCUMENTO)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.FACTURA)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.FECHADOC).HasColumnType("DATE");

                entity.Property(e => e.FECHAFACTURA).HasColumnType("DATE");

                entity.Property(e => e.IMPUESTO)
                    .HasColumnType("NUMBER(14,2)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.LINEA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.MONTO)
                    .HasColumnType("NUMBER(14,2)")
                    .HasDefaultValueSql("0  ");

                entity.Property(e => e.MONTOFACTURA)
                    .HasColumnType("NUMBER(14,2)")
                    .HasDefaultValueSql("0  ");

                entity.Property(e => e.MON_COBRADO)
                    .HasColumnType("NUMBER(14,2)")
                    .HasDefaultValueSql("0  ");

                entity.Property(e => e.MON_COMISION)
                    .HasColumnType("NUMBER(14,2)")
                    .HasDefaultValueSql("0 ");

                entity.Property(e => e.NUM_DOC)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.PER_PROCESO).HasPrecision(2);

                entity.Property(e => e.SUCURSAL)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.TIP_DOC)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();
            });

            modelBuilder.Entity<CXC_EMPLEADO_COBRO>(entity =>
            {
                entity.HasKey(e => new { e.COD_CIA, e.COD_CLIENTE, e.EMPLEADO, e.ANO_FISCAL, e.PER_PROCESO })
                    .HasName("CXC_EMPLEADO_COBRO_PK");

                entity.ToTable("CXC_EMPLEADO_COBRO");

                entity.Property(e => e.COD_CIA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.COD_CLIENTE)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.EMPLEADO)
                    .HasMaxLength(15)
                    .IsUnicode(false);

                entity.Property(e => e.ANO_FISCAL).HasPrecision(4);

                entity.Property(e => e.PER_PROCESO).HasPrecision(2);

                entity.Property(e => e.COBROBRUTO)
                    .HasColumnType("NUMBER(14,2)")
                    .HasDefaultValueSql("0     ");

                entity.Property(e => e.COD_AGENTE)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.MON_COBRADO)
                    .HasColumnType("NUMBER(14,2)")
                    .HasDefaultValueSql("0 ");

                entity.Property(e => e.MON_COMISION)
                    .HasColumnType("NUMBER(14,2)")
                    .HasDefaultValueSql("0 ");

                entity.Property(e => e.PORCENTAJE).HasColumnType("NUMBER(5,2)");
            });

            modelBuilder.Entity<LOG_ENVIO_PDF_ORACLE>(entity =>
            {
                entity.HasKey(e => e.ID_LOG);

                entity.ToTable("LOG_ENVIO_PDF_ORACLE");

                entity.Property(e => e.ID_LOG)
                    .HasColumnType("NUMBER")
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.DOCUMENTO)
                    .HasMaxLength(50)
                    .IsUnicode(false)
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.ESTADO)
                    .HasMaxLength(20)
                    .IsUnicode(false)
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.FECHA_INTENTO)
                    .HasColumnType("DATE")
                    .ValueGeneratedOnAdd()
                    .HasDefaultValueSql("SYSDATE ");

                entity.Property(e => e.MENSAJE)
                    .HasMaxLength(1000)
                    .IsUnicode(false)
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.NOMBRE_ARCHIVO)
                    .HasMaxLength(255)
                    .IsUnicode(false)
                    .ValueGeneratedOnAdd();
            });

            modelBuilder.Entity<PLAEMPLEADO>(entity =>
            {
                entity.HasKey(e => new { e.CIA, e.EMPLEADO })
                    .HasName("PLAEMPLEADO_PK");

                entity.ToTable("PLAEMPLEADO");

                entity.HasIndex(e => new { e.CIA, e.PUESTO, e.CATEGORIA }, "PLACATEGORIA_EMPLEADO_FK");

                entity.HasIndex(e => new { e.CIA, e.SUCURSAL, e.DEPARTAMENTO }, "PLADEPARTAMENTO_EMPLEADO_FK");

                entity.HasIndex(e => new { e.CIA, e.JEFEINMEDIATO }, "PLAEMPLEADO_JEFEINMEDIATO_FK");

                entity.HasIndex(e => new { e.CIA, e.HORARIO }, "PLAHORARIO_EMPLEADO_FK");

                entity.HasIndex(e => new { e.CIA, e.PLANILLA }, "PLAPLANILLA_EMPLEADO_FK");

                entity.HasIndex(e => new { e.CIA, e.BANCO }, "PLATEF_EMPLEADO_FK");

                entity.Property(e => e.CIA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .ValueGeneratedOnAdd()
                    .IsFixedLength();

                entity.Property(e => e.EMPLEADO)
                    .HasMaxLength(15)
                    .IsUnicode(false)
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.ACTUALIZA)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'")
                    .IsFixedLength();

                entity.Property(e => e.ACTUALIZACLAVE)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ")
                    .IsFixedLength();

                entity.Property(e => e.ALQUILERVEHICULO)
                    .HasColumnType("NUMBER(14,2)")
                    .HasDefaultValueSql("0 ");

                entity.Property(e => e.ASOCIACION)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.BANCO)
                    .HasMaxLength(12)
                    .IsUnicode(false);

                entity.Property(e => e.CATEGORIA)
                    .HasMaxLength(2)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.CEDULA)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.CEDULANUEVA)
                    .HasMaxLength(15)
                    .IsUnicode(false);

                entity.Property(e => e.CLAVE)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.COD_CLIENTE)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.CONYUGUE)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.CUENTA)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.DEPARTAMENTO)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.DEPENDIENTES).HasPrecision(2);

                entity.Property(e => e.DIRECCION)
                    .HasMaxLength(512)
                    .IsUnicode(false);

                entity.Property(e => e.EMAIL)
                    .HasMaxLength(60)
                    .IsUnicode(false);

                entity.Property(e => e.ESTADO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .ValueGeneratedOnAdd()
                    .IsFixedLength();

                entity.Property(e => e.ESTADOCIVIL)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.FACEID)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.FECHAINGRESO).HasColumnType("DATE");

                entity.Property(e => e.FECHANAC).HasColumnType("DATE");

                entity.Property(e => e.FECHASALIDA).HasColumnType("DATE");

                entity.Property(e => e.FOTO).HasColumnType("BLOB");

                entity.Property(e => e.GANACOMIS)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.GANAEXTRAS)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.HIJOS).HasPrecision(2);

                entity.Property(e => e.HIJOSMAY).HasPrecision(2);

                entity.Property(e => e.HORARIO)
                    .HasMaxLength(2)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.HUELLA)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.ID_ORACLE)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.IND_RECIBCORR)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'")
                    .IsFixedLength();

                entity.Property(e => e.JEFEINMEDIATO)
                    .HasMaxLength(15)
                    .IsUnicode(false);

                entity.Property(e => e.LOCAL1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ");

                entity.Property(e => e.MARCATARJETA)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.MONEDACOMPROBANTE)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'CL' ")
                    .IsFixedLength();

                entity.Property(e => e.MONEDASALARIO)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'CL' ");

                entity.Property(e => e.NOMBRE)
                    .HasMaxLength(40)
                    .IsUnicode(false);

                entity.Property(e => e.ORDEN).HasColumnType("NUMBER(38)");

                entity.Property(e => e.PENSIONADO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.PENSIONVOLUN)
                    .HasColumnType("NUMBER(14,2)")
                    .HasDefaultValueSql("0 ");

                entity.Property(e => e.PLANILLA)
                    .HasMaxLength(2)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.PLANILLAWEB)
                    .HasMaxLength(2)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.PROCESO)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.PROCESO2)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.PRODUC)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.PUESTO)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.REPLICA1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ");

                entity.Property(e => e.SALARIOPRD).HasColumnType("NUMBER(10,4)");

                entity.Property(e => e.SEGUROSOCIAL)
                    .HasMaxLength(15)
                    .IsUnicode(false);

                entity.Property(e => e.SEXO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.SUCURSAL)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.TELEFONO)
                    .HasMaxLength(12)
                    .IsUnicode(false);

                entity.Property(e => e.TIPOID).HasPrecision(1);

                entity.Property(e => e.TIPOPAGO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.USUARIO)
                    .HasMaxLength(30)
                    .IsUnicode(false);
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
                    .HasMaxLength(50)
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
                    .HasMaxLength(200)
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
                    .HasMaxLength(500)
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
                    .HasMaxLength(500)
                    .IsUnicode(false);

                entity.Property(e => e.RECEPTOR_FAX_CODIGOPAIS).HasPrecision(3);

                entity.Property(e => e.RECEPTOR_FAX_NUMTELEFONO).HasColumnType("NUMBER(20)");

                entity.Property(e => e.RECEPTOR_IDEXTRANJERO)
                    .HasMaxLength(1)
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

            modelBuilder.HasSequence("SEQ_EJECUCIONES");

            modelBuilder.HasSequence("SEQ_HCM_ACCIONES_PERSONALES");

            modelBuilder.HasSequence("SEQ_HCM_CONTROL_ARCHIVOS");

            modelBuilder.HasSequence("SEQ_HCM_DEMOGRAFICOS");

            modelBuilder.HasSequence("SEQ_LOG_ENVIO_PDF_ORACLE");

            modelBuilder.HasSequence("SEQ_LOG_PO_INTERFACE");

            modelBuilder.HasSequence("SEQ_ORDEN");

            modelBuilder.HasSequence("SEQ_PO_BATCH_ID");

            modelBuilder.HasSequence("SEQ_PO_HEADER_KEY");

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
