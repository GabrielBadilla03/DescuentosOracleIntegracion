namespace SolicitudesDescuentos.ModelsOracle;

public class DescuentosWorkerOptions
{
    // true: subir ZIP pendientes y actualizar PREDESCLASEORACLE en cada ciclo.
    // false: no ejecutar ninguna de las dos tareas.
    public bool Habilitado { get; set; } = true;

    public int IntervalSeconds { get; set; } = 120;
    public string OutputFolder { get; set; } = @"C:\DescuentosWorker\salida";
}
