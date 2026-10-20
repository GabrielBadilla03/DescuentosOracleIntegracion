using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TomaFisica.ModelsTomaFis;

namespace TomaFisica.Data
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
        public virtual DbSet<INV_BODEGA> INV_BODEGAs { get; set; } = null!;
        public virtual DbSet<INV_DETTOMAFI> INV_DETTOMAFIs { get; set; } = null!;
        public virtual DbSet<INV_LOC> INV_LOCs { get; set; } = null!;
        public virtual DbSet<INV_ORIGEN> INV_ORIGENs { get; set; } = null!;
        public virtual DbSet<INV_TOMAFI> INV_TOMAFIs { get; set; } = null!;
        public virtual DbSet<LOCALIZARD> LOCALIZARDs { get; set; } = null!;
        public virtual DbSet<PNORDENE> PNORDENEs { get; set; } = null!;

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

            modelBuilder.Entity<INV_BODEGA>(entity =>
            {
                entity.HasKey(e => new { e.COD_CIA, e.COD_BODEGA })
                    .HasName("INV_BODEGA_PK");

                entity.ToTable("INV_BODEGA");

                entity.Property(e => e.COD_CIA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.COD_BODEGA)
                    .HasMaxLength(5)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.DES_BODEGA)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.SUCURSAL)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();
            });

            modelBuilder.Entity<INV_DETTOMAFI>(entity =>
            {
                entity.HasKey(e => new { e.SECUENCIA, e.COD_LOTE, e.COD_ARTICULO, e.LOCALIZA })
                    .HasName("INV_DETTOMAFIS_PK");

                entity.ToTable("INV_DETTOMAFIS");

                entity.HasIndex(e => e.SECUENCIA, "INVTOMAFIS_DET_FK");

                entity.HasIndex(e => new { e.SECUENCIA, e.COD_LOTE, e.LOCALIZA, e.COD_ARTICULO }, "INV_DETTOMAFIS_LOC");

                entity.HasIndex(e => new { e.SECUENCIA, e.LOCALIZA, e.COD_ARTICULO }, "INV_DETTOMAFIS_LOC1");

                entity.HasIndex(e => e.LOCALIZA, "INV_DETTOMAFIS_LOC2");

                entity.Property(e => e.SECUENCIA).HasPrecision(10);

                entity.Property(e => e.COD_LOTE)
                    .HasMaxLength(15)
                    .IsUnicode(false);

                entity.Property(e => e.COD_ARTICULO)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.LOCALIZA)
                    .HasMaxLength(15)
                    .IsUnicode(false);

                entity.Property(e => e.BATCH)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.CAN_EXISTE).HasColumnType("NUMBER(14,4)");

                entity.Property(e => e.CONTEO1).HasColumnType("NUMBER(14,4)");

                entity.Property(e => e.CONTEO2).HasColumnType("NUMBER(14,4)");

                entity.Property(e => e.CONTEO3).HasColumnType("NUMBER(14,4)");

                entity.Property(e => e.COSTO).HasColumnType("NUMBER(14,4)");

                entity.Property(e => e.FECHALOTE).HasColumnType("DATE");

                entity.Property(e => e.LOCAL1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ");

                entity.Property(e => e.ORIGEN).HasPrecision(2);

                entity.Property(e => e.REPLICA1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ");

                entity.Property(e => e.RESERVA)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N'\n")
                    .IsFixedLength();

                entity.HasOne(d => d.SECUENCIANavigation)
                    .WithMany(p => p.INV_DETTOMAFIs)
                    .HasForeignKey(d => d.SECUENCIA)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("INVTOMAFIS_DET_FK");
            });

            modelBuilder.Entity<INV_LOC>(entity =>
            {
                entity.HasKey(e => new { e.COD_CIA, e.COD_BODEGA, e.LOCALIZA })
                    .HasName("INV_LOC_PK");

                entity.ToTable("INV_LOC");

                entity.HasIndex(e => new { e.COD_CIA, e.COD_BODEGA }, "INVBODEGA_LOC_FK");

                entity.HasIndex(e => new { e.COD_BODEGA, e.COD_CIA }, "INVBODEGA_LOC_FK1");

                entity.Property(e => e.COD_CIA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.COD_BODEGA)
                    .HasMaxLength(5)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.LOCALIZA)
                    .HasMaxLength(15)
                    .IsUnicode(false);

                entity.Property(e => e.DES_LOCALIZA)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.LOCAL1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ");

                entity.Property(e => e.REPLICA1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ");

                entity.HasOne(d => d.COD_)
                    .WithMany(p => p.INV_LOCs)
                    .HasForeignKey(d => new { d.COD_CIA, d.COD_BODEGA })
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("INVBODEGA_LOC_FK");
            });

            modelBuilder.Entity<INV_ORIGEN>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("INV_ORIGEN");

                entity.HasIndex(e => e.ORIGEN, "INV_ORIGEN_UQ")
                    .IsUnique();

                entity.Property(e => e.DESCRIPCION)
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.ORIGEN).HasPrecision(2);
            });

            modelBuilder.Entity<INV_TOMAFI>(entity =>
            {
                entity.HasKey(e => e.SECUENCIA)
                    .HasName("INV_TOMAFIS_PK");

                entity.ToTable("INV_TOMAFIS");

                entity.Property(e => e.SECUENCIA)
                    .HasPrecision(10)
                    .ValueGeneratedNever();

                entity.Property(e => e.COD_BODEGA)
                    .HasMaxLength(5)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.COD_CIA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.ESTADO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'P' ")
                    .IsFixedLength();

                entity.Property(e => e.FEC_APLICA).HasColumnType("DATE");

                entity.Property(e => e.FEC_MOVTO).HasColumnType("DATE");

                entity.Property(e => e.LOCAL1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ");

                entity.Property(e => e.OBSERVACION)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.REPLICA1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ");

                entity.Property(e => e.TIPO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'1' ")
                    .IsFixedLength();

                entity.Property(e => e.USUARIO)
                    .HasMaxLength(30)
                    .IsUnicode(false)
                    .HasDefaultValueSql("USER ");
            });

            modelBuilder.Entity<LOCALIZARD>(entity =>
            {
                entity.HasKey(e => e.LOCALIZACION);

                entity.ToTable("LOCALIZARD");

                entity.Property(e => e.LOCALIZACION)
                    .HasMaxLength(15)
                    .IsUnicode(false);

                entity.Property(e => e.BODEGA)
                    .HasMaxLength(10)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<PNORDENE>(entity =>
            {
                entity.HasKey(e => e.BATCH)
                    .HasName("PNORDENES_PK");

                entity.ToTable("PNORDENES");

                entity.Property(e => e.BATCH)
                    .HasMaxLength(30)
                    .IsUnicode(false)
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.AFECTAMP)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ")
                    .IsFixedLength();

                entity.Property(e => e.COSTO).HasColumnType("NUMBER(14,4)");

                entity.Property(e => e.COSTOADICIONAL)
                    .HasColumnType("NUMBER(14,4)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.COSTOTOTAL).HasColumnType("NUMBER(14,4)");

                entity.Property(e => e.DESCRIPCION)
                    .HasMaxLength(200)
                    .IsUnicode(false);

                entity.Property(e => e.ESTADO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .ValueGeneratedOnAdd()
                    .HasDefaultValueSql("'P' ")
                    .IsFixedLength();

                entity.Property(e => e.FECHA).HasColumnType("DATE");

                entity.Property(e => e.FECHACIERRE).HasColumnType("DATE");

                entity.Property(e => e.FORMULA)
                    .HasMaxLength(40)
                    .IsUnicode(false);

                entity.Property(e => e.LISTACIERRE)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N' ")
                    .IsFixedLength();

                entity.Property(e => e.LOCAL1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ");

                entity.Property(e => e.OBSERVACIONES)
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.PASO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N' ")
                    .IsFixedLength();

                entity.Property(e => e.REPLICA1)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'S' ")
                    .IsFixedLength();

                entity.Property(e => e.REPROCESO)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'N' ")
                    .IsFixedLength();

                entity.Property(e => e.RESERVADOINV)
                    .HasMaxLength(1)
                    .IsUnicode(false)
                    .ValueGeneratedOnAdd()
                    .HasDefaultValueSql("'C' ")
                    .IsFixedLength();

                entity.Property(e => e.USUARIO)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.VOLUMENORDER)
                    .HasColumnType("NUMBER(14,4)")
                    .HasDefaultValueSql("0");
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
