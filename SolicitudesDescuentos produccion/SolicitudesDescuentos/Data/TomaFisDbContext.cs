using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SolicitudesDescuentos.ModelsTomaFis;

namespace SolicitudesDescuentos.Data
{
    public partial class TomaFisDbContext : DbContext
    {
        public TomaFisDbContext()
        {
        }

        public TomaFisDbContext(DbContextOptions<TomaFisDbContext> options)
            : base(options)
        {
        }

        public virtual DbSet<INV_ARTICULO> INV_ARTICULOs { get; set; } = null!;
        public virtual DbSet<INV_ARTIC_CODBAR> INV_ARTIC_CODBARs { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("TOMAFIS");

            modelBuilder.Entity<INV_ARTICULO>(entity =>
            {
                entity.HasKey(e => e.COD_ARTICULO);

                entity.ToTable("INV_ARTICULO");

                entity.Property(e => e.COD_ARTICULO)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.CATEGORIA)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.DESCRIPCION)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.MEDIDA)
                    .HasMaxLength(10)
                    .IsUnicode(false);

                entity.Property(e => e.MULTIPLOCAJA).HasColumnType("NUMBER(8,2)");

                entity.Property(e => e.SUBCATEGORIA)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.TIPOINVENTARIO)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.UNIDADNEGOCIO).HasPrecision(1);
            });

            modelBuilder.Entity<INV_ARTIC_CODBAR>(entity =>
            {
                entity.HasKey(e => new { e.COD_CIA, e.COD_ARTICULO, e.COD_BARRAS })
                    .HasName("INV_ARTIC_CODBAR_PK");

                entity.ToTable("INV_ARTIC_CODBAR");

                entity.HasIndex(e => new { e.COD_CIA, e.COD_ARTICULO }, "INVARTICULO_ARTICCODBAR_FK1");

                entity.HasIndex(e => new { e.COD_CIA, e.COD_BARRAS }, "INV_ARTIC_CODBAR_IDX1")
                    .IsUnique();

                entity.Property(e => e.COD_CIA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.COD_ARTICULO)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.COD_BARRAS)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.CLASE)
                    .HasMaxLength(5)
                    .IsUnicode(false);

                entity.Property(e => e.LOCAL1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S'                   ");

                entity.Property(e => e.REPLICA1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S'                   ");

                entity.Property(e => e.TITULAR)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.HasOne(d => d.COD_ARTICULONavigation)
                    .WithMany(p => p.INV_ARTIC_CODBARs)
                    .HasForeignKey(d => d.COD_ARTICULO)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("INVARTICULO_ARTICCODBAR_FK");
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
