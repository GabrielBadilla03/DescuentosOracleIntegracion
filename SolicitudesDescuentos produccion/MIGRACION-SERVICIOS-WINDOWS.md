# Separación de los trabajos de IIS hacia el servicio Windows

## Proyectos
- **SolicitudesDescuentos** (.NET 6): web IIS; formularios, reportes, edición, exportaciones manuales; no registra IHostedService.
- **DescuentosOracle.Core** (.NET 6): DbContext, entidades, lógica compartida de generación de archivos; mantiene los namespaces originales.
- **DescuentosOracleWorker** (.NET 6): alojador de los jobs. Contiene PredescuentosHostedService, TiendasDescuentosHostedService y SftpFingerprintStartupService, más procesamiento batch, SFTP y locks.

## Procesos automáticos
- PredescuentosHostedService: habilitado por DescuentosWorker:Habilitado; intervalo DescuentosWorker:IntervalSeconds (1200 en la configuración migrada). Incluye limpieza de impulsadores, procesamiento de aprobados/reversados, ART_NO_PROMO, ZIP, SFTP y GENERAR_PREDESCLASEORACLE.
- TiendasDescuentosHostedService: habilitado por TiendasDescuentosWorker:Habilitado; IntervalHours (8 horas).
- SftpFingerprintStartupService: inicialización SFTP al iniciar el servicio; SftpFingerprintStartup:Habilitado.
- CrossProcessJobLock mantiene la protección contra ejecuciones concurrentes en el mismo servidor.

## Configuración independiente
1. Crear en la carpeta de publicación del Worker el archivo **appsettings.Private.json** tomando como guía **appsettings.Private.example.json**. NO incluirlo en Git: está excluido en .gitignore.
2. Completar ConnectionStrings:LANCO (Oracle 11g), ConnectionStrings:Oracle (ATP), ConnectionStrings:LANCOTIENDAS, IdentificadorTiendas, DescuentosSftp (host, usuario, llave, huella SSH). Estas credenciales deliberadamente NO se copiaron a la configuración del repositorio.
3. Las rutas que contengan {ContentRoot} se resuelven desde la carpeta propia del Worker. Verificar permisos NTFS de la cuenta del servicio para Oracle wallet, llave SFTP, carpeta ZIP y archivo de huella.
4. La web conserva sus cadenas de conexión: no dependerá del Worker para atender peticiones ni generar ZIP manualmente.
5. La configuración de los jobs se cambia ahora en el Worker, no en appsettings.json de IIS.

## Publicación
```powershell
dotnet build "SolicitudesDescuentos.sln" -c Release
dotnet publish "DescuentosOracleWorker/DescuentosOracleWorker.csproj" -c Release -r win-x64 --self-contained false -o "C:\Servicios\DescuentosOracleWorker"
# Copiar appsettings.Private.json (fuera de Git) a la carpeta publicada.
sc.exe create "DescuentosOracleWorker" binPath= "C:\Servicios\DescuentosOracleWorker\DescuentosOracleWorker.exe" start= auto
sc.exe start "DescuentosOracleWorker"
```
Para detener: `sc.exe stop "DescuentosOracleWorker"`.

## Orden seguro de activación
1. Publicar PRIMERO la web sin los HostedServices y verificar que IIS realmente sirve la nueva versión.
2. Detener el App Pool antiguo si aún está ejecutando jobs.
3. Preparar y publicar el Worker y su configuración privada.
4. Instalar e iniciar el servicio de Windows y comprobar sus logs, conexión Oracle y procesamiento SFTP.
5. No permitir que la versión antigua de IIS y el Worker nuevo estén activos a la vez.
6. .NET 6 ya está fuera de soporte. Planificar actualización a LTS tras validar la compatibilidad con Oracle 11g.

Nota: no se cambió la lógica de descuentos, comisiones, integración ni generación manual de archivos. No se ha podido ejecutar una compilación con dotnet en este entorno.

## Registro diario del Worker

Los mensajes de `ILogger` de los jobs, SFTP, sincronizacion de tiendas, errores de arranque y eventos del host se guardan automaticamente en la carpeta `logs` que esta junto a `DescuentosOracleWorker.exe`. Se usa `AppContext.BaseDirectory`, no el directorio actual de Windows.

Ejemplo al publicar en `C:\\Servicios\\DescuentosOracleWorker_Publicado`:

```text
C:\\Servicios\\DescuentosOracleWorker_Publicado\\
  DescuentosOracleWorker.exe
  logs\\
    worker-20261112.log
    worker-20261113.log
```

Los archivos se crean por dia al recibir eventos de log, incluyen fecha y hora, nivel, origen del mensaje, texto y excepciones. Se conservan los ultimos 30 archivos diarios (aproximadamente 30 dias con actividad). Los mensajes tambien se escriben en la consola durante pruebas. La cuenta de Windows que ejecuta el servicio necesita permisos de escritura sobre `logs`.

Al actualizar el Worker, no eliminar la carpeta `logs`. El registro se inicializa antes de configurar las conexiones, por lo que tambien captura errores de inicio del host.
