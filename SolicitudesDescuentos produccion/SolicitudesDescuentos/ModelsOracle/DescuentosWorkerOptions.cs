namespace SolicitudesDescuentos.ModelsOracle;

public class DescuentosWorkerOptions
{
    public int IntervalSeconds { get; set; } = 120;
    public string OutputFolder { get; set; } = @"C:\DescuentosWorker\salida";
}