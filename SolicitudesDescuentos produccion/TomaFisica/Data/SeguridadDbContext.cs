using Microsoft.EntityFrameworkCore;
using TomaFisica.ModelsSeguridad;

namespace TomaFisica.Data
{
    public partial class SeguridadDbContext : DbContext
    {
        public SeguridadDbContext()
        {
        }

        public SeguridadDbContext(
            DbContextOptions<SeguridadDbContext> options)
            : base(options)
        {
        }

        public virtual DbSet<SEG_USUARIO> SEG_USUARIOs { get; set; } = null!;
        public virtual DbSet<SEG_GRUPO> SEG_GRUPOs { get; set; } = null!;
        public virtual DbSet<SEG_GRPUSUARIO> SEG_GRPUSUARIOs { get; set; } = null!;

        protected override void OnModelCreating(
            ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("NUEVO");

            modelBuilder.Entity<SEG_USUARIO>(entity =>
            {
                entity.HasKey(e => e.COD_USUARIO)
                    .HasName("SEG_USUARIO_PK");

                entity.ToTable("SEG_USUARIO");

                entity.Property(e => e.COD_USUARIO)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.DES_USUARIO)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.CAMBIOCLAVE)
                    .HasColumnType("DATE");

                entity.Property(e => e.TIPO)
                    .HasMaxLength(2)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'OT'")
                    .IsFixedLength();

                entity.Property(e => e.CIA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.SUCURSAL)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.PASSWORD)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.COD_CLIENTE)
                    .HasMaxLength(25)
                    .IsUnicode(false);

                entity.Property(e => e.IDIOMA)
                    .HasMaxLength(10)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'ESP'");

                entity.Property(e => e.CAJA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .IsFixedLength();

                entity.Property(e => e.CLAVE_USUARIO)
                    .HasMaxLength(15)
                    .IsUnicode(false);

                entity.Property(e => e.COD_CIA)
                    .HasMaxLength(3)
                    .IsUnicode(false)
                    .HasDefaultValueSql("'001' ")
                    .IsFixedLength();
            });

            modelBuilder.Entity<SEG_GRUPO>(entity =>
            {
                entity.HasKey(e => e.COD_GRUPO)
                    .HasName("SEG_GRUPO_PK");

                entity.ToTable("SEG_GRUPO");

                entity.Property(e => e.COD_GRUPO)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.DES_GRUPO)
                    .HasMaxLength(30)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<SEG_GRPUSUARIO>(entity =>
            {
                entity.HasKey(e => new
                    {
                        e.COD_GRUPO,
                        e.COD_USUARIO
                    })
                    .HasName("SEG_GRPUSUARIO_PK");

                entity.ToTable("SEG_GRPUSUARIO");

                entity.HasIndex(
                    e => e.COD_USUARIO,
                    "SEGUSUARIO_GRPUSUARIO_FK");

                entity.Property(e => e.COD_GRUPO)
                    .HasMaxLength(30)
                    .IsUnicode(false);

                entity.Property(e => e.COD_USUARIO)
                    .HasMaxLength(30)
                    .IsUnicode(false);
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(
            ModelBuilder modelBuilder);
    }
}
