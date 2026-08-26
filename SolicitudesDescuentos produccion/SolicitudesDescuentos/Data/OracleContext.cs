using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SolicitudesDescuentos.ModelsOracle;

namespace SolicitudesDescuentos.Data
{
    public partial class OracleContext : DbContext
    {
        public OracleContext()
        {
        }

        public OracleContext(DbContextOptions<OracleContext> options)
            : base(options)
        {
        }

        public virtual DbSet<ART_DET_NO_PROMO> ART_DET_NO_PROMOs { get; set; } = null!;
        public virtual DbSet<ART_NO_PROMO> ART_NO_PROMOs { get; set; } = null!;
        public virtual DbSet<CXC_AGE_COBRO> CXC_AGE_COBROs { get; set; } = null!;
        public virtual DbSet<CXC_CLIENTE_COBRO> CXC_CLIENTE_COBROs { get; set; } = null!;
        public virtual DbSet<CXC_DETAGE_COBRO> CXC_DETAGE_COBROs { get; set; } = null!;
        public virtual DbSet<CXC_DET_COMISION> CXC_DET_COMISIONs { get; set; } = null!;
        public virtual DbSet<CXC_EMPLEADO_COBRO> CXC_EMPLEADO_COBROs { get; set; } = null!;
        public virtual DbSet<GENCLIENTEIMPULSADOR> GENCLIENTEIMPULSADORs { get; set; } = null!;
        public virtual DbSet<GEN_CLIENTE> GEN_CLIENTEs { get; set; } = null!;
        public virtual DbSet<GEN_MAS_COMISION> GEN_MAS_COMISIONs { get; set; } = null!;
        public virtual DbSet<GEN_VENDEDOR> GEN_VENDEDORs { get; set; } = null!;
        public virtual DbSet<IMPULSADORESORACLE> IMPULSADORESORACLEs { get; set; } = null!;
        public virtual DbSet<INV_ARTICULO> INV_ARTICULOs { get; set; } = null!;
        public virtual DbSet<INV_CLASE> INV_CLASEs { get; set; } = null!;
        public virtual DbSet<INV_LINEA> INV_LINEAs { get; set; } = null!;
        public virtual DbSet<INV_MEDIDum> INV_MEDIDAs { get; set; } = null!;
        public virtual DbSet<PREDESCLASEORACLE> PREDESCLASEORACLEs { get; set; } = null!;
        public virtual DbSet<PREDESCUENTO> PREDESCUENTOs { get; set; } = null!;
        public virtual DbSet<PREDESCUENTOS_MASTER> PREDESCUENTOS_MASTERs { get; set; } = null!;
        public virtual DbSet<PREDETDESCUENTO> PREDETDESCUENTOs { get; set; } = null!;
        public virtual DbSet<XXORA_COMISIONE> XXORA_COMISIONEs { get; set; } = null!;
        public virtual DbSet<XXORA_CUSTOMER_MASTER> XXORA_CUSTOMER_MASTERs { get; set; } = null!;
        public virtual DbSet<XXORA_DISCOUNT_LIST> XXORA_DISCOUNT_LISTs { get; set; } = null!;
        public virtual DbSet<XXORA_ITEM_MASTER> XXORA_ITEM_MASTERs { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("BG_INTUSER")
                .UseCollation("USING_NLS_COMP");

            modelBuilder.Entity<ART_DET_NO_PROMO>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("ART_DET_NO_PROMO");

                entity.HasIndex(e => new { e.BU_NAME, e.ORGANIZATION_CODE, e.ITEM_NUMBER }, "IX_ADNP_HDR");

                entity.HasIndex(e => new { e.BU_NAME, e.ORGANIZATION_CODE, e.ITEM_NUMBER, e.RULE_DISCOUNT_NAME, e.PARTY_NUMBER, e.START_DATE }, "UQ_ADNP_DET")
                    .IsUnique();

                entity.Property(e => e.BU_NAME)
                    .HasMaxLength(240)
                    .IsUnicode(false);

                entity.Property(e => e.DISCOUNT_PRICE).HasColumnType("NUMBER");

                entity.Property(e => e.END_DATE).HasPrecision(6);

                entity.Property(e => e.ITEM_NUMBER)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.ORGANIZATION_CODE)
                    .HasMaxLength(500)
                    .IsUnicode(false);

