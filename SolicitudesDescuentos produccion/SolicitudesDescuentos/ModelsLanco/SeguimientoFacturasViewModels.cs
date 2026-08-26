using System;
using System.Collections.Generic;
using System.Globalization;

namespace SolicitudesDescuentos.ModelsLanco
{
    public sealed class SeguimientoFacturasIndexViewModel
    {
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
    }

    public sealed class SeguimientoFacturaItemViewModel
    {
        public string Cia { get; set; } = string.Empty;
        public string? Sucursal { get; set; }
        public string? Documento { get; set; }
        public string Clave { get; set; } = string.Empty;

        // Se usa únicamente durante la proyección desde VENDOCENCFED.
        // FECHAEMISION está modelada como string en el DbContext.
        public string? FechaEmisionTexto { get; set; }

        public DateTime FechaEmision
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FechaEmisionTexto))
                    return DateTime.MinValue;

                var valor = FechaEmisionTexto.Length >= 10
                    ? FechaEmisionTexto[..10]
                    : FechaEmisionTexto;

                return DateTime.TryParseExact(
                    valor,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var fecha)
                        ? fecha
                        : DateTime.MinValue;
            }
        }

        public string NumeroConsecutivo { get; set; } = string.Empty;
        public string? CodigoCliente { get; set; }
        public string? NombreCliente { get; set; }
        public string? Ruta { get; set; }
        public decimal TotalComprobante { get; set; }

        public bool EnDetalle { get; set; }
        public bool EnBitacora { get; set; }
        public bool PistoleadaProcesada { get; set; }
        public bool EscaneadaProcesada { get; set; }

        public decimal? IdLog { get; set; }
        public string? EstadoLog { get; set; }
        public DateTime? FechaIntento { get; set; }
        public string? NombreArchivo { get; set; }
        public string? MensajeLog { get; set; }

        public string OrigenPistoleo => (EnDetalle, EnBitacora) switch
        {
            (true, true) => "Detalle y bitácora",
            (true, false) => "Detalle",
            (false, true) => "Bitácora",
            _ => "Pendiente"
        };

        public string EstadoPistoleo => PistoleadaProcesada ? "PROCESADA" : "PENDIENTE";
        public string EstadoEscaneo => EscaneadaProcesada ? "PROCESADA" : "PENDIENTE";

        public bool EscaneadaConError => string.Equals(
            EstadoLog?.Trim(),
            "ERROR",
            StringComparison.OrdinalIgnoreCase);

        public string ResultadoEscaneo
        {
            get
            {
                if (IdLog is null)
                    return "SIN INTENTO";

                return string.IsNullOrWhiteSpace(EstadoLog)
                    ? "SIN ESTADO"
                    : EstadoLog.Trim().ToUpperInvariant();
            }
        }
    }

    public sealed class SeguimientoFacturasRespuestaViewModel
    {
        public bool Ok { get; set; }
        public string? Mensaje { get; set; }

        // Totales del rango completo, antes de aplicar los filtros de pantalla.
        public int Total { get; set; }
        public int TotalProcesadas { get; set; }
        public int TotalPendientes { get; set; }
        public int TotalLogProcesado { get; set; }
        public int TotalLogError { get; set; }
        public int TotalSinIntento { get; set; }

        public int TotalMostradas => Items.Count;

        public List<SeguimientoFacturaItemViewModel> Items { get; set; } = new();
    }
}
