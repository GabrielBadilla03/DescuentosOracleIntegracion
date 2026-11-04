namespace SolicitudesDescuentos.ModelsOracle;

public class DescuentosSftpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string User { get; set; } = "";
    public string PrivateKeyPath { get; set; } = "";
    public string PrivateKeyPassphrase { get; set; } = "";

    // Puede venir vacío. Si está vacío, la app la descubre al iniciar.
    public string SshHostKeyFingerprint { get; set; } = "";

    public bool IgnorarSeguridad { get; set; } = false;
    public string RemoteDirPending { get; set; } = "/inbound/OM/DiscountList/Pending";

    // Archivo local donde se guarda la huella descubierta
    public string FingerprintCacheFile { get; set; } = @"C:\DescuentosWorker\sftp-hostkey.txt";
}