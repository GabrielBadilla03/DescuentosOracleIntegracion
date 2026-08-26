using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SolicitudesDescuentos.ModelsTiendas;

namespace SolicitudesDescuentos.Data
{
    public partial class LancoTiendasContext : DbContext
    {
        public LancoTiendasContext()
        {
        }

        public LancoTiendasContext(DbContextOptions<LancoTiendasContext> options)
            : base(options)
        {
        }

        public virtual DbSet<INV_ARTIC_PROV> INV_ARTIC_PROVs { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("LANCOP");

            modelBuilder.Entity<INV_ARTIC_PROV>(entity =>
            {
                entity.HasKey(e => new { e.COD_CIA, e.COD_PROVEEDOR, e.COD_ARTICULO })
                    .HasName("INV_ARTIC_PROV_PK");

                entity.ToTable("INV_ARTIC_PROV");

                entity.HasIndex(e => new { e.COD_CIA, e.COD_ARTICULO }, "INVARTICULO_ARTICPROV_FK1");

                entity.Property(e => e.COD_CIA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.COD_PROVEEDOR)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.COD_ARTICULO)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.BONIFICA)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S'    ")
                    .IsFixedLength();

                entity.Property(e => e.CODIGO_BARRAS)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.COD_ARTIC_PROV)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.COD_MONEDA)
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.COSTO_ULT_COMPRA).HasColumnType("NUMBER(14,4)");

                entity.Property(e => e.DESC_FIJO).HasColumnType("NUMBER(5,2)");

                entity.Property(e => e.DIAS_TRANSITO).HasPrecision(3);

                entity.Property(e => e.FECHA_ULT_COMPRA).HasColumnType("DATE");

                entity.Property(e => e.FEC_ULTMOVTO).HasColumnType("DATE");

                entity.Property(e => e.IND_DESCONTINUADO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'    ")
                    .IsFixedLength();

                entity.Property(e => e.MEDIDA)
                    .HasMaxLength(5)
                    .IsUnicode(false);

                entity.Property(e => e.MINIMO_DESPACHO).HasPrecision(12);

                entity.Property(e => e.PERMITEDESCTO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ")
                    .IsFixedLength();
            });

            modelBuilder.HasSequence("CXC_SEQ_AUTORIZA");

            modelBuilder.HasSequence("CXP_GEN_PAGO");

            modelBuilder.HasSequence("DESCTOESPECIAL");

            modelBuilder.HasSequence("GENRASTREOSEQ");

            modelBuilder.HasSequence("PLAPAGOSEC");

            modelBuilder.HasSequence("PLAREPORTESEQ");

            modelBuilder.HasSequence("RHACCION");

            modelBuilder.HasSequence("RHSOLIC");

            modelBuilder.HasSequence("SEQ_AUTOVEN");

            modelBuilder.HasSequence("SEQ_DEPOSITOS");

            modelBuilder.HasSequence("SEQ_ORDEN");

            modelBuilder.HasSequence("SEQ_RECIBOS");

            modelBuilder.HasSequence("SEQ_RECLAMO");

            modelBuilder.HasSequence("SEQ_SOLICITUD");

            modelBuilder.HasSequence("SEQ_TOMAFIS");

            modelBuilder.HasSequence("VENBITFACQ");

            modelBuilder.HasSequence("VENSEQRESERVABIT");

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
