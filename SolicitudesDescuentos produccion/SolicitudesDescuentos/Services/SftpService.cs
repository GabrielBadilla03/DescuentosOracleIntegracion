using WinSCP;

namespace SolicitudesDescuentos.Services;

public enum SftpUploadResult
{
    Subido,
    YaExistiaMismoTamano
}

public class SftpService
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _user;
    private readonly string _privateKeyPath;
    private readonly string _sshHostKeyFingerprint;
    private readonly string? _privateKeyPassphrase;
    private readonly bool _ignorarSeguridad;
    private readonly bool _autoDiscoverFingerprintIfMissing;

    public SftpService(
        string host,
        int port,
        string user,
        string privateKeyPath,
        string sshHostKeyFingerprint,
        string? privateKeyPassphrase = null,
        bool ignorarSeguridad = false,
        bool autoDiscoverFingerprintIfMissing = false)
    {
        _host = host;
        _port = port;
        _user = user;
        _privateKeyPath = privateKeyPath;
        _sshHostKeyFingerprint = sshHostKeyFingerprint;
        _privateKeyPassphrase = privateKeyPassphrase;
        _ignorarSeguridad = ignorarSeguridad;
        _autoDiscoverFingerprintIfMissing = autoDiscoverFingerprintIfMissing;
    }

    public static string DescubrirHuellaSshHost(
        string host,
        int port,
        string user,
        string privateKeyPath,
        string? privateKeyPassphrase = null)
    {
        var opts = new WinSCP.SessionOptions
        {
            Protocol = Protocol.Sftp,
            HostName = host,
            PortNumber = port,
            UserName = user,
            SshPrivateKeyPath = privateKeyPath
        };

        if (!string.IsNullOrWhiteSpace(privateKeyPassphrase))
            opts.PrivateKeyPassphrase = privateKeyPassphrase;

        using var session = new Session();

        var fp = session.ScanFingerprint(opts, "SHA-256");

        if (string.IsNullOrWhiteSpace(fp))
            throw new InvalidOperationException("No se pudo obtener la huella SSH del host.");

        return fp;
    }

    /// <summary>
    /// Método original. Se conserva la misma firma y el mismo comportamiento
    /// para no afectar ningún flujo existente que ya lo utilice.
    /// </summary>
    public void UploadFile(
        string localFullPath,
        string remoteDir,
        string remoteFileName,
        bool overwrite = true)
    {
        _ = UploadFileInternal(
            localFullPath,
            remoteDir,
            remoteFileName,
            overwrite,
            omitirSiExisteMismoTamano: false);
    }

    /// <summary>
    /// Variante utilizada exclusivamente por el worker automático.
    ///
    /// Si el archivo remoto ya existe con exactamente el mismo nombre y tamaño,
    /// no se vuelve a transmitir. Esto permite recuperarse del caso donde el
    /// SFTP terminó correctamente pero falló el SaveChanges de GENERADO='S'.
    ///
    /// El nombre remoto recibido se conserva exactamente igual.
    /// </summary>
    public SftpUploadResult UploadFileIdempotent(
        string localFullPath,
        string remoteDir,
        string remoteFileName,
        bool overwrite = true)
    {
        return UploadFileInternal(
            localFullPath,
            remoteDir,
            remoteFileName,
            overwrite,
            omitirSiExisteMismoTamano: true);
    }

    private SftpUploadResult UploadFileInternal(
        string localFullPath,
        string remoteDir,
        string remoteFileName,
        bool overwrite,
        bool omitirSiExisteMismoTamano)
    {
        if (string.IsNullOrWhiteSpace(localFullPath))
            throw new ArgumentException("Ruta local inválida.", nameof(localFullPath));

        if (!File.Exists(localFullPath))
            throw new FileNotFoundException("No existe el archivo local a subir.", localFullPath);

        if (string.IsNullOrWhiteSpace(remoteDir))
            throw new ArgumentException("Directorio remoto inválido.", nameof(remoteDir));

        if (string.IsNullOrWhiteSpace(remoteFileName))
            throw new ArgumentException("Nombre remoto inválido.", nameof(remoteFileName));

        var sessionOptions = new WinSCP.SessionOptions
        {
            Protocol = Protocol.Sftp,
            HostName = _host,
            PortNumber = _port,
            UserName = _user,
            SshPrivateKeyPath = _privateKeyPath
        };

        if (!string.IsNullOrWhiteSpace(_privateKeyPassphrase))
            sessionOptions.PrivateKeyPassphrase = _privateKeyPassphrase;

        if (!_ignorarSeguridad)
        {
            if (string.IsNullOrWhiteSpace(_sshHostKeyFingerprint))
            {
                if (_autoDiscoverFingerprintIfMissing)
                {
                    sessionOptions.SshHostKeyFingerprint = DescubrirHuellaSshHost(
                        _host,
                        _port,
                        _user,
                        _privateKeyPath,
                        _privateKeyPassphrase);
                }
                else
                {
                    throw new InvalidOperationException(
                        "Falta SshHostKeyFingerprint para la conexión SFTP.");
                }
            }
            else
            {
                sessionOptions.SshHostKeyFingerprint = _sshHostKeyFingerprint;
            }
        }

        using var session = new Session();
        session.Open(sessionOptions);

        if (!session.FileExists(remoteDir))
            session.CreateDirectory(remoteDir);

        var remotePath = $"{remoteDir.TrimEnd('/')}/{remoteFileName}";

        if (omitirSiExisteMismoTamano && session.FileExists(remotePath))
        {
            var localLength = new FileInfo(localFullPath).Length;
            var remoteLength = session.GetFileInfo(remotePath).Length;

            if (localLength == remoteLength)
                return SftpUploadResult.YaExistiaMismoTamano;
        }

        /*
         * Para la ruta original se mantiene exactamente el comportamiento
         * anterior de PutFiles. El parámetro overwrite se conserva por
         * compatibilidad con la firma existente.
         */
        _ = overwrite;

        var transferOptions = new TransferOptions
        {
            TransferMode = TransferMode.Binary
        };

        var transferResult = session.PutFiles(
            localFullPath,
            remotePath,
            remove: false,
            options: transferOptions);

        transferResult.Check();

        return SftpUploadResult.Subido;
    }
}