                entity.Property(e => e.PARTY_NUMBER)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.PRICING_UOM_CODE)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.RULE_DISCOUNT_NAME)
                    .HasMaxLength(80)
                    .IsUnicode(false);

                entity.Property(e => e.START_DATE).HasPrecision(6);

                entity.HasOne(d => d.ART_NO_PROMO)
                    .WithMany()
                    .HasForeignKey(d => new { d.BU_NAME, d.ORGANIZATION_CODE, d.ITEM_NUMBER })
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("FK_ART_DET_NO_PROMO_HDR");
            });

            modelBuilder.Entity<ART_NO_PROMO>(entity =>
            {
                entity.HasKey(e => new { e.BU_NAME, e.ORGANIZATION_CODE, e.ITEM_NUMBER });

                entity.ToTable("ART_NO_PROMO");

                entity.Property(e => e.BU_NAME)
                    .HasMaxLength(240)
                    .IsUnicode(false);

                entity.Property(e => e.ORGANIZATION_CODE)
                    .HasMaxLength(500)
                    .IsUnicode(false);

                entity.Property(e => e.ITEM_NUMBER)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.ESTADO)
                    .HasMaxLength(25)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'PENDIENTE' ");

                entity.Property(e => e.GENERADO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N' ")
                    .IsFixedLength();
            });

            modelBuilder.Entity<CXC_AGE_COBRO>(entity =>
            {
                entity.HasKey(e => new { e.COD_CIA, e.SUCURSAL, e.ANO_FISCAL, e.PER_PROCESO, e.COD_AGENTE, e.COD_COMISION })
                    .HasName("CXC_AGE_COBRO_PK");

                entity.ToTable("CXC_AGE_COBRO");

                entity.HasIndex(e => new { e.COD_CIA, e.COD_AGENTE, e.ANO_FISCAL, e.PER_PROCESO }, "CXC_AGE_COBRO_AGENTE_IX");

                entity.Property(e => e.COD_CIA)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.SUCURSAL)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.ANO_FISCAL).HasPrecision(4);

                entity.Property(e => e.PER_PROCESO).HasPrecision(2);

                entity.Property(e => e.COD_AGENTE)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.COD_COMISION)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.COBROBRUTO)
                    .HasColumnType("NUMBER(18,2)")
                    .HasDefaultValueSql("0          ");

                entity.Property(e => e.LOCAL1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S'    ");

                entity.Property(e => e.MON_COBRADO)
                    .HasColumnType("NUMBER(18,2)")
                    .HasDefaultValueSql("0          ");

                entity.Property(e => e.MON_COMISION)
                    .HasColumnType("NUMBER(18,2)")
                    .HasDefaultValueSql("0          ");

                entity.Property(e => e.REPLICA1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S'    ");
            });

            modelBuilder.Entity<CXC_CLIENTE_COBRO>(entity =>
            {
                entity.HasKey(e => new { e.COD_CIA, e.ANO_FISCAL, e.PER_PROCESO, e.COD_AGENTE, e.COD_CLIENTE })
                    .HasName("CXC_CLIENTE_COBRO_PK");

                entity.ToTable("CXC_CLIENTE_COBRO");

                entity.HasIndex(e => new { e.COD_CIA, e.COD_CLIENTE, e.ANO_FISCAL, e.PER_PROCESO }, "CXC_CLIENTE_COBRO_CLIENTE_IX");

                entity.Property(e => e.COD_CIA)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.ANO_FISCAL).HasPrecision(4);

                entity.Property(e => e.PER_PROCESO).HasPrecision(2);

                entity.Property(e => e.COD_AGENTE)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.COD_CLIENTE)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.COBROBRUTO)
                    .HasColumnType("NUMBER(18,2)")
                    .HasDefaultValueSql("0          ");

                entity.Property(e => e.MON_COBRADO)
                    .HasColumnType("NUMBER(18,2)")
                    .HasDefaultValueSql("0          ");

                entity.Property(e => e.MON_COMISION)
                    .HasColumnType("NUMBER(18,2)")
                    .HasDefaultValueSql("0          ");
            });

            modelBuilder.Entity<CXC_DETAGE_COBRO>(entity =>
            {
                entity.HasKey(e => new { e.COD_CIA, e.SUCURSAL, e.ANO_FISCAL, e.PER_PROCESO, e.COD_AGENTE, e.COD_CLIENTE, e.COD_COMISION, e.DOCUMENTO, e.LINEA })
                    .HasName("CXC_DETAGE_COBRO_PK");

                entity.ToTable("CXC_DETAGE_COBRO");

                entity.HasIndex(e => new { e.COD_CIA, e.COD_CLIENTE, e.ANO_FISCAL, e.PER_PROCESO }, "CXC_DETAGE_COBRO_CLIENTE_IX");

                entity.HasIndex(e => new { e.COD_CIA, e.DOCUMENTO }, "CXC_DETAGE_COBRO_DOC_IX");

                entity.Property(e => e.COD_CIA)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.SUCURSAL)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.ANO_FISCAL).HasPrecision(4);

                entity.Property(e => e.PER_PROCESO).HasPrecision(2);

                entity.Property(e => e.COD_AGENTE)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.COD_CLIENTE)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.COD_COMISION)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.DOCUMENTO)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.LINEA)
                    .HasPrecision(10)
                    .HasDefaultValueSql("1            ");

                entity.Property(e => e.COD_MONEDA)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.DESCUENTO)
                    .HasColumnType("NUMBER(18,2)")
                    .HasDefaultValueSql("0          ");

                entity.Property(e => e.FACTURA)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.FECHADOC).HasColumnType("DATE");

                entity.Property(e => e.FECHAFACTURA).HasColumnType("DATE");

                entity.Property(e => e.IMPUESTO)
                    .HasColumnType("NUMBER(18,2)")
                    .HasDefaultValueSql("0          ");

                entity.Property(e => e.MONTO)
                    .HasColumnType("NUMBER(18,2)")
                    .HasDefaultValueSql("0          ");

                entity.Property(e => e.MON_COBRADO)
                    .HasColumnType("NUMBER(18,2)")
                    .HasDefaultValueSql("0          ");

                entity.Property(e => e.MON_COMISION)
                    .HasColumnType("NUMBER(18,2)")
                    .HasDefaultValueSql("0          ");

                entity.Property(e => e.NUM_DOC)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.TIP_DOC)
                    .HasMaxLength(20)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<CXC_DET_COMISION>(entity =>
            {
                entity.HasKey(e => new { e.BU_NOMBRE, e.COD_COMISION, e.MONTO_COBRADO })
                    .HasName("CXC_DET_COMISION_PK");

                entity.ToTable("CXC_DET_COMISION");

                entity.Property(e => e.BU_NOMBRE)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.COD_COMISION)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.MONTO_COBRADO).HasColumnType("NUMBER(14,2)");

                entity.Property(e => e.LOCAL1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ");

                entity.Property(e => e.PORCENTAJE_COMISION).HasColumnType("NUMBER(5,2)");

                entity.Property(e => e.REPLICA1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ");
            });

            modelBuilder.Entity<CXC_EMPLEADO_COBRO>(entity =>
            {
                entity.HasKey(e => new { e.COD_CIA, e.ANO_FISCAL, e.PER_PROCESO, e.COD_AGENTE, e.COD_CLIENTE, e.EMPLEADO })
                    .HasName("CXC_EMPLEADO_COBRO_PK");

                entity.ToTable("CXC_EMPLEADO_COBRO");

                entity.HasIndex(e => new { e.COD_CIA, e.EMPLEADO, e.ANO_FISCAL, e.PER_PROCESO }, "CXC_EMPLEADO_COBRO_EMP_IX");

                entity.Property(e => e.COD_CIA)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.ANO_FISCAL).HasPrecision(4);

                entity.Property(e => e.PER_PROCESO).HasPrecision(2);

                entity.Property(e => e.COD_AGENTE)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.COD_CLIENTE)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.EMPLEADO)
                    .HasMaxLength(15)
                    .IsUnicode(false);

                entity.Property(e => e.COBROBRUTO)
                    .HasColumnType("NUMBER(18,2)")
                    .HasDefaultValueSql("0          ");

                entity.Property(e => e.MON_COBRADO)
                    .HasColumnType("NUMBER(18,2)")
                    .HasDefaultValueSql("0          ");

                entity.Property(e => e.MON_COMISION)
                    .HasColumnType("NUMBER(18,2)")
                    .HasDefaultValueSql("0          ");

                entity.Property(e => e.PORCENTAJE)
                    .HasColumnType("NUMBER(7,2)")
                    .HasDefaultValueSql("0           ");
            });

            modelBuilder.Entity<GENCLIENTEIMPULSADOR>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("GENCLIENTEIMPULSADOR");

                entity.Property(e => e.BU_NOMBRE)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.CLIENTE)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.EMPLEADO)
                    .HasMaxLength(15)
                    .IsUnicode(false);

                entity.Property(e => e.NOM_EMPLEADO)
                    .HasMaxLength(40)
                    .IsUnicode(false);

                entity.Property(e => e.PORCENTAJE)
                    .HasColumnType("NUMBER(7,2)")
                    .HasDefaultValueSql("0                     ");
            });

            modelBuilder.Entity<GEN_CLIENTE>(entity =>
            {
                entity.HasNoKey();

                entity.ToView("GEN_CLIENTES");

                entity.Property(e => e.IDCLIENTE)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.NOMBRE_CLIENTE)
                    .HasMaxLength(150)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<GEN_MAS_COMISION>(entity =>
            {
                entity.HasKey(e => new { e.BU_NOMBRE, e.COD_COMISION })
                    .HasName("GEN_MAS_COMISION_PK");

                entity.ToTable("GEN_MAS_COMISION");

                entity.HasIndex(e => e.BU_NOMBRE, "GENPARAMETRO_COMISION_FK2");

                entity.Property(e => e.BU_NOMBRE)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.COD_COMISION)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.COD_MONEDA)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.DES_COMISION)
                    .HasMaxLength(40)
                    .IsUnicode(false);

                entity.Property(e => e.LOCAL1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ");

                entity.Property(e => e.PROPORCIONAL)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.REPLICA1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ");

                entity.Property(e => e.TIPO_CALCULO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.TIPO_COMISION)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.VALOR)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .IsFixedLength();
            });

            modelBuilder.Entity<GEN_VENDEDOR>(entity =>
            {
                entity.HasNoKey();

                entity.ToView("GEN_VENDEDOR");

                entity.Property(e => e.BU_NOMBRE)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.CATEGORIA)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.IDVENDEDOR)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.NOMBRE_VENDEDOR)
                    .HasMaxLength(150)
                    .IsUnicode(false);

                entity.Property(e => e.REGISTRY_ID)
                    .HasMaxLength(20)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<IMPULSADORESORACLE>(entity =>
            {
                entity.HasKey(e => new { e.BU_NOMBRE, e.CLIENTE, e.EMPLEADO })
                    .HasName("IMPULSADORESORACLE_PK");

                entity.ToTable("IMPULSADORESORACLE");

                entity.Property(e => e.BU_NOMBRE)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.CLIENTE)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.EMPLEADO)
                    .HasMaxLength(15)
                    .IsUnicode(false);

                entity.Property(e => e.PORCENTAJE)
                    .HasColumnType("NUMBER(7,2)")
                    .HasDefaultValueSql("0                     ");
            });

            modelBuilder.Entity<INV_ARTICULO>(entity =>
            {
                entity.HasNoKey();

                entity.ToView("INV_ARTICULO");

                entity.Property(e => e.ACEPTADESCUENTO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.COD_ARTICULO)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.COD_CLASE)
                    .HasMaxLength(820)
                    .IsUnicode(false);

                entity.Property(e => e.COD_LINEA)
                    .HasMaxLength(820)
                    .IsUnicode(false);

                entity.Property(e => e.DES_ARTICULO)
                    .HasMaxLength(240)
                    .IsUnicode(false);

                entity.Property(e => e.DES_CLASE)
                    .HasMaxLength(250)
                    .IsUnicode(false);

                entity.Property(e => e.DES_LINEA)
                    .HasMaxLength(250)
                    .IsUnicode(false);

                entity.Property(e => e.MEDIDA)
                    .HasMaxLength(3)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<INV_CLASE>(entity =>
            {
                entity.HasNoKey();

                entity.ToView("INV_CLASE");

                entity.Property(e => e.CATEGORY_CODE)
                    .HasMaxLength(820)
                    .IsUnicode(false);

                entity.Property(e => e.CATEGORY_NAME)
                    .HasMaxLength(250)
                    .IsUnicode(false);

                entity.Property(e => e.SUBCATEGORY_CODE)
                    .HasMaxLength(820)
                    .IsUnicode(false);

                entity.Property(e => e.SUBCATEGORY_NAME)
                    .HasMaxLength(250)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<INV_LINEA>(entity =>
            {
                entity.HasNoKey();

                entity.ToView("INV_LINEA");

                entity.Property(e => e.CATEGORY_CODE)
                    .HasMaxLength(820)
                    .IsUnicode(false);

                entity.Property(e => e.CATEGORY_NAME)
                    .HasMaxLength(250)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<INV_MEDIDum>(entity =>
            {
                entity.HasNoKey();

                entity.ToView("INV_MEDIDA");

                entity.Property(e => e.PRIMARY_UOM_CODE)
                    .HasMaxLength(3)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<PREDESCLASEORACLE>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("PREDESCLASEORACLE");

                entity.HasIndex(e => new { e.ORGANIZATION_CODE, e.IDCLIENTE, e.CATEGORY_CODE, e.SUBCATEGORY_CODE, e.ITEM_NUMBER }, "PREDESCLASEORACLE_UQ")
                    .IsUnique();

                entity.Property(e => e.CATEGORY_CODE)
                    .HasMaxLength(820)
                    .IsUnicode(false);

                entity.Property(e => e.FECHA_FIN).HasColumnType("DATE");

                entity.Property(e => e.FECHA_INICIO).HasColumnType("DATE");

                entity.Property(e => e.IDCLIENTE)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.ITEM_NUMBER)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.ORGANIZATION_CODE)
                    .HasMaxLength(500)
                    .IsUnicode(false);

                entity.Property(e => e.PORCENTAJE).HasColumnType("NUMBER(5,2)");

                entity.Property(e => e.SUBCATEGORY_CODE)
                    .HasMaxLength(820)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<PREDESCUENTO>(entity =>
            {
                entity.HasKey(e => new { e.BU_NOMBRE, e.CONSECUTIVO });

                entity.ToTable("PREDESCUENTO");

                entity.Property(e => e.BU_NOMBRE)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.CONSECUTIVO)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.AUTORIZADO_POR)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.COD_CLIENTE)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.ESTADO)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.FECHAFIN).HasColumnType("DATE");

                entity.Property(e => e.FECHAINICIO).HasColumnType("DATE");

                entity.Property(e => e.FECHAREGISTRO).HasColumnType("DATE");

                entity.Property(e => e.FECHASOLICITUD).HasColumnType("DATE");

                entity.Property(e => e.FECHA_APLICACION).HasColumnType("DATE");

                entity.Property(e => e.GENERADO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N' ")
                    .IsFixedLength();

                entity.Property(e => e.INGRESADO_POR)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.OBSERVACIONES)
                    .HasMaxLength(1000)
                    .IsUnicode(false);

                entity.Property(e => e.TIPODESCUENTO)
                    .HasMaxLength(50)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<PREDESCUENTOS_MASTER>(entity =>
            {
                entity.HasKey(e => new { e.CONSECUTIVO, e.BU_NOMBRE, e.ORGANIZATION_CODE, e.COD_CLIENTE, e.COD_LINEA, e.COD_ARTICULO, e.COD_CLASE })
                    .HasName("PREDESCUENTOS_MASTER_AK");

                entity.ToTable("PREDESCUENTOS_MASTER");

                entity.Property(e => e.CONSECUTIVO)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.BU_NOMBRE)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.ORGANIZATION_CODE)
                    .HasMaxLength(500)
                    .IsUnicode(false);

                entity.Property(e => e.COD_CLIENTE)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.COD_LINEA)
                    .HasMaxLength(820)
                    .IsUnicode(false);

                entity.Property(e => e.COD_ARTICULO)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.COD_CLASE)
                    .HasMaxLength(820)
                    .IsUnicode(false);

                entity.Property(e => e.COD_USUARIO)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.FECHA)
                    .HasMaxLength(40)
                    .IsUnicode(false);

                entity.Property(e => e.FECHA_FIN).HasColumnType("DATE");

                entity.Property(e => e.FECHA_INICIO).HasColumnType("DATE");

                entity.Property(e => e.LOCAL1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ");

                entity.Property(e => e.MEDIDA)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.PORCENTAJE).HasColumnType("NUMBER(5,2)");

                entity.Property(e => e.REPLICA1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ");
            });

            modelBuilder.Entity<PREDETDESCUENTO>(entity =>
            {
                entity.HasKey(e => new { e.CONSECUTIVODETALLE, e.BU_NOMBRE })
                    .HasName("PK_PREDETDESCUENTOS");

                entity.ToTable("PREDETDESCUENTO");

                entity.Property(e => e.CONSECUTIVODETALLE).HasPrecision(10);

                entity.Property(e => e.BU_NOMBRE)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.COD_ARTICULO)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.COD_CLASE)
                    .HasMaxLength(820)
                    .IsUnicode(false);

                entity.Property(e => e.COD_CLIENTE)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.COD_LINEA)
                    .HasMaxLength(820)
                    .IsUnicode(false);

                entity.Property(e => e.CONSECUTIVO)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.FECHASOLICITUD).HasColumnType("DATE");

                entity.Property(e => e.TIPO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.VALOR).HasColumnType("NUMBER(14,2)");

                entity.HasOne(d => d.PREDESCUENTO)
                    .WithMany(p => p.PREDETDESCUENTOs)
                    .HasForeignKey(d => new { d.BU_NOMBRE, d.CONSECUTIVO })
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("FK_PREDESCUENTO");
            });

            modelBuilder.Entity<XXORA_COMISIONE>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("XXORA_COMISIONES");

                entity.Property(e => e.BU_NOMBRE)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.CANTIDAD_APLICADA).HasColumnType("NUMBER");

                entity.Property(e => e.CANTIDAD_PENDIENTE).HasColumnType("NUMBER");

                entity.Property(e => e.CHEQUE_DEVUELTO)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.DESCUENTO).HasColumnType("NUMBER");

                entity.Property(e => e.ESTATUS)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.FECHA_APLICADA).HasColumnType("DATE");

                entity.Property(e => e.FECHA_RECIBO).HasColumnType("DATE");

                entity.Property(e => e.ID_CLIENTE)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.METODO_RECIBO)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.MONEDA)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.MONEDA_FACTURA)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.MONTO_ORIGINAL_FACTURA).HasColumnType("NUMBER");

                entity.Property(e => e.NOMBRE_CLIENTE)
                    .HasMaxLength(150)
                    .IsUnicode(false);

                entity.Property(e => e.NUM_RECIBO)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.NUM_TRX_APLICADA)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.PENDIENTE_APLICAR).HasColumnType("NUMBER");

                entity.Property(e => e.SITIO)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.TOTAL_IMPUESTO_FACTURA).HasColumnType("NUMBER");

                entity.Property(e => e.TOTAL_RECIBO).HasColumnType("NUMBER");

                entity.Property(e => e.VENDEDOR)
                    .HasMaxLength(2)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<XXORA_CUSTOMER_MASTER>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("XXORA_CUSTOMER_MASTER");

                entity.HasIndex(e => e.ACCOUNT_ID, "IDX_CUSTOMER_ACCOUNT_ID");

                entity.HasIndex(e => e.BU_NOMBRE, "IDX_CUSTOMER_BU");

                entity.HasIndex(e => e.IDCLIENTE, "IDX_CUSTOMER_IDCLIENTE");

                entity.HasIndex(e => e.ORGANIZATION_ID, "IDX_CUSTOMER_ORGANIZATION_ID");

                entity.HasIndex(e => e.PARTY_ID, "IDX_CUSTOMER_PARTY_ID");

                entity.HasIndex(e => e.PARTY_SITE_NUMBER, "IDX_CUSTOMER_PARTY_SITE");

                entity.HasIndex(e => e.VENDEDOR, "IDX_CUSTOMER_SALESPEERSON");

                entity.HasIndex(e => e.SITIO, "IDX_CUSTOMER_SITE");

                entity.HasIndex(e => new { e.SITIO, e.SITIO_ESTATUS, e.RUTA }, "IDX_CUSTOMER_SITE_ESTADO_RUTA");

                entity.Property(e => e.ACCOUNT_ID).HasColumnType("NUMBER");

                entity.Property(e => e.ACCT_LAST_UPDATE_DATE).HasColumnType("DATE");

                entity.Property(e => e.AR_NUMERO)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.BILL_TO_SITE)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.BILL_TO_SITE_USE_ID).HasColumnType("NUMBER");

                entity.Property(e => e.BU_NOMBRE)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.CATEGORIA)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.CEDULA)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.CLIENTE_ESTATUS)
                    .HasMaxLength(1)
                    .IsUnicode(false);

                entity.Property(e => e.CUST_ACCT_SITE_ID).HasColumnType("NUMBER");

                entity.Property(e => e.EMAIL_CLIENTE)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.GRUPO_CLIENTE)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.IDCLIENTE)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.IDVENDEDOR)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.LATITUD_MUNICIPIO).HasColumnType("NUMBER");

                entity.Property(e => e.LIMITECREDITO).HasColumnType("NUMBER");

                entity.Property(e => e.LIMITECREDITO_MONEDA)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.LONGITUD_MUNICIPIO).HasColumnType("NUMBER");

                entity.Property(e => e.MERCHANDISER)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.NOMBRE_CLASECLIENTE)
                    .HasMaxLength(60)
                    .IsUnicode(false);

                entity.Property(e => e.NOMBRE_CLIENTE)
                    .HasMaxLength(150)
                    .IsUnicode(false);

                entity.Property(e => e.NOMBRE_SITIO)
                    .HasMaxLength(150)
                    .IsUnicode(false);

                entity.Property(e => e.ORGANIZATION_ID).HasColumnType("NUMBER");

                entity.Property(e => e.PAIS)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.PARTY_ID).HasColumnType("NUMBER");

                entity.Property(e => e.PARTY_NAME)
                    .HasMaxLength(150)
                    .IsUnicode(false);

                entity.Property(e => e.PARTY_SITE_ID).HasColumnType("NUMBER");

                entity.Property(e => e.PARTY_SITE_NUMBER)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.PARTY_SITE_PRIMARY_FLAG)
                    .HasMaxLength(1)
                    .IsUnicode(false);

                entity.Property(e => e.PORCIENTO_MERCHANDISER)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.PORCIENTO_VENDEDOR)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.REGISTRY_ID)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.RUTA)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.RUTA_COBRO)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.SITE_LAST_UPDATE_DATE).HasColumnType("DATE");

                entity.Property(e => e.SITE_USE_ID).HasColumnType("NUMBER");

                entity.Property(e => e.SITIO)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.SITIO_CANTON)
                    .HasMaxLength(60)
                    .IsUnicode(false);

                entity.Property(e => e.SITIO_CIUDAD)
                    .HasMaxLength(60)
                    .IsUnicode(false);

                entity.Property(e => e.SITIO_DIR1)
                    .HasMaxLength(240)
                    .IsUnicode(false);

                entity.Property(e => e.SITIO_DIR2)
                    .HasMaxLength(240)
                    .IsUnicode(false);

                entity.Property(e => e.SITIO_DIR3)
                    .HasMaxLength(240)
                    .IsUnicode(false);

                entity.Property(e => e.SITIO_DISTRITO)
                    .HasMaxLength(60)
                    .IsUnicode(false);

                entity.Property(e => e.SITIO_ESTADO)
                    .HasMaxLength(60)
                    .IsUnicode(false);

                entity.Property(e => e.SITIO_ESTATUS)
                    .HasMaxLength(1)
                    .IsUnicode(false);

                entity.Property(e => e.SITIO_POSTALCODE)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.SITIO_PROVINCIA)
                    .HasMaxLength(60)
                    .IsUnicode(false);

                entity.Property(e => e.TELEFONO1_CLIENTE)
                    .HasMaxLength(16)
                    .IsUnicode(false);

                entity.Property(e => e.TERMINO_PAGO)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.VENDEDOR)
                    .HasMaxLength(150)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<XXORA_DISCOUNT_LIST>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("XXORA_DISCOUNT_LIST");

                entity.Property(e => e.BU_NAME)
                    .HasMaxLength(240)
                    .IsUnicode(false);

                entity.Property(e => e.CREATED_BY)
                    .HasMaxLength(150)
                    .IsUnicode(false);

                entity.Property(e => e.CREATION_DATE).HasPrecision(6);

                entity.Property(e => e.CURRENCY_CODE)
                    .HasMaxLength(12)
                    .IsUnicode(false);

                entity.Property(e => e.DISCOUNT_LIST_ID).HasPrecision(18);

                entity.Property(e => e.DISCOUNT_LIST_ITEM_ID).HasPrecision(18);

                entity.Property(e => e.DISCOUNT_LIST_NAME)
                    .HasMaxLength(80)
                    .IsUnicode(false);

                entity.Property(e => e.DISCOUNT_PRICE).HasColumnType("NUMBER");

                entity.Property(e => e.DISCOUNT_TYPE)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.END_DATE).HasPrecision(6);

                entity.Property(e => e.ITEM_NUMBER)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.LAST_UPDATED_BY)
                    .HasMaxLength(150)
                    .IsUnicode(false);

                entity.Property(e => e.LAST_UPDATE_DATE).HasPrecision(6);

                entity.Property(e => e.PARTY_NUMBER)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.PRICING_RULE_TYPE_CODE)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.PRICING_UOM_CODE)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.RULE_DISCOUNT_NAME)
                    .HasMaxLength(80)
                    .IsUnicode(false);

                entity.Property(e => e.START_DATE).HasPrecision(6);

                entity.Property(e => e.STATUS)
                    .HasMaxLength(3)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<XXORA_ITEM_MASTER>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("XXORA_ITEM_MASTER");

                entity.Property(e => e.ACCEPTADESCUENTO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S'")
                    .IsFixedLength();

                entity.Property(e => e.ATTRIBUTE_DENSIDAD).HasColumnType("NUMBER(12,2)");

                entity.Property(e => e.BU_NAME)
                    .HasMaxLength(240)
                    .IsUnicode(false);

                entity.Property(e => e.CASE_PACK_QUANTITY).HasColumnType("NUMBER");

                entity.Property(e => e.CATEGORY_CODE)
                    .HasMaxLength(820)
                    .IsUnicode(false);

                entity.Property(e => e.CATEGORY_NAME)
                    .HasMaxLength(250)
                    .IsUnicode(false);

                entity.Property(e => e.CREATED_BY)
                    .HasMaxLength(150)
                    .IsUnicode(false);

                entity.Property(e => e.CREATION_DATE).HasPrecision(6);

                entity.Property(e => e.DESCRIPTION)
                    .HasMaxLength(240)
                    .IsUnicode(false);

                entity.Property(e => e.INDTRASLADO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'\n")
                    .IsFixedLength();

                entity.Property(e => e.ITEM_CODE)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.ITEM_NUMBER)
                    .HasMaxLength(300)
                    .IsUnicode(false);

                entity.Property(e => e.ITEM_STATUS_CODE)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.ITEM_TYPE)
                    .HasMaxLength(80)
                    .IsUnicode(false);

                entity.Property(e => e.LAST_UPDATED_BY)
                    .HasMaxLength(150)
                    .IsUnicode(false);

                entity.Property(e => e.LAST_UPDATE_DATE).HasPrecision(6);

                entity.Property(e => e.LONG_DESCRIPTION)
                    .HasMaxLength(2000)
                    .IsUnicode(false);

                entity.Property(e => e.ORGANIZATION_CODE)
                    .HasMaxLength(500)
                    .IsUnicode(false);

                entity.Property(e => e.ORIGIN_COUNTRY)
                    .HasMaxLength(250)
                    .IsUnicode(false);

                entity.Property(e => e.PRIMARY_UOM_CODE)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.SECONDARY_UOM_CODE)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.STATUS)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.SUBCATEGORY_CODE)
                    .HasMaxLength(820)
                    .IsUnicode(false);

                entity.Property(e => e.SUBCATEGORY_NAME)
                    .HasMaxLength(250)
                    .IsUnicode(false);

                entity.Property(e => e.TAX_CLASSIFICATION_CODE)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.TAX_RATE).HasColumnType("NUMBER");

                entity.Property(e => e.UNIT_WEIGHT).HasColumnType("NUMBER");

                entity.Property(e => e.WEIGHT_UOM_CODE)
                    .HasMaxLength(3)
                    .IsUnicode(false);
            });

            modelBuilder.HasSequence("DBTOOLS$EXECUTION_HISTORY_SEQ");

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
