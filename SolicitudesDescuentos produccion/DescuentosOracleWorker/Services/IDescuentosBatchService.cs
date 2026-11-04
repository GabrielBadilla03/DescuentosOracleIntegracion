using System.Threading;
using System.Threading.Tasks;

namespace SolicitudesDescuentos.Services;

public interface IDescuentosBatchService
{
    Task ProcesarPendientesAsync(CancellationToken cancellationToken = default);
}