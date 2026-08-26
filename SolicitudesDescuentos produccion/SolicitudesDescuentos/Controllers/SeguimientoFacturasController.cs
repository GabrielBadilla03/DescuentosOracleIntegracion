using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SolicitudesDescuentos.Data;
using SolicitudesDescuentos.ModelsLanco;

namespace SolicitudesDescuentos.Controllers;

[Authorize]
public sealed class SeguimientoFacturasController : Controller
{
    private const string EstadoTodos = "TODOS";
    private const string EstadoPendientes = "PENDIENTES";
    private const string EstadoProcesadas = "PROCESADAS";

    private const string ResultadoProcesado = "PROCESADO";
    private const string ResultadoError = "ERROR";
    private const string ResultadoSinIntento = "SIN_INTENTO";

    private const string TipoDocNotaCredito = "03";

    private const string TipoPersonaTodos = "TODOS";
    private const string TipoPersonaAgente = "A";
    private const string TipoPersonaTransportista = "T";

    private readonly LancoDbContext _context;

    public SeguimientoFacturasController(LancoDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var hoy = DateTime.Today;

        return View(new SeguimientoFacturasIndexViewModel
        {
            FechaInicio = hoy.AddDays(-15),
            FechaFin = hoy
        });
    }

    /// <summary>
    /// Pantalla de pistoleadas.
    ///
    /// Solo participan TIPODOC 01, 04 y 09.
    /// El TIPODOC 03 NO se toma en cuenta para pistoleadas/no pistoleadas.
    ///
    /// Procesada: la CLAVE existe en CXCDETFACREC o CXCDETFACRECBIT y el
    /// encabezado CXCENCFACREC relacionado está activo.
    ///
    /// Pendiente: no existe en ninguna de las dos relaciones activas.
    ///
    /// tipoPersona:
    /// - TODOS: no filtra por persona.
    /// - A: solo facturas pistoleadas relacionadas con CXCENCFACREC.TIPOPERSONA = A.
    /// - T: solo facturas pistoleadas relacionadas con CXCENCFACREC.TIPOPERSONA = T.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> BuscarPistoleadas(
        DateTime fechaInicio,
        DateTime fechaFin,
        string? estado,
        string? tipoPersona,
        CancellationToken cancellationToken)
    {
        var validacion = ValidarFechasYEstado(fechaInicio, fechaFin, estado);
        if (validacion.Error is not null)
            return BadRequest(new { ok = false, mensaje = validacion.Error });

        var validacionTipoPersona = ValidarTipoPersona(tipoPersona);
        if (validacionTipoPersona.Error is not null)
            return BadRequest(new { ok = false, mensaje = validacionTipoPersona.Error });

        fechaInicio = fechaInicio.Date;
        fechaFin = fechaFin.Date;
        estado = validacion.Estado;
        tipoPersona = validacionTipoPersona.TipoPersona;

        try
        {
            // Para pistoleo NO se obtiene TIPODOC 03.
            var facturasBase = await ObtenerFacturasAsync(
                fechaInicio,
                fechaFin,
                incluirTipo03: false,
                cancellationToken: cancellationToken);

            var facturas = facturasBase
                .Select(x => x.Factura)
                .ToList();

            var items = await CompletarPistoleoAsync(
                facturas,
                cancellationToken);

            // Primero se aplica el filtro normal de estado.
            var itemsFiltrados = FiltrarPistoleadas(
                items,
                estado);

            // Si seleccionó A o T, se conservan únicamente las facturas
            // que tengan relación activa con CXCENCFACREC de ese TIPOPERSONA.
            itemsFiltrados = await FiltrarPistoleadasPorTipoPersonaAsync(
                itemsFiltrados,
                tipoPersona,
                cancellationToken);

            var respuesta = new SeguimientoFacturasRespuestaViewModel
            {
                Ok = true,
                Total = items.Count,
                TotalProcesadas = items.Count(x => x.PistoleadaProcesada),
                TotalPendientes = items.Count(x => !x.PistoleadaProcesada),
                Items = itemsFiltrados
            };

            return Json(respuesta);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new { ok = false, mensaje = "La consulta fue cancelada." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                ok = false,
                mensaje = $"No fue posible consultar las facturas pistoleadas: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Pantalla de escaneadas.
    ///
    /// Para escaneo participan TIPODOC 01, 03, 04 y 09.
    ///
    /// Regla previa:
    /// - TIPODOC 03 entra al seguimiento de escaneo sin validar pistoleo.
    /// - TIPODOC 01, 04 y 09 solo entran si ya están pistoleados.
    ///
    /// Una vez obtenidas las facturas elegibles:
    /// - Procesada: el último intento en LOG_ENVIO_PDF_ORACLE terminó PROCESADO.
    /// - Pendiente: no tiene intento o el último intento no terminó PROCESADO.
    /// - Si el último intento terminó ERROR, se mantiene PENDIENTE y se muestra
    ///   el error, la fecha, el archivo y el mensaje del log.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> BuscarEscaneadas(
        DateTime fechaInicio,
        DateTime fechaFin,
        string? estado,
        string? resultado,
        CancellationToken cancellationToken)
    {
        var validacion = ValidarFechasYEstado(fechaInicio, fechaFin, estado);
        if (validacion.Error is not null)
            return BadRequest(new { ok = false, mensaje = validacion.Error });

        fechaInicio = fechaInicio.Date;
        fechaFin = fechaFin.Date;
        estado = validacion.Estado;
        resultado = Normalizar(resultado);

        if (string.IsNullOrEmpty(resultado))
            resultado = EstadoTodos;

        if (resultado is not (EstadoTodos or ResultadoProcesado or ResultadoError or ResultadoSinIntento))
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = "El resultado debe ser TODOS, PROCESADO, ERROR o SIN_INTENTO."
            });
        }

        try
        {
            // Para escaneo sí se obtiene el TIPODOC 03.
            var facturasBase = await ObtenerFacturasAsync(
                fechaInicio,
                fechaFin,
                incluirTipo03: true,
                cancellationToken: cancellationToken);

            /*
             * Los tipos 01, 04 y 09 deben estar pistoleados antes de poder
             * aparecer en la pantalla de escaneo.
             *
             * El tipo 03 es la única excepción: entra directamente y NO se
             * valida contra CXCDETFACREC / CXCDETFACRECBIT.
             */
            var facturasQueRequierenPistoleo = facturasBase
                .Where(x => Normalizar(x.TipoDoc) != TipoDocNotaCredito)
                .Select(x => x.Factura)
                .ToList();

            await CompletarPistoleoAsync(
                facturasQueRequierenPistoleo,
                cancellationToken);

            var facturasElegiblesParaEscaneo = facturasBase
                .Where(x =>
                    Normalizar(x.TipoDoc) == TipoDocNotaCredito ||
                    x.Factura.PistoleadaProcesada)
                .Select(x => x.Factura)
                .ToList();

            var items = await CompletarEscaneoAsync(
                facturasElegiblesParaEscaneo,
                cancellationToken);

            var respuesta = new SeguimientoFacturasRespuestaViewModel
            {
                Ok = true,
                Total = items.Count,
                TotalProcesadas = items.Count(x => x.EscaneadaProcesada),
                TotalPendientes = items.Count(x => !x.EscaneadaProcesada),
                TotalLogProcesado = items.Count(x =>
                    string.Equals(
                        x.EstadoLog,
                        ResultadoProcesado,
                        StringComparison.OrdinalIgnoreCase)),
                TotalLogError = items.Count(x => x.EscaneadaConError),
                TotalSinIntento = items.Count(x => x.IdLog is null),
                Items = FiltrarEscaneadas(items, estado, resultado)
            };

            return Json(respuesta);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new { ok = false, mensaje = "La consulta fue cancelada." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                ok = false,
                mensaje = $"No fue posible consultar las facturas escaneadas: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Descarga en PDF las facturas que cumplen exactamente los filtros
    /// seleccionados en la pantalla actual.
    ///
    /// tipo = PISTOLEADAS:
    ///     aplica fechaInicio, fechaFin, estado y tipoPersona.
    ///
    /// tipo = ESCANEADAS:
    ///     aplica fechaInicio, fechaFin, estado y resultado.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> DescargarPdf(
        string tipo,
        DateTime fechaInicio,
        DateTime fechaFin,
        string? estado,
        string? resultado,
        string? tipoPersona,
        CancellationToken cancellationToken)
    {
        var validacion = ValidarFechasYEstado(fechaInicio, fechaFin, estado);
        if (validacion.Error is not null)
            return BadRequest(new { ok = false, mensaje = validacion.Error });

        fechaInicio = fechaInicio.Date;
        fechaFin = fechaFin.Date;
        estado = validacion.Estado;
        tipo = Normalizar(tipo);

        if (tipo is not ("PISTOLEADAS" or "ESCANEADAS"))
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = "El tipo debe ser PISTOLEADAS o ESCANEADAS."
            });
        }

        // El filtro de tipo de persona solamente corresponde a la pantalla
        // de pistoleadas. Para escaneadas se ignora.
        if (tipo == "PISTOLEADAS")
        {
            var validacionTipoPersona = ValidarTipoPersona(tipoPersona);
            if (validacionTipoPersona.Error is not null)
                return BadRequest(new { ok = false, mensaje = validacionTipoPersona.Error });

            tipoPersona = validacionTipoPersona.TipoPersona;
        }
        else
        {
            tipoPersona = TipoPersonaTodos;
        }

        try
        {
            List<SeguimientoFacturaItemViewModel> items;
            string titulo;
            string resultadoPdf = string.Empty;
            bool esEscaneadas = tipo == "ESCANEADAS";

            if (!esEscaneadas)
            {
                // Misma lógica de BuscarPistoleadas.
                var facturasBase = await ObtenerFacturasAsync(
                    fechaInicio,
                    fechaFin,
                    incluirTipo03: false,
                    cancellationToken: cancellationToken);

                var facturas = facturasBase
                    .Select(x => x.Factura)
                    .ToList();

                items = await CompletarPistoleoAsync(
                    facturas,
                    cancellationToken);

                items = FiltrarPistoleadas(items, estado);

                items = await FiltrarPistoleadasPorTipoPersonaAsync(
                    items,
                    tipoPersona,
                    cancellationToken);

                titulo = "Seguimiento de facturas pistoleadas";
            }
            else
            {
                // Misma validación de BuscarEscaneadas.
                resultado = Normalizar(resultado);

                if (string.IsNullOrEmpty(resultado))
                    resultado = EstadoTodos;

                if (resultado is not (
                    EstadoTodos or
                    ResultadoProcesado or
                    ResultadoError or
                    ResultadoSinIntento))
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "El resultado debe ser TODOS, PROCESADO, ERROR o SIN_INTENTO."
                    });
                }

                // Para escaneo sí participa el TIPODOC 03.
                var facturasBase = await ObtenerFacturasAsync(
                    fechaInicio,
                    fechaFin,
                    incluirTipo03: true,
                    cancellationToken: cancellationToken);

                // 01, 04 y 09 deben estar pistoleadas.
                // 03 entra directamente.
                var facturasQueRequierenPistoleo = facturasBase
                    .Where(x => Normalizar(x.TipoDoc) != TipoDocNotaCredito)
                    .Select(x => x.Factura)
                    .ToList();

                await CompletarPistoleoAsync(
                    facturasQueRequierenPistoleo,
                    cancellationToken);

                var facturasElegiblesParaEscaneo = facturasBase
                    .Where(x =>
                        Normalizar(x.TipoDoc) == TipoDocNotaCredito ||
                        x.Factura.PistoleadaProcesada)
                    .Select(x => x.Factura)
                    .ToList();

                items = await CompletarEscaneoAsync(
                    facturasElegiblesParaEscaneo,
                    cancellationToken);

                items = FiltrarEscaneadas(
                    items,
                    estado,
                    resultado);

                titulo = "Seguimiento de facturas escaneadas";
                resultadoPdf = resultado;
            }

            var pdf = GenerarPdfFacturas(
                titulo,
                items,
                fechaInicio,
                fechaFin,
                estado,
                resultadoPdf,
                tipoPersona,
                esEscaneadas);

            var nombreArchivo =
                $"Facturas_{tipo}_{fechaInicio:yyyyMMdd}_{fechaFin:yyyyMMdd}.pdf";

            return File(
                pdf,
                "application/pdf",
                nombreArchivo);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new
            {
                ok = false,
                mensaje = "La generación del PDF fue cancelada."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                ok = false,
                mensaje = $"No fue posible generar el PDF: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Genera el reporte PDF con las mismas columnas principales de la pantalla.
    /// </summary>
    private static byte[] GenerarPdfFacturas(
        string titulo,
        IReadOnlyList<SeguimientoFacturaItemViewModel> items,
        DateTime fechaInicio,
        DateTime fechaFin,
        string estado,
        string resultado,
        string tipoPersona,
        bool esEscaneadas)
    {
        static string T(string? valor) =>
            (valor ?? string.Empty).Trim();

        static string FechaDesdeTexto(string? valor)
        {
            var texto = T(valor);

            if (texto.Length >= 10)
                return texto[..10];

            return texto;
        }

        static string FechaHora(object? valor)
        {
            if (valor is DateTime fecha)
                return fecha.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

            return string.Empty;
        }

        static string Monto(object? valor)
        {
            if (valor is null)
                return "0,00";

            try
            {
                var numero = Convert.ToDecimal(valor, CultureInfo.InvariantCulture);

                return numero.ToString(
                    "N2",
                    CultureInfo.GetCultureInfo("es-CR"));
            }
            catch
            {
                return valor.ToString() ?? string.Empty;
            }
        }

        static IContainer CeldaEncabezado(IContainer container) =>
            container
                .Background(Colors.Grey.Lighten3)
                .BorderBottom(1)
                .BorderColor(Colors.Grey.Medium)
                .PaddingVertical(4)
                .PaddingHorizontal(3);

        static IContainer Celda(IContainer container) =>
            container
                .BorderBottom(0.5f)
                .BorderColor(Colors.Grey.Lighten2)
                .PaddingVertical(3)
                .PaddingHorizontal(3);

        var documento = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(18);

                page.DefaultTextStyle(style =>
                    style.FontSize(esEscaneadas ? 6.5f : 7f));

                page.Header()
                    .PaddingBottom(10)
                    .Column(column =>
                    {
                        column.Item()
                            .Text(titulo)
                            .FontSize(16)
                            .SemiBold();

                        column.Item()
                            .PaddingTop(3)
                            .Text(
                                $"Desde: {fechaInicio:dd/MM/yyyy}   " +
                                $"Hasta: {fechaFin:dd/MM/yyyy}   " +
                                $"Estado: {estado}");

                        if (!esEscaneadas)
                        {
                            var tipoPersonaTexto = tipoPersona switch
                            {
                                TipoPersonaAgente => "Agentes",
                                TipoPersonaTransportista => "Transportistas",
                                _ => "Todos"
                            };

                            column.Item()
                                .Text($"Tipo persona: {tipoPersonaTexto}");
                        }

                        if (esEscaneadas)
                        {
                            column.Item()
                                .Text(
                                    $"Último intento: " +
                                    $"{(string.IsNullOrWhiteSpace(resultado) ? EstadoTodos : resultado)}");
                        }

                        column.Item()
                            .Text($"Facturas mostradas: {items.Count}");
                    });

                page.Content()
                    .Element(content =>
                    {
                        if (items.Count == 0)
                        {
                            content
                                .PaddingTop(30)
                                .AlignCenter()
                                .Text("No hay facturas para los filtros seleccionados.")
                                .FontSize(11);

                            return;
                        }

                        if (!esEscaneadas)
                        {
                            content.Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(58); // Estado
                                    columns.ConstantColumn(58); // Origen
                                    columns.ConstantColumn(58); // Fecha
                                    columns.ConstantColumn(70); // Documento
                                    columns.ConstantColumn(95); // Consecutivo
                                    columns.ConstantColumn(65); // Cliente
                                    columns.RelativeColumn(1.7f); // Nombre
                                    columns.ConstantColumn(48); // Ruta
                                    columns.ConstantColumn(70); // Total
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Element(CeldaEncabezado).Text("Estado").SemiBold();
                                    header.Cell().Element(CeldaEncabezado).Text("Origen").SemiBold();
                                    header.Cell().Element(CeldaEncabezado).Text("Fecha").SemiBold();
                                    header.Cell().Element(CeldaEncabezado).Text("Documento").SemiBold();
                                    header.Cell().Element(CeldaEncabezado).Text("Consecutivo").SemiBold();
                                    header.Cell().Element(CeldaEncabezado).Text("Cliente").SemiBold();
                                    header.Cell().Element(CeldaEncabezado).Text("Nombre").SemiBold();
                                    header.Cell().Element(CeldaEncabezado).Text("Ruta").SemiBold();
                                    header.Cell().Element(CeldaEncabezado).AlignRight().Text("Total").SemiBold();
                                });

                                foreach (var item in items)
                                {
                                    table.Cell().Element(Celda).Text(T(item.EstadoPistoleo));
                                    table.Cell().Element(Celda).Text(T(item.OrigenPistoleo));
                                    table.Cell().Element(Celda).Text(FechaDesdeTexto(item.FechaEmisionTexto));
                                    table.Cell().Element(Celda).Text(T(item.Documento));
                                    table.Cell().Element(Celda).Text(T(item.NumeroConsecutivo));
                                    table.Cell().Element(Celda).Text(T(item.CodigoCliente));
                                    table.Cell().Element(Celda).Text(T(item.NombreCliente));
                                    table.Cell().Element(Celda).Text(T(item.Ruta));
                                    table.Cell().Element(Celda).AlignRight().Text(Monto(item.TotalComprobante));
                                }
                            });
                        }
                        else
                        {
                            content.Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(52); // Estado
                                    columns.ConstantColumn(62); // Resultado
                                    columns.ConstantColumn(48); // Error
                                    columns.ConstantColumn(55); // Fecha
                                    columns.ConstantColumn(65); // Documento
                                    columns.ConstantColumn(82); // Consecutivo
                                    columns.ConstantColumn(58); // Cliente
                                    columns.RelativeColumn(1.15f); // Nombre
                                    columns.ConstantColumn(78); // Fecha intento
                                    columns.RelativeColumn(0.85f); // Archivo
                                    columns.RelativeColumn(1.35f); // Mensaje
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Element(CeldaEncabezado).Text("Estado").SemiBold();
                                    header.Cell().Element(CeldaEncabezado).Text("Último intento").SemiBold();
                                    header.Cell().Element(CeldaEncabezado).Text("Error").SemiBold();
                                    header.Cell().Element(CeldaEncabezado).Text("Fecha").SemiBold();
                                    header.Cell().Element(CeldaEncabezado).Text("Documento").SemiBold();
                                    header.Cell().Element(CeldaEncabezado).Text("Consecutivo").SemiBold();
                                    header.Cell().Element(CeldaEncabezado).Text("Cliente").SemiBold();
                                    header.Cell().Element(CeldaEncabezado).Text("Nombre").SemiBold();
                                    header.Cell().Element(CeldaEncabezado).Text("Fecha intento").SemiBold();
                                    header.Cell().Element(CeldaEncabezado).Text("Archivo").SemiBold();
                                    header.Cell().Element(CeldaEncabezado).Text("Mensaje").SemiBold();
                                });

                                foreach (var item in items)
                                {
                                    var error = item.EscaneadaConError
                                        ? "SÍ"
                                        : item.IdLog is null
                                            ? "SIN INTENTO"
                                            : "NO";

                                    table.Cell().Element(Celda).Text(T(item.EstadoEscaneo));
                                    table.Cell().Element(Celda).Text(T(item.ResultadoEscaneo));
                                    table.Cell().Element(Celda).Text(error);
                                    table.Cell().Element(Celda).Text(FechaDesdeTexto(item.FechaEmisionTexto));
                                    table.Cell().Element(Celda).Text(T(item.Documento));
                                    table.Cell().Element(Celda).Text(T(item.NumeroConsecutivo));
                                    table.Cell().Element(Celda).Text(T(item.CodigoCliente));
                                    table.Cell().Element(Celda).Text(T(item.NombreCliente));
                                    table.Cell().Element(Celda).Text(FechaHora(item.FechaIntento));
                                    table.Cell().Element(Celda).Text(T(item.NombreArchivo));
                                    table.Cell().Element(Celda).Text(T(item.MensajeLog));
                                }
                            });
                        }
                    });

                page.Footer()
                    .PaddingTop(8)
                    .AlignCenter()
                    .Text(text =>
                    {
                        text.Span("Página ");
                        text.CurrentPageNumber();
                        text.Span(" de ");
                        text.TotalPages();
                    });
            });
        });

        return documento.GeneratePdf();
    }

    /// <summary>
    /// Obtiene las facturas base desde VENDOCENCFED.
    ///
    /// incluirTipo03 = false:
    ///     obtiene únicamente TIPODOC 01, 04 y 09.
    ///
    /// incluirTipo03 = true:
    ///     obtiene TIPODOC 01, 03, 04 y 09.
    ///
    /// Se conserva TIPODOC únicamente de forma interna para poder aplicar
    /// la regla especial del tipo 03 sin tener que modificar el ViewModel.
    /// </summary>
    private async Task<List<FacturaSeguimientoDto>> ObtenerFacturasAsync(
        DateTime fechaInicio,
        DateTime fechaFin,
        bool incluirTipo03,
        CancellationToken cancellationToken)
    {
        // FECHAEMISION está modelada como string y viene en formato ISO.
        // Para mantener la consulta 100% sobre el DbContext y evitar SQL manual,
        // se filtra por el prefijo yyyy-MM-dd. Los bloques de 900 evitan superar
        // el límite de 1000 expresiones de un IN de Oracle.
        var fechas = new List<string>();

        for (var fecha = fechaInicio.Date;
             fecha <= fechaFin.Date;
             fecha = fecha.AddDays(1))
        {
            fechas.Add(fecha.ToString("yyyy-MM-dd"));
        }

        var resultado = new List<FacturaSeguimientoDto>();

        foreach (var bloqueFechas in Partir(fechas, 900))
        {
            var fechasConsulta = bloqueFechas.ToList();

            var consulta = _context.VENDOCENCFEDs
                .AsNoTracking()
                .Where(x =>
                    x.INDORACLE == "S" &&
                    x.ESTADO == "A" &&
                    x.FECHAEMISION != null &&
                    x.FECHAEMISION.Length >= 10 &&
                    fechasConsulta.Contains(x.FECHAEMISION.Substring(0, 10)));

            if (incluirTipo03)
            {
                consulta = consulta.Where(x =>
                    x.TIPODOC == "01" ||
                    x.TIPODOC == "03" ||
                    x.TIPODOC == "04" ||
                    x.TIPODOC == "09");
            }
            else
            {
                consulta = consulta.Where(x =>
                    x.TIPODOC == "01" ||
                    x.TIPODOC == "04" ||
                    x.TIPODOC == "09");
            }

            /*
             * Primero se proyectan valores simples desde EF.
             * Luego se construye el ViewModel en memoria para conservar TIPODOC
             * en FacturaSeguimientoDto sin tener que agregarlo al ViewModel.
             */
            var bloque = await consulta
                .Select(x => new
                {
                    x.TIPODOC,
                    x.CIA,
                    x.SUCURSAL,
                    x.DOCUMENTO,
                    x.CLAVE,
                    x.FECHAEMISION,
                    x.NUMEROCONSECUTIVO,
                    x.COD_CLIENTE,
                    x.RECEPTOR_NOMBRE,
                    x.COD_RUTA,
                    x.TOTALCOMPROBANTE
                })
                .ToListAsync(cancellationToken);

            resultado.AddRange(
                bloque.Select(x => new FacturaSeguimientoDto
                {
                    TipoDoc = x.TIPODOC,
                    Factura = new SeguimientoFacturaItemViewModel
                    {
                        Cia = x.CIA,
                        Sucursal = x.SUCURSAL,
                        Documento = x.DOCUMENTO,
                        Clave = x.CLAVE,
                        FechaEmisionTexto = x.FECHAEMISION,
                        NumeroConsecutivo = x.NUMEROCONSECUTIVO,
                        CodigoCliente = x.COD_CLIENTE,
                        NombreCliente = x.RECEPTOR_NOMBRE,
                        Ruta = x.COD_RUTA,
                        TotalComprobante = x.TOTALCOMPROBANTE
                    }
                }));
        }

        // Evita duplicados en caso de que la misma factura aparezca más de una vez
        // en VENDOCENCFED por la combinación utilizada para seguimiento.
        return resultado
            .GroupBy(x => new
            {
                Cia = Normalizar(x.Factura.Cia),
                Clave = Normalizar(x.Factura.Clave),
                Documento = Normalizar(x.Factura.Documento)
            })
            .Select(g => g.First())
            .OrderByDescending(x => x.Factura.FechaEmision)
            .ThenByDescending(x => x.Factura.Documento)
            .ToList();
    }

    /// <summary>
    /// Marca como pistoleadas las facturas cuya CIA + CLAVE exista en
    /// CXCDETFACREC o CXCDETFACRECBIT y cuyo encabezado CXCENCFACREC
    /// relacionado esté activo.
    ///
    /// Este método no decide qué TIPODOC participa. Esa decisión se toma antes
    /// de llamarlo:
    /// - Pantalla de pistoleo: solo 01, 04 y 09.
    /// - Pantalla de escaneo: solo se llama para 01, 04 y 09.
    /// </summary>
    private async Task<List<SeguimientoFacturaItemViewModel>> CompletarPistoleoAsync(
        List<SeguimientoFacturaItemViewModel> facturas,
        CancellationToken cancellationToken)
    {
        if (facturas.Count == 0)
            return facturas;

        var claves = facturas
            .Select(x => Normalizar(x.Clave))
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var detalle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bitacora = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var bloqueClaves in Partir(claves, 900))
        {
            var clavesConsulta = bloqueClaves.ToList();

            var clavesDetalle = await (
                from d in _context.CXCDETFACRECs.AsNoTracking()
                join e in _context.CXCENCFACRECs.AsNoTracking()
                    on new { d.COD_CIA, d.SUCURSAL, d.DOCUMENTO }
                    equals new { e.COD_CIA, e.SUCURSAL, e.DOCUMENTO }
                where e.ESTADO == "A" &&
                      clavesConsulta.Contains(d.CLAVE)
                select new
                {
                    d.COD_CIA,
                    d.CLAVE
                })
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var item in clavesDetalle)
                detalle.Add(CrearLlave(item.COD_CIA, item.CLAVE));

            var clavesBitacora = await (
                from b in _context.CXCDETFACRECBITs.AsNoTracking()
                join e in _context.CXCENCFACRECs.AsNoTracking()
                    on new { b.COD_CIA, b.SUCURSAL, b.DOCUMENTO }
                    equals new { e.COD_CIA, e.SUCURSAL, e.DOCUMENTO }
                where e.ESTADO == "A" &&
                      clavesConsulta.Contains(b.CLAVE)
                select new
                {
                    b.COD_CIA,
                    b.CLAVE
                })
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var item in clavesBitacora)
                bitacora.Add(CrearLlave(item.COD_CIA, item.CLAVE));
        }

        foreach (var factura in facturas)
        {
            var llave = CrearLlave(factura.Cia, factura.Clave);

            factura.EnDetalle = detalle.Contains(llave);
            factura.EnBitacora = bitacora.Contains(llave);
            factura.PistoleadaProcesada =
                factura.EnDetalle ||
                factura.EnBitacora;
        }

        return facturas;
    }

    /// <summary>
    /// Filtra facturas pistoleadas por el tipo de persona del encabezado
    /// CXCENCFACREC relacionado con CXCDETFACREC o CXCDETFACRECBIT.
    ///
    /// A = Agente.
    /// T = Transportista.
    /// TODOS = no aplica filtro adicional.
    ///
    /// Si se selecciona A o T, una factura pendiente no puede aparecer porque
    /// todavía no tiene una relación activa de pistoleo con CXCENCFACREC.
    /// </summary>
    private async Task<List<SeguimientoFacturaItemViewModel>> FiltrarPistoleadasPorTipoPersonaAsync(
        IEnumerable<SeguimientoFacturaItemViewModel> items,
        string tipoPersona,
        CancellationToken cancellationToken)
    {
        var lista = items.ToList();

        if (lista.Count == 0 || tipoPersona == TipoPersonaTodos)
            return lista;

        var claves = lista
            .Where(x => x.PistoleadaProcesada)
            .Select(x => Normalizar(x.Clave))
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (claves.Count == 0)
            return new List<SeguimientoFacturaItemViewModel>();

        var llavesTipoPersona = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var bloqueClaves in Partir(claves, 900))
        {
            var clavesConsulta = bloqueClaves.ToList();

            var clavesDetalle = await (
                from d in _context.CXCDETFACRECs.AsNoTracking()
                join e in _context.CXCENCFACRECs.AsNoTracking()
                    on new { d.COD_CIA, d.SUCURSAL, d.DOCUMENTO }
                    equals new { e.COD_CIA, e.SUCURSAL, e.DOCUMENTO }
                where e.ESTADO == "A" &&
                      e.TIPOPERSONA != null &&
                      e.TIPOPERSONA.Trim().ToUpper() == tipoPersona &&
                      clavesConsulta.Contains(d.CLAVE)
                select new
                {
                    d.COD_CIA,
                    d.CLAVE
                })
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var item in clavesDetalle)
                llavesTipoPersona.Add(CrearLlave(item.COD_CIA, item.CLAVE));

            var clavesBitacora = await (
                from b in _context.CXCDETFACRECBITs.AsNoTracking()
                join e in _context.CXCENCFACRECs.AsNoTracking()
                    on new { b.COD_CIA, b.SUCURSAL, b.DOCUMENTO }
                    equals new { e.COD_CIA, e.SUCURSAL, e.DOCUMENTO }
                where e.ESTADO == "A" &&
                      e.TIPOPERSONA != null &&
                      e.TIPOPERSONA.Trim().ToUpper() == tipoPersona &&
                      clavesConsulta.Contains(b.CLAVE)
                select new
                {
                    b.COD_CIA,
                    b.CLAVE
                })
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var item in clavesBitacora)
                llavesTipoPersona.Add(CrearLlave(item.COD_CIA, item.CLAVE));
        }

        return lista
            .Where(x =>
                x.PistoleadaProcesada &&
                llavesTipoPersona.Contains(CrearLlave(x.Cia, x.Clave)))
            .OrderByDescending(x => x.FechaEmision)
            .ThenByDescending(x => x.Documento)
            .ToList();
    }

    /// <summary>
    /// Completa el estado de escaneo usando únicamente las facturas que ya
    /// pasaron la regla previa de elegibilidad:
    /// - 03: siempre elegible.
    /// - 01, 04, 09: únicamente si están pistoleadas.
    ///
    /// Para cada DOCUMENTO se toma el último LOG_ENVIO_PDF_ORACLE según
    /// FECHA_INTENTO DESC e ID_LOG DESC.
    /// </summary>
    private async Task<List<SeguimientoFacturaItemViewModel>> CompletarEscaneoAsync(
        List<SeguimientoFacturaItemViewModel> facturas,
        CancellationToken cancellationToken)
    {
        if (facturas.Count == 0)
            return facturas;

        var documentos = facturas
            .Select(x => Normalizar(x.Documento))
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var logs = new List<LogSeguimientoDto>();

        foreach (var bloqueDocumentos in Partir(documentos, 900))
        {
            var documentosConsulta = bloqueDocumentos.ToList();

            var bloqueLogs = await _context.LOG_ENVIO_PDF_ORACLEs
                .AsNoTracking()
                .Where(x =>
                    documentosConsulta.Contains(x.DOCUMENTO.Trim()))
                .Select(x => new LogSeguimientoDto
                {
                    IdLog = x.ID_LOG,
                    Documento = x.DOCUMENTO,
                    NombreArchivo = x.NOMBRE_ARCHIVO,
                    FechaIntento = x.FECHA_INTENTO,
                    Estado = x.ESTADO,
                    Mensaje = x.MENSAJE
                })
                .ToListAsync(cancellationToken);

            logs.AddRange(bloqueLogs);
        }

        var ultimoLogPorDocumento = logs
            .GroupBy(
                x => Normalizar(x.Documento),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(x => x.FechaIntento)
                    .ThenByDescending(x => x.IdLog)
                    .First(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var factura in facturas)
        {
            var documento = Normalizar(factura.Documento);

            if (!string.IsNullOrEmpty(documento) &&
                ultimoLogPorDocumento.TryGetValue(documento, out var log))
            {
                factura.IdLog = log.IdLog;
                factura.EstadoLog = Normalizar(log.Estado);
                factura.FechaIntento = log.FechaIntento;
                factura.NombreArchivo = log.NombreArchivo?.Trim();
                factura.MensajeLog = log.Mensaje?.Trim();
            }

            factura.EscaneadaProcesada = string.Equals(
                factura.EstadoLog,
                ResultadoProcesado,
                StringComparison.OrdinalIgnoreCase);
        }

        return facturas;
    }

    private static List<SeguimientoFacturaItemViewModel> FiltrarPistoleadas(
        IEnumerable<SeguimientoFacturaItemViewModel> items,
        string estado)
    {
        var consulta = items;

        if (estado == EstadoProcesadas)
            consulta = consulta.Where(x => x.PistoleadaProcesada);
        else if (estado == EstadoPendientes)
            consulta = consulta.Where(x => !x.PistoleadaProcesada);

        return consulta
            .OrderByDescending(x => x.FechaEmision)
            .ThenByDescending(x => x.Documento)
            .ToList();
    }

    private static List<SeguimientoFacturaItemViewModel> FiltrarEscaneadas(
        IEnumerable<SeguimientoFacturaItemViewModel> items,
        string estado,
        string resultado)
    {
        var consulta = items;

        if (estado == EstadoProcesadas)
            consulta = consulta.Where(x => x.EscaneadaProcesada);
        else if (estado == EstadoPendientes)
            consulta = consulta.Where(x => !x.EscaneadaProcesada);

        consulta = resultado switch
        {
            ResultadoProcesado => consulta.Where(x =>
                string.Equals(
                    x.EstadoLog,
                    ResultadoProcesado,
                    StringComparison.OrdinalIgnoreCase)),

            ResultadoError => consulta.Where(x =>
                x.EscaneadaConError),

            ResultadoSinIntento => consulta.Where(x =>
                x.IdLog is null),

            _ => consulta
        };

        return consulta
            .OrderByDescending(x => x.FechaEmision)
            .ThenByDescending(x => x.Documento)
            .ToList();
    }

    private static (string Estado, string? Error) ValidarFechasYEstado(
        DateTime fechaInicio,
        DateTime fechaFin,
        string? estado)
    {
        if (fechaInicio == default || fechaFin == default)
        {
            return (
                EstadoTodos,
                "Debe indicar la fecha inicial y la fecha final.");
        }

        if (fechaInicio.Date > fechaFin.Date)
        {
            return (
                EstadoTodos,
                "La fecha inicial no puede ser mayor que la fecha final.");
        }

        var estadoNormalizado = Normalizar(estado);

        if (string.IsNullOrEmpty(estadoNormalizado))
            estadoNormalizado = EstadoTodos;

        if (estadoNormalizado is not (
            EstadoTodos or
            EstadoPendientes or
            EstadoProcesadas))
        {
            return (
                EstadoTodos,
                "El estado debe ser TODOS, PENDIENTES o PROCESADAS.");
        }

        return (estadoNormalizado, null);
    }

    private static (string TipoPersona, string? Error) ValidarTipoPersona(
        string? tipoPersona)
    {
        var tipoPersonaNormalizado = Normalizar(tipoPersona);

        if (string.IsNullOrEmpty(tipoPersonaNormalizado))
            tipoPersonaNormalizado = TipoPersonaTodos;

        if (tipoPersonaNormalizado is not (
            TipoPersonaTodos or
            TipoPersonaAgente or
            TipoPersonaTransportista))
        {
            return (
                TipoPersonaTodos,
                "El tipo de persona debe ser TODOS, A (Agente) o T (Transportista).");
        }

        return (tipoPersonaNormalizado, null);
    }

    private static IEnumerable<List<T>> Partir<T>(
        IReadOnlyList<T> datos,
        int tamano)
    {
        for (var i = 0; i < datos.Count; i += tamano)
        {
            var cantidad = Math.Min(
                tamano,
                datos.Count - i);

            var bloque = new List<T>(cantidad);

            for (var j = 0; j < cantidad; j++)
                bloque.Add(datos[i + j]);

            yield return bloque;
        }
    }

    private static string CrearLlave(
        string? cia,
        string? clave) =>
        $"{Normalizar(cia)}|{Normalizar(clave)}";

    private static string Normalizar(string? valor) =>
        (valor ?? string.Empty)
            .Trim()
            .ToUpperInvariant();

    /// <summary>
    /// DTO interno utilizado únicamente para conservar TIPODOC junto con el
    /// ViewModel sin requerir cambios en SeguimientoFacturaItemViewModel.
    /// </summary>
    private sealed class FacturaSeguimientoDto
    {
        public string TipoDoc { get; set; } = string.Empty;

        public SeguimientoFacturaItemViewModel Factura { get; set; } = null!;
    }

    private sealed class LogSeguimientoDto
    {
        public decimal IdLog { get; set; }

        public string Documento { get; set; } = string.Empty;

        public string? NombreArchivo { get; set; }

        public DateTime FechaIntento { get; set; }

        public string Estado { get; set; } = string.Empty;

        public string? Mensaje { get; set; }
    }
}