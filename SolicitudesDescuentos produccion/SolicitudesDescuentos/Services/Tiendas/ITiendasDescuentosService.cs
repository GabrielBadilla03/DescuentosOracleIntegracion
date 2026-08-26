namespace SolicitudesDescuentos.Services.Tiendas
{
    public interface ITiendasDescuentosService
    {
        Task<SincronizacionTiendasResult> SincronizarAsync(
            CancellationToken cancellationToken = default);
    }
}
