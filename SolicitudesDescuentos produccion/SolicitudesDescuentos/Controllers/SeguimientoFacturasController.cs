using System;
using System.Collections.Generic;
using System.Data;
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

    private const string TipoDocTodos = "TODOS";
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
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var hoy = DateTime.Today;

        var vm = new SeguimientoFacturasIndexViewModel
        {
            FechaInicio = hoy.AddDays(-15),
            FechaFin = hoy,
            TiposDocumento = await ObtenerTiposDocumentoAsync(cancellationToken)
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> BuscarPistoleadas(
        DateTime fechaInicio,
        DateTime fechaFin,
        string? estado,
        string? tipoPersona,
        string? tipoDocumento,
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
        tipoDocumento = NormalizarTipoDocumento(tipoDocumento);

        try
        {
            var facturasBase = await ObtenerFacturasAsync(
                fechaInicio,
                fechaFin,
                tipoDocumento,
                cancellationToken);

            var items = await CompletarPistoleoAsync(
                facturasBase.Select(x => x.Factura).ToList(),
                cancellationToken);

            var itemsFiltrados = FiltrarPistoleadas(items, estado);

            itemsFiltrados = await FiltrarPistoleadasPorTipoPersonaAsync(
                itemsFiltrados,
                tipoPersona,
                cancellationToken);

            return Json(new SeguimientoFacturasRespuestaViewModel
            {
                Ok = true,
                Total = items.Count,
                TotalProcesadas = items.Count(x => x.PistoleadaProcesada),
                TotalPendientes = items.Count(x => !x.PistoleadaProcesada),
                Items = itemsFiltrados
            });
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

    [HttpGet]
    public async Task<IActionResult> BuscarEscaneadas(
        DateTime fechaInicio,
        DateTime fechaFin,
        string? estado,
        string? resultado,
        string? tipoDocumento,
        CancellationToken cancellationToken)
    {
        var validacion = ValidarFechasYEstado(fechaInicio, fechaFin, estado);
        if (validacion.Error is not null)
            return BadRequest(new { ok = false, mensaje = validacion.Error });

        fechaInicio = fechaInicio.Date;
        fechaFin = fechaFin.Date;
        estado = validacion.Estado;
        resultado = Normalizar(resultado);
        tipoDocumento = NormalizarTipoDocumento(tipoDocumento);

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
            var facturasBase = await ObtenerFacturasAsync(
                fechaInicio,
                fechaFin,
                tipoDocumento,
                cancellationToken);

            // La nota de crédito 03 entra directamente al seguimiento de escaneo.
            // Los demás tipos deben haber sido pistoleados.
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

            return Json(new SeguimientoFacturasRespuestaViewModel
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
            });
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

    [HttpGet]
    public async Task<IActionResult> DescargarPdf(
        string tipo,
        DateTime fechaInicio,
        DateTime fechaFin,
        string? estado,
        string? resultado,
        string? tipoPersona,
        string? tipoDocumento,
        CancellationToken cancellationToken)
    {
        var validacion = ValidarFechasYEstado(fechaInicio, fechaFin, estado);
        if (validacion.Error is not null)
            return BadRequest(new { ok = false, mensaje = validacion.Error });

        fechaInicio = fechaInicio.Date;
        fechaFin = fechaFin.Date;
        estado = validacion.Estado;
        tipo = Normalizar(tipo);
        tipoDocumento = NormalizarTipoDocumento(tipoDocumento);

        if (tipo is not ("PISTOLEADAS" or "ESCANEADAS"))
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = "El tipo debe ser PISTOLEADAS o ESCANEADAS."
            });
        }

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
            var esEscaneadas = tipo == "ESCANEADAS";

            var facturasBase = await ObtenerFacturasAsync(
                fechaInicio,
                fechaFin,
                tipoDocumento,
                cancellationToken);

            if (!esEscaneadas)
            {
                items = await CompletarPistoleoAsync(
                    facturasBase.Select(x => x.Factura).ToList(),
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

                items = FiltrarEscaneadas(items, estado, resultado);

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
                tipoPersona!,
                tipoDocumento,
                esEscaneadas);

            var nombreArchivo =
                $"Facturas_{tipo}_{fechaInicio:yyyyMMdd}_{fechaFin:yyyyMMdd}.pdf";

            return File(pdf, "application/pdf", nombreArchivo);
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

    private static byte[] GenerarPdfFacturas(
        string titulo,
        IReadOnlyList<SeguimientoFacturaItemViewModel> items,
        DateTime fechaInicio,
        DateTime fechaFin,
        string estado,
        string resultado,
        string tipoPersona,
        string tipoDocumento,
        bool esEscaneadas)
    {
        static string T(string? valor) => (valor ?? string.Empty).Trim();

        static string FechaDesdeTexto(string? valor)
        {
            var texto = T(valor);
            return texto.Length >= 10 ? texto[..10] : texto;
        }

        static string FechaHora(DateTime? valor) =>
            valor.HasValue
                ? valor.Value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
                : string.Empty;

        static string Monto(decimal valor) =>
            valor.ToString("N2", CultureInfo.GetCultureInfo("es-CR"));

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

                        column.Item()
                            .Text(
                                $"Tipo documento: " +
                                $"{(tipoDocumento == TipoDocTodos ? "Todos" : tipoDocumento)}");

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
                        else
                        {
                            column.Item()
                                .Text(
                                    $"Último intento: " +
                                    $"{(string.IsNullOrWhiteSpace(resultado) ? EstadoTodos : resultado)}");
                        }

                        column.Item().Text($"Facturas mostradas: {items.Count}");
                    });

                page.Content().Element(content =>
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
                                columns.ConstantColumn(58);
                                columns.ConstantColumn(58);
                                columns.ConstantColumn(58);
                                columns.ConstantColumn(70);
                                columns.ConstantColumn(95);
                                columns.ConstantColumn(65);
                                columns.RelativeColumn(1.7f);
                                columns.ConstantColumn(48);
                                columns.ConstantColumn(70);
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
                                columns.ConstantColumn(52);
                                columns.ConstantColumn(62);
                                columns.ConstantColumn(48);
                                columns.ConstantColumn(55);
                                columns.ConstantColumn(65);
                                columns.ConstantColumn(82);
                                columns.ConstantColumn(58);
                                columns.RelativeColumn(1.15f);
                                columns.ConstantColumn(78);
                                columns.RelativeColumn(0.85f);
                                columns.RelativeColumn(1.35f);
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

    private async Task<List<FacturaSeguimientoDto>> ObtenerFacturasAsync(
        DateTime fechaInicio,
        DateTime fechaFin,
        string tipoDocumento,
        CancellationToken cancellationToken)
    {
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

            if (tipoDocumento != TipoDocTodos)
            {
                consulta = consulta.Where(x =>
                    x.TIPODOC != null &&
                    x.TIPODOC.Trim().ToUpper() == tipoDocumento);
            }

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
        var consulta = items.AsEnumerable();

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
        var consulta = items.AsEnumerable();

        if (estado == EstadoProcesadas)
            consulta = consulta.Where(x => x.EscaneadaProcesada);
        else if (estado == EstadoPendientes)
            consulta = consulta.Where(x => !x.EscaneadaProcesada);

        if (resultado == ResultadoProcesado)
        {
            consulta = consulta.Where(x =>
                string.Equals(
                    x.EstadoLog,
                    ResultadoProcesado,
                    StringComparison.OrdinalIgnoreCase));
        }
        else if (resultado == ResultadoError)
        {
            consulta = consulta.Where(x => x.EscaneadaConError);
        }
        else if (resultado == ResultadoSinIntento)
        {
            consulta = consulta.Where(x => x.IdLog is null);
        }

        return consulta
            .OrderByDescending(x => x.FechaEmision)
            .ThenByDescending(x => x.Documento)
            .ToList();
    }

    private async Task<List<TipoDocumentoFiltroViewModel>> ObtenerTiposDocumentoAsync(
        CancellationToken cancellationToken)
    {
        var conexion = _context.Database.GetDbConnection();
        var cerrarConexion = conexion.State != ConnectionState.Open;

        if (cerrarConexion)
            await conexion.OpenAsync(cancellationToken);

        try
        {
            // Primero intenta con el esquema usado por LancoDbContext.
            // Si existe un sinónimo o la conexión ya apunta al esquema, se usa el fallback.
            try
            {
                return await LeerTiposDocumentoDesdeTablaAsync(
                    conexion,
                    "SELECT * FROM NUEVO.FE_TIPODOC",
                    cancellationToken);
            }
            catch
            {
                return await LeerTiposDocumentoDesdeTablaAsync(
                    conexion,
                    "SELECT * FROM FE_TIPODOC",
                    cancellationToken);
            }
        }
        finally
        {
            if (cerrarConexion && conexion.State == ConnectionState.Open)
                conexion.Close();
        }
    }

    private static async Task<List<TipoDocumentoFiltroViewModel>> LeerTiposDocumentoDesdeTablaAsync(
        System.Data.Common.DbConnection conexion,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var comando = conexion.CreateCommand();
        comando.CommandText = sql;

        await using var reader = await comando.ExecuteReaderAsync(cancellationToken);

        var nombres = Enumerable.Range(0, reader.FieldCount)
            .Select(i => new
            {
                Indice = i,
                Nombre = Normalizar(reader.GetName(i))
            })
            .ToList();

        var nombresCodigo = new[]
        {
            "CODIGO",
            "COD_TIPODOC",
            "COD_TIPO_DOC",
            "TIPODOC",
            "TIPO_DOC",
            "CODIGO_TIPODOC",
            "CODIGO_TIPO_DOC",
            "CODE"
        };

        var nombresDescripcion = new[]
        {
            "DESCRIPCION",
            "DESCRIP",
            "NOMBRE",
            "DETALLE",
            "DESCRIPCION_TIPODOC",
            "DESCRIPCION_TIPO_DOC",
            "NAME"
        };

        var indiceCodigo = nombres
            .Where(x => nombresCodigo.Contains(x.Nombre))
            .Select(x => x.Indice)
            .DefaultIfEmpty(0)
            .First();

        var indiceDescripcion = nombres
            .Where(x =>
                x.Indice != indiceCodigo &&
                nombresDescripcion.Contains(x.Nombre))
            .Select(x => x.Indice)
            .DefaultIfEmpty(-1)
            .First();

        if (indiceDescripcion < 0 && reader.FieldCount > 1)
            indiceDescripcion = indiceCodigo == 0 ? 1 : 0;

        var tipos = new List<TipoDocumentoFiltroViewModel>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var codigo = reader.IsDBNull(indiceCodigo)
                ? string.Empty
                : Convert.ToString(
                    reader.GetValue(indiceCodigo),
                    CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(codigo))
                continue;

            var descripcion =
                indiceDescripcion >= 0 && !reader.IsDBNull(indiceDescripcion)
                    ? Convert.ToString(
                        reader.GetValue(indiceDescripcion),
                        CultureInfo.InvariantCulture)?.Trim() ?? string.Empty
                    : string.Empty;

            tipos.Add(new TipoDocumentoFiltroViewModel
            {
                Codigo = codigo,
                Descripcion = descripcion
            });
        }

        return tipos
            .GroupBy(x => Normalizar(x.Codigo))
            .Select(g => g.First())
            .OrderBy(x => x.Codigo)
            .ToList();
    }

    private static (string Estado, string? Error) ValidarFechasYEstado(
        DateTime fechaInicio,
        DateTime fechaFin,
        string? estado)
    {
        if (fechaInicio == default || fechaFin == default)
            return (string.Empty, "Debe indicar la fecha inicial y la fecha final.");

        if (fechaInicio.Date > fechaFin.Date)
            return (string.Empty, "La fecha inicial no puede ser mayor que la fecha final.");

        var estadoNormalizado = Normalizar(estado);

        if (string.IsNullOrEmpty(estadoNormalizado))
            estadoNormalizado = EstadoTodos;

        if (estadoNormalizado is not (EstadoTodos or EstadoPendientes or EstadoProcesadas))
        {
            return (
                string.Empty,
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
                string.Empty,
                "El tipo de persona debe ser TODOS, A o T.");
        }

        return (tipoPersonaNormalizado, null);
    }

    private static string NormalizarTipoDocumento(string? tipoDocumento)
    {
        var valor = Normalizar(tipoDocumento);
        return string.IsNullOrWhiteSpace(valor) ? TipoDocTodos : valor;
    }

    private static string Normalizar(string? valor) =>
        (valor ?? string.Empty).Trim().ToUpperInvariant();

    private static string CrearLlave(string? cia, string? clave) =>
        $"{Normalizar(cia)}|{Normalizar(clave)}";

    private static IEnumerable<List<T>> Partir<T>(
        IReadOnlyList<T> elementos,
        int tamano)
    {
        for (var i = 0; i < elementos.Count; i += tamano)
        {
            yield return elementos
                .Skip(i)
                .Take(tamano)
                .ToList();
        }
    }

    private sealed class FacturaSeguimientoDto
    {
        public string TipoDoc { get; set; } = string.Empty;
        public SeguimientoFacturaItemViewModel Factura { get; set; } = null!;
    }

    private sealed class LogSeguimientoDto
    {
        public decimal IdLog { get; set; }
        public string? Documento { get; set; }
        public string? NombreArchivo { get; set; }
        public DateTime? FechaIntento { get; set; }
        public string? Estado { get; set; }
        public string? Mensaje { get; set; }
    }
}
