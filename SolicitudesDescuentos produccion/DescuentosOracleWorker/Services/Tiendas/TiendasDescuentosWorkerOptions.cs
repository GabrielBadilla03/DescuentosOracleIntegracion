namespace SolicitudesDescuentos.Services.Tiendas
{
    public sealed class TiendasDescuentosWorkerOptions
    {
        public bool Habilitado { get; set; } = true;

        public double IntervalHours { get; set; } = 8;
    }
}
