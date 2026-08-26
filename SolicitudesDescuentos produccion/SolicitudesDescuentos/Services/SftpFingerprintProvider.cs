using Microsoft.Extensions.Options;
using SolicitudesDescuentos.ModelsOracle;
using WinSCP;

namespace SolicitudesDescuentos.Services;

public class SftpFingerprintProvider : ISftpFingerprintProvider
{
    private readonly DescuentosSftpOptions _options;
    private readonly ILogger<SftpFingerprintProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _fingerprint;

    public SftpFingerprintProvider(
        IOptions<DescuentosSftpOptions> options,
        ILogger<SftpFingerprintProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string? CurrentFingerprint => _fingerprint;

    public async Task<string> GetFingerprintAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(_fingerprint))
            return _fingerprint;

        await _gate.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrWhiteSpace(_fingerprint))
                return _fingerprint;

            // 1) Si vino fija en appsettings, úsala
            if (!string.IsNullOrWhiteSpace(_options.SshHostKeyFingerprint))
            {
                _fingerprint = _options.SshHostKeyFingerprint.Trim();
                _logger.LogInformation("Usando huella SSH desde configuración: {Fingerprint}", _fingerprint);
                return _fingerprint;
            }

            // 2) Si existe en archivo local, úsala
            if (!string.IsNullOrWhiteSpace(_options.FingerprintCacheFile) &&
                File.Exists(_options.FingerprintCacheFile))
            {
                var fromFile = (await File.ReadAllTextAsync(_options.FingerprintCacheFile, ct)).Trim();
                if (!string.IsNullOrWhiteSpace(fromFile))
                {
                    _fingerprint = fromFile;
                    _logger.LogInformation("Usando huella SSH desde caché local: {Fingerprint}", _fingerprint);
                    return _fingerprint;
                }
            }

            // 3) Si no existe, descubrirla
            var sessionOptions = new WinSCP.SessionOptions
            {
                Protocol = Protocol.Sftp,
                HostName = _options.Host,
                PortNumber = _options.Port,
                UserName = _options.User,
                SshPrivateKeyPath = _options.PrivateKeyPath
            };

            if (!string.IsNullOrWhiteSpace(_options.PrivateKeyPassphrase))
                sessionOptions.PrivateKeyPassphrase = _options.PrivateKeyPassphrase;

            using var session = new Session();

            var fingerprint = await Task.Run(
                () => session.ScanFingerprint(sessionOptions, "SHA-256"),
                ct);

            if (string.IsNullOrWhiteSpace(fingerprint))
                throw new InvalidOperationException("No se pudo descubrir la huella SSH del servidor.");

            _fingerprint = fingerprint.Trim();

            if (!string.IsNullOrWhiteSpace(_options.FingerprintCacheFile))
            {
                var dir = Path.GetDirectoryName(_options.FingerprintCacheFile);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                await File.WriteAllTextAsync(_options.FingerprintCacheFile, _fingerprint, ct);
            }

            _logger.LogInformation("Huella SSH descubierta y guardada: {Fingerprint}", _fingerprint);
            return _fingerprint;
        }
        finally
        {
            _gate.Release();
        }
    }
}