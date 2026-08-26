namespace SolicitudesDescuentos.Services.Tiendas
{
    public sealed class SincronizacionTiendasResult
    {
        public bool Ok { get; init; }

        public string Mensaje { get; init; } = string.Empty;

        public string RegistryId { get; init; } = string.Empty;

        public int DescuentosCliente { get; init; }

        public int PromocionesVigentes { get; init; }

        public int ArticulosCalculados { get; init; }

        public int FilasEncontradasTiendas { get; init; }

        public int FilasActualizadas { get; init; }

        public int FilasPuestasEnCero { get; init; }

        public static SincronizacionTiendasResult Fallo(string mensaje)
        {
            return new SincronizacionTiendasResult
            {
                Ok = false,
                Mensaje = mensaje
            };
        }
    }
}