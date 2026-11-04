using System.Collections.Concurrent;

namespace SolicitudesDescuentos.Services;

/// <summary>
/// Bloqueo de ejecución para evitar que dos procesos IIS del mismo servidor
/// ejecuten simultáneamente el mismo worker durante reciclados o cuando el
/// App Pool tenga más de un proceso.
///
/// Se utiliza un FileStream con FileShare.None porque el sistema operativo
/// libera automáticamente el bloqueo si el proceso termina de forma abrupta.
/// No modifica estados de negocio, nombres de archivos ni nomenclatura de
/// integración.
/// </summary>
public sealed class CrossProcessJobLock : IDisposable
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LocalFallbackLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly FileStream? _lockStream;
    private readonly SemaphoreSlim? _localSemaphore;
    private bool _acquired;

    private CrossProcessJobLock(
        FileStream? lockStream,
        SemaphoreSlim? localSemaphore,
        bool acquired)
    {
        _lockStream = lockStream;
        _localSemaphore = localSemaphore;
        _acquired = acquired;
    }

    public bool Acquired => _acquired;

    public static CrossProcessJobLock TryAcquire(string lockName)
    {
        if (string.IsNullOrWhiteSpace(lockName))
            throw new ArgumentException("El nombre del lock no puede estar vacío.", nameof(lockName));

        var safeName = new string(lockName
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_')
            .ToArray());

        try
        {
            var lockDirectory = Path.Combine(
                Path.GetTempPath(),
                "SolicitudesDescuentos_WorkerLocks");

            Directory.CreateDirectory(lockDirectory);

            var lockPath = Path.Combine(
                lockDirectory,
                $"{safeName}.lock");

            try
            {
                var stream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    options: FileOptions.None);

                return new CrossProcessJobLock(
                    lockStream: stream,
                    localSemaphore: null,
                    acquired: true);
            }
            catch (IOException)
            {
                // El archivo existe, pero está bloqueado por otro proceso.
                return new CrossProcessJobLock(
                    lockStream: null,
                    localSemaphore: null,
                    acquired: false);
            }
        }
        catch (UnauthorizedAccessException)
        {
            return AcquireLocalFallback(safeName);
        }
        catch (IOException)
        {
            return AcquireLocalFallback(safeName);
        }
    }

    private static CrossProcessJobLock AcquireLocalFallback(string safeName)
    {
        var semaphore = LocalFallbackLocks.GetOrAdd(
            safeName,
            _ => new SemaphoreSlim(1, 1));

        var acquired = semaphore.Wait(0);

        return new CrossProcessJobLock(
            lockStream: null,
            localSemaphore: semaphore,
            acquired: acquired);
    }

    public void Dispose()
    {
        if (!_acquired)
            return;

        try
        {
            if (_lockStream != null)
            {
                _lockStream.Dispose();
            }
            else
            {
                _localSemaphore?.Release();
            }
        }
        finally
        {
            _acquired = false;
        }
    }
}
