// Controllers/ReportesCobrosController.cs
// Adaptado para leer los resultados calculados por CALCULA_COMISIONES_XXORA.
// Fuentes de los reportes:
// - Agentes: CXC_DETAGE_COBRO / CXC_AGE_COBRO.
// - Cobros diarios por agente: XXORA_COMISIONES.
// - Impulsadores: CXC_CLIENTE_COBRO / CXC_EMPLEADO_COBRO.
// - Configuración de impulsadores: IMPULSADORESORACLE.


using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SolicitudesDescuentos.Data;
using SolicitudesDescuentos.ModelsOracle;
using SolicitudesDescuentos.ModelsOracle.ViewModels.Reportes;
using System.Globalization;
using QDocument = QuestPDF.Fluent.Document;
using QLicenseType = QuestPDF.Infrastructure.LicenseType;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Data.Common;
using System.Threading;

namespace SolicitudesDescuentos.Controllers
{
    public class ReportesCobrosController : Controller
    {
        private readonly OracleContext _context;
        private readonly LancoDbContext _lancoContext;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;

        private static readonly SemaphoreSlim CalculoComisionesLock = new(1, 1);
        private static readonly SemaphoreSlim ActualizacionNaviusLock = new(1, 1);
        private const string UltimaFirmaCalculoCacheKey = "REPORTES_COBROS_ULTIMA_FIRMA_CALCULO";
        private const string SucursalFija = "001";
        private const string LineaCobroFija = "001";
        private const string DescripcionLineaCobroFija = "CREDITO NORMAL";
        // Moneda base real del negocio. Ya no se expone al usuario.
        private const string MonedaBaseSistema = "CRC";

        public sealed class ParametrosCalculoComisionVm
        {
            public int AnoFiscal { get; set; }
            public int Periodo { get; set; }
            public string? TipoChkDev1 { get; set; }
            public string? TipoChkDev2 { get; set; }
            public string? TipoDescuento { get; set; }
            public string? PorcentajeImpuesto { get; set; }
            public string? AplicarImpuesto { get; set; }
        }

        private sealed class ClienteFiltroOpcion
        {
            public string IdCliente { get; set; } = "";
            public string Nombre { get; set; } = "";
        }

        private sealed class ResumenComisionAgenteFila
        {
            public string CodComision { get; set; } = "";
            public string DesComision { get; set; } = "";

            // Monto mostrado en el resumen:
            // monto bruto menos el descuento acumulado.
            public decimal Monto { get; set; }

            public decimal MontoFacturaSinImpuesto { get; set; }
            public decimal MontoComision { get; set; }
        }

        private sealed class ResumenAgenteComisionesFila
        {
            public string GrupoCodigo { get; set; } = "";
            public string GrupoDescripcion { get; set; } = "";
            public string CodVendedor { get; set; } = "";
            public string NombreVendedor { get; set; } = "";

            public List<ResumenComisionAgenteFila> Comisiones { get; set; }
                = new();

            public decimal Monto =>
                Comisiones.Sum(x => x.Monto);

            public decimal MontoFacturaSinImpuesto =>
                Comisiones.Sum(x => x.MontoFacturaSinImpuesto);

            public decimal MontoComision =>
                Comisiones.Sum(x => x.MontoComision);
        }

        private sealed class ImpulsadorDetalleFila
        {
            public string Empleado { get; set; } = "";
            public string NombreEmpleado { get; set; } = "";
            public decimal Porcentaje { get; set; }
            public decimal MontoComision { get; set; }
        }

        private sealed class ImpulsadorClienteFila
        {
            public string CodAgente { get; set; } = "";
            public string NombreAgente { get; set; } = "";
            public string GrupoCodigo { get; set; } = "";
            public string GrupoDescripcion { get; set; } = "";
            public string CodCliente { get; set; } = "";
            public string NombreCliente { get; set; } = "";
            public decimal CobroBruto { get; set; }
            public decimal MontoSinImpuesto { get; set; }
            public decimal MontoComision { get; set; }
            public List<ImpulsadorDetalleFila> Impulsadores { get; set; } = new();
        }

        private sealed class ImpulsadorOracleConfiguracion
        {
            public string Cliente { get; init; } = "";
            public string Empleado { get; init; } = "";
            public decimal Porcentaje { get; init; }
        }

        private sealed class AcumuladoCobrosPeriodo
        {
            public decimal Cobros { get; set; }
            public decimal ChequesDevueltos { get; set; }
            public decimal Descuentos { get; set; }

            public decimal CobroNeto =>
                Cobros - ChequesDevueltos - Descuentos;
        }


        private sealed class CobrosDiariosAgenteFila
        {
            public string CodAgente { get; set; } = "";
            public string NombreAgente { get; set; } = "";
            public string GrupoCodigo { get; set; } = "";
            public string GrupoDescripcion { get; set; } = "";

            public decimal CobrosDia { get; set; }
            public decimal ChequesDevueltos { get; set; }
            public decimal Descuentos { get; set; }
            public decimal CobroNeto { get; set; }
            public decimal CobrosMes { get; set; }
        }

        private sealed class CobrosDiariosAgenteTrabajo
        {
            public string CodAgente { get; set; } = "";
            public string NombreAgente { get; set; } = "";
            public string GrupoCodigo { get; set; } = "";
            public string GrupoDescripcion { get; set; } = "";

            public AcumuladoCobrosPeriodo Periodo { get; } = new();
        }


        public ReportesCobrosController(
            OracleContext context,
            LancoDbContext lancoContext,
            IMemoryCache cache,
            IConfiguration configuration)
        {
            _context = context;
            _lancoContext = lancoContext;
            _cache = cache;
            _configuration = configuration;
        }

        private sealed class CatalogosReporte
        {
            public Dictionary<string, GEN_VENDEDOR> Vendedores { get; init; }
                = new(StringComparer.OrdinalIgnoreCase);

            public Dictionary<
                (string Cliente, string Vendedor),
                XXORA_CUSTOMER_MASTER> ClientesPorRelacion
            { get; init; }
                = new();

            public Dictionary<string, XXORA_CUSTOMER_MASTER> ClientesPorCodigo
            { get; init; }
                = new(StringComparer.OrdinalIgnoreCase);

            // Relación auxiliar IDCLIENTE -> REGISTRY_ID/PARTY_NUMBER.
            // Se conserva para respaldos de nombres y datos históricos;
            // los filtros de cliente se aplican directamente por IDCLIENTE.
            public Dictionary<string, string> PartyNumberPorIdCliente
            { get; init; }
                = new(StringComparer.OrdinalIgnoreCase);
        }

        private async Task<CatalogosReporte> ObtenerCatalogosAsync(string bu)
        {
            var cacheKey = $"REPORTES_COBROS_CATALOGOS_{Normalizar(bu)}";

            var catalogos = await _cache.GetOrCreateAsync(
                cacheKey,
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow =
                        TimeSpan.FromMinutes(10);

                    // DbContext no permite consultas simultáneas sobre la misma instancia.
                    var vendedores = await _context.GEN_VENDEDORs
                        .AsNoTracking()
                        .Where(x => x.BU_NOMBRE == bu)
                        .ToListAsync();

                    var clientes = await _context.XXORA_CUSTOMER_MASTERs
                        .AsNoTracking()
                        .Where(x => x.BU_NOMBRE == bu)
                        .ToListAsync();

                    var partyNumbersPorIdCliente =
                        await ObtenerPartyNumbersPorIdClienteAsync(bu);

                    return ConstruirCatalogos(
                        vendedores,
                        clientes,
                        partyNumbersPorIdCliente);
                });

            return catalogos ?? new CatalogosReporte();
        }

        private async Task<Dictionary<string, string>>
            ObtenerPartyNumbersPorIdClienteAsync(string bu)
        {
            var resultado = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            var connection = _context.Database.GetDbConnection();
            var cerrarConexion = connection.State != ConnectionState.Open;

            try
            {
                if (cerrarConexion)
                    await connection.OpenAsync();

                using var command = connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = @"
                    SELECT DISTINCT
                           TRIM(IDCLIENTE)  AS IDCLIENTE,
                           TRIM(REGISTRY_ID) AS PARTY_NUMBER
                      FROM BG_INTUSER.XXORA_CUSTOMER_MASTER
                     WHERE TRIM(UPPER(BU_NOMBRE)) = :P_BU
                       AND IDCLIENTE IS NOT NULL
                       AND REGISTRY_ID IS NOT NULL";

                var bindByName = command
                    .GetType()
                    .GetProperty("BindByName");

                if (bindByName?.CanWrite == true)
                    bindByName.SetValue(command, true);

                AgregarParametro(
                    command,
                    "P_BU",
                    Normalizar(bu),
                    DbType.String);

                using var reader = await command.ExecuteReaderAsync();

                var ordinalIdCliente =
                    reader.GetOrdinal("IDCLIENTE");

                var ordinalPartyNumber =
                    reader.GetOrdinal("PARTY_NUMBER");

                while (await reader.ReadAsync())
                {
                    var idCliente = reader.IsDBNull(ordinalIdCliente)
                        ? ""
                        : reader.GetString(ordinalIdCliente).Trim();

                    var partyNumber = reader.IsDBNull(ordinalPartyNumber)
                        ? ""
                        : reader.GetString(ordinalPartyNumber).Trim();

                    if (string.IsNullOrWhiteSpace(idCliente) ||
                        string.IsNullOrWhiteSpace(partyNumber))
                    {
                        continue;
                    }

                    var idNormalizado = Normalizar(idCliente);

                    // Un IDCLIENTE debe pertenecer a un solo PARTY_NUMBER/REGISTRY_ID.
                    // Si la vista/maestro repite el sitio, conservamos la primera
                    // relación encontrada.
                    if (!resultado.ContainsKey(idNormalizado))
                        resultado[idNormalizado] = Normalizar(partyNumber);
                }

                return resultado;
            }
            finally
            {
                if (cerrarConexion &&
                    connection.State == ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }
        }

        private async Task<Dictionary<string, string>>
            ObtenerDescripcionesComisionAsync(string bu)
        {
            var resultado = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            var connection = _context.Database.GetDbConnection();
            var cerrarConexion =
                connection.State != ConnectionState.Open;

            try
            {
                if (cerrarConexion)
                    await connection.OpenAsync();

                using var command = connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = @"
                    SELECT TRIM(COD_COMISION) AS COD_COMISION,
                           MAX(TRIM(DES_COMISION)) AS DES_COMISION
                      FROM BG_INTUSER.GEN_MAS_COMISION
                     WHERE TRIM(UPPER(BU_NOMBRE)) = :P_BU
                       AND TRIM(UPPER(TIPO_COMISION)) = 'C'
                       AND COD_COMISION IS NOT NULL
                     GROUP BY TRIM(COD_COMISION)";

                var bindByName = command
                    .GetType()
                    .GetProperty("BindByName");

                if (bindByName?.CanWrite == true)
                    bindByName.SetValue(command, true);

                AgregarParametro(
                    command,
                    "P_BU",
                    Normalizar(bu),
                    DbType.String);

                using var reader =
                    await command.ExecuteReaderAsync();

                var ordinalCodigo =
                    reader.GetOrdinal("COD_COMISION");

                var ordinalDescripcion =
                    reader.GetOrdinal("DES_COMISION");

                while (await reader.ReadAsync())
                {
                    var codigo =
                        reader.IsDBNull(ordinalCodigo)
                            ? ""
                            : reader
                                .GetString(ordinalCodigo)
                                .Trim();

                    var descripcion =
                        reader.IsDBNull(ordinalDescripcion)
                            ? ""
                            : reader
                                .GetString(ordinalDescripcion)
                                .Trim();

                    if (string.IsNullOrWhiteSpace(codigo))
                        continue;

                    resultado[Normalizar(codigo)] =
                        string.IsNullOrWhiteSpace(descripcion)
                            ? codigo
                            : descripcion;
                }

                return resultado;
            }
            finally
            {
                if (cerrarConexion &&
                    connection.State == ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }
        }


        private static string ObtenerPartyNumberCliente(
            CatalogosReporte catalogos,
            string? idCliente)
        {
            var idNormalizado = Normalizar(idCliente);

            if (catalogos.PartyNumberPorIdCliente.TryGetValue(
                    idNormalizado,
                    out var partyNumber) &&
                !string.IsNullOrWhiteSpace(partyNumber))
            {
                return Normalizar(partyNumber);
            }

            // Respaldo para registros históricos que no encuentren relación.
            return idNormalizado;
        }

        private static CatalogosReporte ConstruirCatalogos(
            List<GEN_VENDEDOR> vendedores,
            List<XXORA_CUSTOMER_MASTER> clientes,
            Dictionary<string, string> partyNumbersPorIdCliente)
        {
            var vendedoresDic = vendedores
                .SelectMany(vendedor =>
                    new[]
                    {
                        vendedor.IDVENDEDOR,
                        vendedor.REGISTRY_ID
                    }
                    .Where(codigo => !string.IsNullOrWhiteSpace(codigo))
                    .Select(codigo => new
                    {
                        Clave = Normalizar(codigo),
                        Vendedor = vendedor
                    }))
                .GroupBy(x => x.Clave)
                .ToDictionary(
                    g => g.Key,
                    g => g.First().Vendedor,
                    StringComparer.OrdinalIgnoreCase);

            var relaciones = clientes
                .SelectMany(cliente =>
                {
                    var codigosCliente = new[]
                    {
                        cliente.IDCLIENTE,
                        cliente.REGISTRY_ID
                    }
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(Normalizar)
                    .Distinct()
                    .ToList();

                    var codigosVendedor = new[]
                    {
                        cliente.IDVENDEDOR,
                        cliente.VENDEDOR
                    }
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(Normalizar)
                    .Distinct()
                    .ToList();

                    return
                        from codigoCliente in codigosCliente
                        from codigoVendedor in codigosVendedor
                        select new
                        {
                            Clave = (
                                Cliente: codigoCliente,
                                Vendedor: codigoVendedor),
                            Cliente = cliente
                        };
                })
                .GroupBy(x => x.Clave)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .OrderByDescending(x =>
                            !string.IsNullOrWhiteSpace(
                                x.Cliente.PORCIENTO_VENDEDOR))
                        .First()
                        .Cliente);

            var porCodigo = clientes
                .SelectMany(cliente =>
                    new[]
                    {
                        cliente.IDCLIENTE,
                        cliente.REGISTRY_ID
                    }
                    .Where(codigo => !string.IsNullOrWhiteSpace(codigo))
                    .Select(codigo => new
                    {
                        Clave = Normalizar(codigo),
                        Cliente = cliente
                    }))
                .GroupBy(x => x.Clave)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .OrderByDescending(x =>
                            !string.IsNullOrWhiteSpace(
                                x.Cliente.PORCIENTO_VENDEDOR))
                        .First()
                        .Cliente,
                    StringComparer.OrdinalIgnoreCase);

            return new CatalogosReporte
            {
                Vendedores = vendedoresDic,
                ClientesPorRelacion = relaciones,
                ClientesPorCodigo = porCodigo,
                PartyNumberPorIdCliente = partyNumbersPorIdCliente
            };
        }

        private static XXORA_CUSTOMER_MASTER? BuscarClienteRapido(
            CatalogosReporte catalogos,
            string? codCliente,
            string? codVendedor)
        {
            var clienteKey = Normalizar(codCliente);
            var vendedorKey = Normalizar(codVendedor);

            if (catalogos.ClientesPorRelacion.TryGetValue(
                    (clienteKey, vendedorKey),
                    out var clienteRelacion))
            {
                return clienteRelacion;
            }

            catalogos.ClientesPorCodigo.TryGetValue(
                clienteKey,
                out var cliente);

            return cliente;
        }

        [HttpGet]
        public async Task<IActionResult> ResumenCobrosAgente(ResumenCobrosAgenteFiltroVm filtro)
        {
            PrepararFiltro(filtro);

            var vm = new ResumenCobrosAgentePageVm
            {
                Filtro = filtro,
                GruposAgente = await ObtenerGruposAgenteAsync(filtro.BuNombre),
                ClientesSinPorcentajeVendedor =
                    await ObtenerClientesSinPorcentajeVendedorAsync(filtro.BuNombre)
            };

            return View(vm);
        }

        private async Task<List<ClienteSinPorcentajeVendedorVm>>
            ObtenerClientesSinPorcentajeVendedorAsync(string? buNombre)
        {
            var bu = string.IsNullOrWhiteSpace(buNombre)
                ? "LANCO_CR"
                : buNombre.Trim().ToUpperInvariant();

            // XXORA_CUSTOMER_MASTER puede tener más de una fila por cliente
            // (por ejemplo, por sitio). Se consultan los registros sin porcentaje
            // y luego se muestra una sola fila por IDCLIENTE en la advertencia.
            var registros = await _context.XXORA_CUSTOMER_MASTERs
                 .AsNoTracking()
                 .Where(c =>
                     c.BU_NOMBRE != null &&
                     c.BU_NOMBRE.Trim().ToUpper() == bu &&
                     c.IDCLIENTE != null &&
                     c.IDCLIENTE.Trim() != "" &&
                     c.PARTY_SITE_NUMBER != null &&
                     c.PORCIENTO_VENDEDOR == null &&
                     _context.XXORA_COMISIONEs.Any(x =>
                         x.SITIO != null &&
                         x.SITIO.Trim() == c.PARTY_SITE_NUMBER.Trim()
                     ))
                 .Select(c => new
                 {
                     IdCliente = c.IDCLIENTE!,
                     NombreCliente = c.NOMBRE_CLIENTE,
                     Vendedor = c.IDVENDEDOR ?? c.VENDEDOR,
                     PartySiteNumber = c.PARTY_SITE_NUMBER
                 })
                 .ToListAsync();

            return registros
                .Select(x => new ClienteSinPorcentajeVendedorVm
                {
                    IdCliente = (x.IdCliente ?? "").Trim(),
                    NombreCliente = (x.NombreCliente ?? "").Trim(),
                    Vendedor = (x.Vendedor ?? "").Trim()
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.IdCliente))
                .GroupBy(
                    x => x.IdCliente,
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(
                    x => x.IdCliente,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<List<string>> ObtenerGruposAgenteAsync(string? buNombre)
        {
            var bu = string.IsNullOrWhiteSpace(buNombre)
                ? "LANCO_CR"
                : buNombre.Trim();

            var grupos = await _context.GEN_VENDEDORs
                .AsNoTracking()
                .Where(x => x.BU_NOMBRE == bu)
                .Select(x => x.CATEGORIA)
                .ToListAsync();

            return grupos
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct()
                .OrderBy(x => x)
                .ToList();
        }

        [HttpGet]
        public async Task<IActionResult> BuscarVendedores(string? filtro, string? buNombre)
        {
            var bu = string.IsNullOrWhiteSpace(buNombre) ? "LANCO_CR" : buNombre.Trim();
            var q = Normalizar(filtro);

            var vendedores = await _context.GEN_VENDEDORs
                .AsNoTracking()
                .Where(x => x.BU_NOMBRE == bu)
                .Where(x =>
                    string.IsNullOrEmpty(q)
                    || (x.IDVENDEDOR != null && x.IDVENDEDOR.ToUpper().Contains(q))
                    || (x.REGISTRY_ID != null && x.REGISTRY_ID.ToUpper().Contains(q))
                    || (x.NOMBRE_VENDEDOR != null && x.NOMBRE_VENDEDOR.ToUpper().Contains(q))
                )
                .OrderBy(x => x.IDVENDEDOR)
                .Take(50)
                .ToListAsync();

            var data = vendedores
                .Select(x => new
                {
                    codigo = (x.IDVENDEDOR ?? x.REGISTRY_ID ?? "").Trim(),
                    nombre = (x.NOMBRE_VENDEDOR ?? "").Trim(),
                    categoria = (x.CATEGORIA ?? "").Trim()
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.codigo))
                .GroupBy(x => x.codigo)
                .Select(g => g.First())
                .ToList();

            return Json(data);
        }

        [HttpGet]
        public async Task<IActionResult> BuscarClientes(
    string? filtro,
    string? buNombre)
        {
            var bu = string.IsNullOrWhiteSpace(buNombre)
                ? "LANCO_CR"
                : buNombre.Trim().ToUpperInvariant();

            var q = string.IsNullOrWhiteSpace(filtro)
                ? ""
                : filtro.Trim().ToUpperInvariant();

            var registros = await _context.XXORA_CUSTOMER_MASTERs
                .AsNoTracking()
                .Where(x =>
                    x.BU_NOMBRE != null &&
                    x.BU_NOMBRE.ToUpper() == bu &&
                    x.IDCLIENTE != null &&
                    x.IDCLIENTE.Trim() != "")
                .Where(x =>
                    string.IsNullOrEmpty(q) ||
                    x.IDCLIENTE!.ToUpper().Contains(q) ||
                    (
                        x.NOMBRE_CLIENTE != null &&
                        x.NOMBRE_CLIENTE.ToUpper().Contains(q)
                    ))
                .Select(x => new
                {
                    IdCliente = x.IDCLIENTE!,
                    NombreCliente = x.NOMBRE_CLIENTE
                })
                .OrderBy(x => x.IdCliente)
                .Take(500)
                .ToListAsync();

            var clientes = registros
                .Select(x => new
                {
                    codigo = x.IdCliente.Trim(),
                    nombre = (x.NombreCliente ?? "").Trim()
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.codigo))
                .GroupBy(
                    x => x.codigo,
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    codigo = g.Key,
                    nombre = g
                        .Select(x => x.nombre)
                        .FirstOrDefault(x =>
                            !string.IsNullOrWhiteSpace(x)) ?? ""
                })
                .OrderBy(x => x.codigo)
                .Take(50)
                .ToList();

            return Json(clientes);
        }


        // ---------------------------------------------------------------------
        // Actualización Navius / planilla.
        //
        // - NO borra información histórica de Navius.
        // - Solo agrega registros nuevos a:
        //      NUEVO.CXC_AGE_COBRO
        //      NUEVO.CXC_EMPLEADO_COBRO
        // - No utiliza CXC_DETAGE_COBRO ni CXC_CLIENTE_COBRO.
        // - AnoFiscal / Periodo = período del REPORTE.
        // - IdPeriodoPlanilla = período de PLANILLA digitado por el usuario.
        //
        // Si una fila ya existe por su llave en Navius, se conserva tal cual
        // y no vuelve a insertarse.
        // ---------------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActualizarNavius(
            ResumenCobrosAgenteFiltroVm filtro,
            ParametrosCalculoComisionVm parametros,
            int? idPeriodoPlanilla)
        {
            PrepararFiltro(filtro);
            PrepararParametrosCalculo(filtro, parametros);

            var bu = string.IsNullOrWhiteSpace(filtro.BuNombre)
                ? "LANCO_CR"
                : filtro.BuNombre.Trim().ToUpperInvariant();

            if (parametros.AnoFiscal < 2000 ||
                parametros.AnoFiscal > 2100)
            {
                TempData["NaviusError"] =
                    "El año fiscal del reporte no es válido.";

                return RedirigirActualizacionNavius(
                    filtro,
                    parametros,
                    idPeriodoPlanilla);
            }

            if (parametros.Periodo < 1 ||
                parametros.Periodo > 12)
            {
                TempData["NaviusError"] =
                    "El período del reporte debe estar entre 1 y 12.";

                return RedirigirActualizacionNavius(
                    filtro,
                    parametros,
                    idPeriodoPlanilla);
            }

            if (!idPeriodoPlanilla.HasValue ||
                idPeriodoPlanilla.Value <= 0 ||
                idPeriodoPlanilla.Value > 999999)
            {
                TempData["NaviusError"] =
                    "Digite un ID de período de planilla válido.";

                return RedirigirActualizacionNavius(
                    filtro,
                    parametros,
                    idPeriodoPlanilla);
            }

            // La moneda seleccionada en pantalla es únicamente la moneda del reporte.
            // Navius y la planilla siempre deben alimentarse con montos calculados en CRC.
            // Se crea un filtro independiente para no alterar la selección del usuario
            // cuando se redirija nuevamente a la pantalla.
            var filtroCalculoNavius = new ResumenCobrosAgenteFiltroVm
            {
                BuNombre = filtro.BuNombre,
                FechaDesde = filtro.FechaDesde,
                FechaHasta = filtro.FechaHasta,
                TipoCambio = filtro.TipoCambio,
                VendedorDesde = filtro.VendedorDesde,
                VendedorHasta = filtro.VendedorHasta,
                ClienteDesde = filtro.ClienteDesde,
                ClienteHasta = filtro.ClienteHasta,
                GrupoAgente = filtro.GrupoAgente,
                Moneda = "CRC",
                ChequeDevuelto = filtro.ChequeDevuelto
            };

            await ActualizacionNaviusLock.WaitAsync();

            try
            {
                var anoFiscal = checked((short)parametros.AnoFiscal);
                var periodoReporte = checked((short)parametros.Periodo);

                /*
                 * IMPORTANTE:
                 * Antes de leer CXC_AGE_COBRO y CXC_EMPLEADO_COBRO se fuerza
                 * nuevamente el cálculo completo en CRC.
                 *
                 * Esto evita que una generación previa del reporte en USD deje
                 * CXC_AGE_COBRO con importes en dólares y luego esos mismos montos
                 * sean enviados a Navius/planilla como si fueran colones.
                 *
                 * Se usa el mismo lock del cálculo de reportes para impedir que
                 * otro PDF/Excel recalcule las tablas al mismo tiempo.
                 */
                await CalculoComisionesLock.WaitAsync();

                try
                {
                    await EjecutarCalculoAgentesAsync(
                        filtroCalculoNavius,
                        parametros);

                    await EjecutarCalculoImpulsadoresAsync(
                        filtroCalculoNavius,
                        parametros);

                    // Las tablas quedaron físicamente preparadas en CRC.
                    // La caché debe reflejar ese estado. Si el usuario vuelve
                    // a pedir USD después de la actualización, la firma será
                    // diferente y el reporte se recalculará correctamente en USD.
                    _cache.Set(
                        UltimaFirmaCalculoCacheKey,
                        ConstruirFirmaCalculo(
                            filtroCalculoNavius,
                            parametros));
                }
                finally
                {
                    CalculoComisionesLock.Release();
                }

                /*
                 * Únicamente después del recálculo en CRC se leen estas dos
                 * tablas para actualizar PLAPAGOPLANILLA.
                 */
                var agentes = await _context.CXC_AGE_COBROs
                    .AsNoTracking()
                    .Where(x =>
                        x.COD_CIA.Trim() == bu &&
                        x.ANO_FISCAL == anoFiscal &&
                        x.PER_PROCESO == periodoReporte)
                    .ToListAsync();

                var empleados = await _context.CXC_EMPLEADO_COBROs
                    .AsNoTracking()
                    .Where(x =>
                        x.COD_CIA.Trim() == bu &&
                        x.ANO_FISCAL == anoFiscal &&
                        x.PER_PROCESO == periodoReporte)
                    .ToListAsync();

                if (agentes.Count == 0)
                {
                    TempData["NaviusError"] =
                        $"No existen registros en BG_INTUSER.CXC_AGE_COBRO " +
                        $"para {bu}, año {parametros.AnoFiscal}, " +
                        $"período {parametros.Periodo}. No se modificó Navius.";

                    return RedirigirActualizacionNavius(
                        filtro,
                        parametros,
                        idPeriodoPlanilla);
                }

                var connection =
                    _lancoContext.Database.GetDbConnection();

                var cerrarConexion =
                    connection.State != ConnectionState.Open;

                if (cerrarConexion)
                    await connection.OpenAsync();

                await using var transaction =
                    await _lancoContext.Database.BeginTransactionAsync();

                try
                {
                    var dbTransaction =
                        transaction.GetDbTransaction();

                    /*
                     * IMPORTANTE:
                     * No se ejecuta ningún DELETE.
                     *
                     * Los MERGE de abajo únicamente hacen INSERT cuando la
                     * llave todavía no existe. La información histórica de
                     * Navius queda intacta.
                     */
                    var agentesInsertados =
                        await InsertarAgentesNaviusAsync(
                            connection,
                            dbTransaction,
                            agentes);

                    var empleadosInsertados =
                        await InsertarEmpleadosNaviusAsync(
                            connection,
                            dbTransaction,
                            empleados);

                    /*
                     * El paquete mantiene ACTUALIZAR_PLANILLA recibiendo
                     * solamente el ID de planilla.
                     *
                     * CONFIGURAR_PERIODO_REPORTE guarda para esta misma sesión
                     * Oracle el año/período del reporte que se debe usar.
                     * Esto es necesario porque ahora las tablas CXC conservan
                     * información de períodos anteriores.
                     */
                    await EjecutarPaquetePlanillaAsync(
                        connection,
                        dbTransaction,
                        idPeriodoPlanilla.Value,
                        parametros.AnoFiscal,
                        parametros.Periodo);

                    await transaction.CommitAsync();

                    TempData["NaviusOk"] =
                        $"Navius y la planilla {idPeriodoPlanilla.Value} " +
                        $"se actualizaron correctamente. " +
                        $"CXC_AGE_COBRO nuevos: {agentesInsertados:N0} " +
                        $"de {agentes.Count:N0}; " +
                        $"CXC_EMPLEADO_COBRO nuevos: " +
                        $"{empleadosInsertados:N0} de {empleados.Count:N0}. " +
                        $"El cálculo se ejecutó en CRC antes de cargar Navius. " +
                        $"Los registros que ya existían se conservaron.";
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
                finally
                {
                    if (cerrarConexion &&
                        connection.State == ConnectionState.Open)
                    {
                        await connection.CloseAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["NaviusError"] =
                    "No se pudo actualizar Navius ni la planilla. " +
                    ObtenerMensajeExcepcion(ex);
            }
            finally
            {
                ActualizacionNaviusLock.Release();
            }

            return RedirigirActualizacionNavius(
                filtro,
                parametros,
                idPeriodoPlanilla);
        }

        private IActionResult RedirigirActualizacionNavius(
            ResumenCobrosAgenteFiltroVm filtro,
            ParametrosCalculoComisionVm parametros,
            int? idPeriodoPlanilla)
        {
            return RedirectToAction(
                nameof(ResumenCobrosAgente),
                new
                {
                    BuNombre = filtro.BuNombre,
                    AnoFiscal = parametros.AnoFiscal,
                    Periodo = parametros.Periodo,
                    Moneda = filtro.Moneda,
                    TipoCambio = filtro.TipoCambio,
                    GrupoAgente = filtro.GrupoAgente,
                    ClienteDesde = filtro.ClienteDesde,
                    ClienteHasta = filtro.ClienteHasta,
                    VendedorDesde = filtro.VendedorDesde,
                    VendedorHasta = filtro.VendedorHasta,
                    TipoChkDev1 = parametros.TipoChkDev1,
                    TipoChkDev2 = parametros.TipoChkDev2,
                    TipoDescuento = parametros.TipoDescuento,
                    PorcentajeImpuesto =
                        parametros.PorcentajeImpuesto,
                    AplicarImpuesto =
                        parametros.AplicarImpuesto,
                    IdPeriodoPlanilla = idPeriodoPlanilla
                });
        }

        private static string ObtenerMensajeExcepcion(
            Exception ex)
        {
            var actual = ex;

            while (actual.InnerException != null)
                actual = actual.InnerException;

            return actual.Message;
        }

        private static async Task EjecutarPaquetePlanillaAsync(
            DbConnection connection,
            DbTransaction transaction,
            int idPeriodoPlanilla,
            int anoFiscal,
            int periodoReporte)
        {
            const string bloque = @"
                BEGIN
                    NUEVO.PKG_COMISIONES_PLANILLA.CONFIGURAR_PERIODO_REPORTE(
                        P_ANO_FISCAL  => :P_ANO_FISCAL,
                        P_PER_PROCESO => :P_PER_PROCESO
                    );

                    NUEVO.PKG_COMISIONES_PLANILLA.ACTUALIZAR_PLANILLA(
                        P_PERIODO => :P_PERIODO
                    );
                END;";

            await using var command =
                CrearComandoNavius(
                    connection,
                    transaction,
                    bloque);

            CrearParametroNavius(
                command,
                "P_ANO_FISCAL",
                DbType.Int32,
                anoFiscal);

            CrearParametroNavius(
                command,
                "P_PER_PROCESO",
                DbType.Int32,
                periodoReporte);

            CrearParametroNavius(
                command,
                "P_PERIODO",
                DbType.Int32,
                idPeriodoPlanilla);

            command.CommandTimeout = 600;

            await command.ExecuteNonQueryAsync();
        }

        private static async Task<int>
            InsertarAgentesNaviusAsync(
                DbConnection connection,
                DbTransaction transaction,
                IReadOnlyCollection<CXC_AGE_COBRO> filas)
        {
            /*
             * Llave de NUEVO.CXC_AGE_COBRO:
             * COD_CIA, SUCURSAL, COD_AGENTE,
             * ANO_FISCAL, PER_PROCESO, COD_COMISION.
             *
             * Si ya existe, NO se actualiza ni se borra.
             */
            const string sql = @"
                MERGE INTO NUEVO.CXC_AGE_COBRO D
                USING
                (
                    SELECT
                        '001' AS COD_CIA,
                        '001' AS SUCURSAL,
                        :P_COD_AGENTE AS COD_AGENTE,
                        :P_ANO_FISCAL AS ANO_FISCAL,
                        :P_PER_PROCESO AS PER_PROCESO,
                        :P_COD_COMISION AS COD_COMISION,
                        :P_MON_COBRADO AS MON_COBRADO,
                        :P_MON_COMISION AS MON_COMISION,
                        :P_LOCAL1 AS LOCAL1,
                        :P_REPLICA1 AS REPLICA1,
                        :P_COBROBRUTO AS COBROBRUTO
                    FROM DUAL
                ) S
                ON
                (
                    D.COD_CIA = S.COD_CIA
                    AND D.SUCURSAL = S.SUCURSAL
                    AND TRIM(D.COD_AGENTE) =
                        TRIM(S.COD_AGENTE)
                    AND D.ANO_FISCAL = S.ANO_FISCAL
                    AND D.PER_PROCESO = S.PER_PROCESO
                    AND TRIM(D.COD_COMISION) =
                        TRIM(S.COD_COMISION)
                )
                WHEN NOT MATCHED THEN
                    INSERT
                    (
                        COD_CIA,
                        SUCURSAL,
                        COD_AGENTE,
                        ANO_FISCAL,
                        PER_PROCESO,
                        COD_COMISION,
                        MON_COBRADO,
                        MON_COMISION,
                        POSFECOBMES,
                        POSFENOCOB,
                        LOCAL1,
                        REPLICA1,
                        COBROBRUTO
                    )
                    VALUES
                    (
                        S.COD_CIA,
                        S.SUCURSAL,
                        S.COD_AGENTE,
                        S.ANO_FISCAL,
                        S.PER_PROCESO,
                        S.COD_COMISION,
                        S.MON_COBRADO,
                        S.MON_COMISION,
                        NULL,
                        NULL,
                        S.LOCAL1,
                        S.REPLICA1,
                        S.COBROBRUTO
                    )";

            await using var command =
                CrearComandoNavius(
                    connection,
                    transaction,
                    sql);

            var pCodAgente = CrearParametroNavius(
                command,
                "P_COD_AGENTE",
                DbType.String);

            var pAnoFiscal = CrearParametroNavius(
                command,
                "P_ANO_FISCAL",
                DbType.Int32);

            var pPerProceso = CrearParametroNavius(
                command,
                "P_PER_PROCESO",
                DbType.Int32);

            var pCodComision = CrearParametroNavius(
                command,
                "P_COD_COMISION",
                DbType.String);

            var pMonCobrado = CrearParametroNavius(
                command,
                "P_MON_COBRADO",
                DbType.Decimal);

            var pMonComision = CrearParametroNavius(
                command,
                "P_MON_COMISION",
                DbType.Decimal);

            var pLocal1 = CrearParametroNavius(
                command,
                "P_LOCAL1",
                DbType.String);

            var pReplica1 = CrearParametroNavius(
                command,
                "P_REPLICA1",
                DbType.String);

            var pCobroBruto = CrearParametroNavius(
                command,
                "P_COBROBRUTO",
                DbType.Decimal);

            var insertados = 0;

            foreach (var fila in filas)
            {
                pCodAgente.Value =
                    TextoRequerido(fila.COD_AGENTE);

                pAnoFiscal.Value =
                    Convert.ToInt32(fila.ANO_FISCAL);

                pPerProceso.Value =
                    Convert.ToInt32(fila.PER_PROCESO);

                pCodComision.Value =
                    TextoRequerido(fila.COD_COMISION);

                pMonCobrado.Value =
                    fila.MON_COBRADO;

                pMonComision.Value =
                    fila.MON_COMISION;

                pLocal1.Value =
                    TextoRequerido(fila.LOCAL1);

                pReplica1.Value =
                    TextoRequerido(fila.REPLICA1);

                pCobroBruto.Value =
                    fila.COBROBRUTO;

                insertados +=
                    await command.ExecuteNonQueryAsync();
            }

            return insertados;
        }

        private static async Task<int>
            InsertarEmpleadosNaviusAsync(
                DbConnection connection,
                DbTransaction transaction,
                IReadOnlyCollection<CXC_EMPLEADO_COBRO> filas)
        {
            /*
             * La llave configurada en Navius para CXC_EMPLEADO_COBRO es:
             * COD_CIA, COD_CLIENTE, EMPLEADO,
             * ANO_FISCAL, PER_PROCESO.
             *
             * Si la llave ya existe, la fila se conserva sin modificar.
             */
            const string sql = @"
                MERGE INTO NUEVO.CXC_EMPLEADO_COBRO D
                USING
                (
                    SELECT
                        '001' AS COD_CIA,
                        :P_COD_AGENTE AS COD_AGENTE,
                        :P_COD_CLIENTE AS COD_CLIENTE,
                        :P_EMPLEADO AS EMPLEADO,
                        :P_ANO_FISCAL AS ANO_FISCAL,
                        :P_PER_PROCESO AS PER_PROCESO,
                        :P_PORCENTAJE AS PORCENTAJE,
                        :P_COBROBRUTO AS COBROBRUTO,
                        :P_MON_COBRADO AS MON_COBRADO,
                        :P_MON_COMISION AS MON_COMISION
                    FROM DUAL
                ) S
                ON
                (
                    D.COD_CIA = S.COD_CIA
                    AND TRIM(D.COD_CLIENTE) =
                        TRIM(S.COD_CLIENTE)
                    AND TRIM(D.EMPLEADO) =
                        TRIM(S.EMPLEADO)
                    AND D.ANO_FISCAL = S.ANO_FISCAL
                    AND D.PER_PROCESO = S.PER_PROCESO
                )
                WHEN NOT MATCHED THEN
                    INSERT
                    (
                        COD_CIA,
                        COD_AGENTE,
                        COD_CLIENTE,
                        EMPLEADO,
                        ANO_FISCAL,
                        PER_PROCESO,
                        PORCENTAJE,
                        COBROBRUTO,
                        MON_COBRADO,
                        MON_COMISION
                    )
                    VALUES
                    (
                        S.COD_CIA,
                        S.COD_AGENTE,
                        S.COD_CLIENTE,
                        S.EMPLEADO,
                        S.ANO_FISCAL,
                        S.PER_PROCESO,
                        S.PORCENTAJE,
                        S.COBROBRUTO,
                        S.MON_COBRADO,
                        S.MON_COMISION
                    )";

            await using var command =
                CrearComandoNavius(
                    connection,
                    transaction,
                    sql);

            var pCodAgente = CrearParametroNavius(
                command,
                "P_COD_AGENTE",
                DbType.String);

            var pCodCliente = CrearParametroNavius(
                command,
                "P_COD_CLIENTE",
                DbType.String);

            var pEmpleado = CrearParametroNavius(
                command,
                "P_EMPLEADO",
                DbType.String);

            var pAnoFiscal = CrearParametroNavius(
                command,
                "P_ANO_FISCAL",
                DbType.Int32);

            var pPerProceso = CrearParametroNavius(
                command,
                "P_PER_PROCESO",
                DbType.Int32);

            var pPorcentaje = CrearParametroNavius(
                command,
                "P_PORCENTAJE",
                DbType.Decimal);

            var pCobroBruto = CrearParametroNavius(
                command,
                "P_COBROBRUTO",
                DbType.Decimal);

            var pMonCobrado = CrearParametroNavius(
                command,
                "P_MON_COBRADO",
                DbType.Decimal);

            var pMonComision = CrearParametroNavius(
                command,
                "P_MON_COMISION",
                DbType.Decimal);

            var insertados = 0;

            foreach (var fila in filas)
            {
                pCodAgente.Value =
                    TextoRequerido(fila.COD_AGENTE);

                pCodCliente.Value =
                    TextoRequerido(fila.COD_CLIENTE);

                pEmpleado.Value =
                    TextoRequerido(fila.EMPLEADO);

                pAnoFiscal.Value =
                    Convert.ToInt32(fila.ANO_FISCAL);

                pPerProceso.Value =
                    Convert.ToInt32(fila.PER_PROCESO);

                pPorcentaje.Value =
                    fila.PORCENTAJE;

                pCobroBruto.Value =
                    fila.COBROBRUTO;

                pMonCobrado.Value =
                    fila.MON_COBRADO;

                pMonComision.Value =
                    fila.MON_COMISION;

                insertados +=
                    await command.ExecuteNonQueryAsync();
            }

            return insertados;
        }

        private static DbCommand CrearComandoNavius(
            DbConnection connection,
            DbTransaction transaction,
            string sql)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandType = CommandType.Text;
            command.CommandText = sql;
            command.CommandTimeout = 600;

            var bindByName = command
                .GetType()
                .GetProperty("BindByName");

            if (bindByName?.CanWrite == true)
                bindByName.SetValue(command, true);

            return command;
        }

        private static DbParameter CrearParametroNavius(
            DbCommand command,
            string nombre,
            DbType tipo,
            object? valor = null)
        {
            var parametro =
                command.CreateParameter();

            parametro.ParameterName = nombre;
            parametro.DbType = tipo;
            parametro.Value =
                valor ?? DBNull.Value;

            command.Parameters.Add(parametro);

            return parametro;
        }

        private static string TextoRequerido(
            string? valor)
        {
            return (valor ?? "").Trim();
        }

        [HttpGet]
        public async Task<IActionResult> ResumenCobrosAgenteExcel(
            ResumenCobrosAgenteFiltroVm filtro,
            ParametrosCalculoComisionVm parametros)
        {
            PrepararFiltro(filtro);
            PrepararParametrosCalculo(filtro, parametros);

            var agentes = await EjecutarCalculoYConsultarAsync(
                filtro,
                parametros,
                () => ObtenerResumenCobrosAgenteAsync(
                    filtro,
                    parametros));

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Resumen");

            var row = 1;

            ws.Cell(row, 1).Value = "Cuentas Por Cobrar";
            ws.Cell(row, 3).Value =
                "LANCO & HARRIS MFG. CORP. SRL";
            ws.Cell(row, 5).Value = "Pagina";
            row++;

            ws.Cell(row, 1).Value = @"cobros\cxccobag";
            ws.Cell(row, 3).Value = "Cobros Por Agente";
            ws.Cell(row, 5).Value =
                DateTime.Now.ToString("dd/MM/yyyy hh:mm tt");
            row++;

            ws.Cell(row, 3).Value =
                $"Desde: {filtro.FechaDesde:dd/MM/yyyy}, " +
                $"Hasta: {filtro.FechaHasta:dd/MM/yyyy}.";
            ws.Cell(row, 5).Value = "NUEVO";
            row += 2;

            /*
             * La columna Descuento se elimina del reporte.
             * El valor de Monto ya contiene:
             *     MONTO - DESCUENTO
             */
            ws.Cell(row, 1).Value = "Código";
            ws.Cell(row, 2).Value = "Descripción";
            ws.Cell(row, 3).Value = "Monto";
            ws.Cell(row, 4).Value =
                "Monto Factura Sin Impuesto";
            ws.Cell(row, 5).Value = "Monto Comisión";

            ws.Range(row, 1, row, 5).Style.Font.Bold = true;
            ws.Range(row, 1, row, 5)
                .Style.Border.BottomBorder =
                    XLBorderStyleValues.Thin;

            row++;

            if (!agentes.Any())
            {
                ws.Cell(row, 1).Value =
                    "No hay datos para los filtros seleccionados.";

                ws.Range(row, 1, row, 5).Merge();

                ws.Range(row, 1, row, 5)
                    .Style.Alignment.Horizontal =
                        XLAlignmentHorizontalValues.Center;
            }
            else
            {
                var grupos = agentes
                    .GroupBy(x => new
                    {
                        x.GrupoCodigo,
                        x.GrupoDescripcion
                    })
                    .OrderBy(x => x.Key.GrupoCodigo)
                    .ToList();

                foreach (var grupo in grupos)
                {
                    row++;

                    ws.Cell(row, 1).Value = "Grupo";
                    ws.Cell(row, 2).Value =
                        grupo.Key.GrupoCodigo;
                    ws.Cell(row, 3).Value =
                        grupo.Key.GrupoDescripcion;

                    ws.Range(row, 1, row, 5)
                        .Style.Font.Bold = true;

                    ws.Range(row, 1, row, 5)
                        .Style.Fill.BackgroundColor =
                            XLColor.FromHtml("#E9ECEF");

                    row++;

                    foreach (var agente in grupo
                        .OrderBy(x => x.CodVendedor))
                    {
                        ws.Cell(row, 1).Value = "Agente";
                        ws.Cell(row, 2).Value =
                            $"{agente.CodVendedor} - " +
                            agente.NombreVendedor;

                        ws.Range(row, 2, row, 5).Merge();
                        ws.Range(row, 1, row, 5)
                            .Style.Font.Bold = true;

                        ws.Range(row, 1, row, 5)
                            .Style.Fill.BackgroundColor =
                                XLColor.FromHtml("#F8F9FA");

                        row++;

                        /*
                         * Desglose vertical por código de comisión.
                         * ObtenerResumenCobrosAgenteAsync ya excluye códigos
                         * cuyo monto sin impuesto no sea mayor que cero.
                         */
                        foreach (var comision in agente.Comisiones
                            .OrderBy(x => x.CodComision))
                        {
                            ws.Cell(row, 1).Value =
                                comision.CodComision;

                            ws.Cell(row, 2).Value =
                                comision.DesComision;

                            ws.Cell(row, 3).Value =
                                comision.Monto;

                            ws.Cell(row, 4).Value =
                                comision.MontoFacturaSinImpuesto;

                            ws.Cell(row, 5).Value =
                                comision.MontoComision;

                            row++;
                        }

                        ws.Cell(row, 2).Value = "TOTAL AGENTE";
                        ws.Cell(row, 3).Value = agente.Monto;
                        ws.Cell(row, 4).Value =
                            agente.MontoFacturaSinImpuesto;
                        ws.Cell(row, 5).Value =
                            agente.MontoComision;

                        ws.Range(row, 2, row, 5)
                            .Style.Font.Bold = true;

                        ws.Range(row, 2, row, 5)
                            .Style.Border.TopBorder =
                                XLBorderStyleValues.Thin;

                        row += 2;
                    }

                    ws.Cell(row, 2).Value = "TOTAL GRUPO";
                    ws.Cell(row, 3).Value =
                        grupo.Sum(x => x.Monto);
                    ws.Cell(row, 4).Value =
                        grupo.Sum(
                            x => x.MontoFacturaSinImpuesto);
                    ws.Cell(row, 5).Value =
                        grupo.Sum(x => x.MontoComision);

                    ws.Range(row, 2, row, 5)
                        .Style.Font.Bold = true;

                    ws.Range(row, 2, row, 5)
                        .Style.Border.TopBorder =
                            XLBorderStyleValues.Medium;

                    row++;
                }

                row++;

                ws.Cell(row, 2).Value = "TOTAL GENERAL";
                ws.Cell(row, 3).Value =
                    agentes.Sum(x => x.Monto);
                ws.Cell(row, 4).Value =
                    agentes.Sum(
                        x => x.MontoFacturaSinImpuesto);
                ws.Cell(row, 5).Value =
                    agentes.Sum(x => x.MontoComision);

                ws.Range(row, 2, row, 5)
                    .Style.Font.Bold = true;

                ws.Range(row, 2, row, 5)
                    .Style.Border.TopBorder =
                        XLBorderStyleValues.Medium;
            }

            ws.SheetView.FreezeRows(5);

            ws.Column(1).Width = 14;
            ws.Column(2).Width = 40;
            ws.Column(3).Width = 20;
            ws.Column(4).Width = 27;
            ws.Column(5).Width = 20;

            ws.Columns(3, 5)
                .Style.NumberFormat.Format =
                    "#,##0.00";

            ws.PageSetup.PageOrientation =
                XLPageOrientation.Portrait;
            ws.PageSetup.FitToPages(1, 0);

            var stream = new MemoryStream();

            var fileName =
                $"Resumen_Cobros_Agente_" +
                $"{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

            wb.SaveAs(stream);
            stream.Position = 0;

            return File(
                stream,
                "application/vnd.openxmlformats-officedocument." +
                "spreadsheetml.sheet",
                fileName);
        }


        private static IContainer HeaderStyleResumen(IContainer container)
        {
            return container
                .PaddingBottom(3)
                .BorderBottom(0.5f)
                .PaddingHorizontal(2);
        }

        private static IContainer BodyStyleResumen(IContainer container)
        {
            return container
                .PaddingVertical(1.5f)
                .PaddingHorizontal(2);
        }

        private static string FormatoMonto(decimal monto)
        {
            if (monto == 0)
                return ",00";

            var culture = new CultureInfo("es-CR");
            return monto.ToString("N2", culture);
        }

        [HttpGet]
        public async Task<IActionResult> ResumenCobrosAgentePdf(
            ResumenCobrosAgenteFiltroVm filtro,
            ParametrosCalculoComisionVm parametros)
        {
            PrepararFiltro(filtro);
            PrepararParametrosCalculo(filtro, parametros);

            var agentes = await EjecutarCalculoYConsultarAsync(
                filtro,
                parametros,
                () => ObtenerResumenCobrosAgenteAsync(
                    filtro,
                    parametros));

            QuestPDF.Settings.License =
                QLicenseType.Community;

            var pdfBytes = QDocument.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(25);
                    page.Size(PageSizes.Letter);

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem()
                                .Text("Cuentas Por Cobrar")
                                .FontSize(9);

                            row.RelativeItem()
                                .AlignCenter()
                                .Text(
                                    "LANCO & HARRIS MFG. CORP. SRL")
                                .FontSize(9);

                            row.RelativeItem()
                                .AlignRight()
                                .Text(text =>
                                {
                                    text.Span("Pagina ");
                                    text.CurrentPageNumber();
                                    text.Span("/");
                                    text.TotalPages();
                                });
                        });

                        col.Item().Row(row =>
                        {
                            row.RelativeItem()
                                .Text(@"cobros\cxccobag")
                                .FontSize(8);

                            row.RelativeItem()
                                .AlignCenter()
                                .Text("Cobros Por Agente")
                                .FontSize(10)
                                .Bold();

                            row.RelativeItem()
                                .AlignRight()
                                .Text(
                                    DateTime.Now.ToString(
                                        "dd/MM/yyyy hh:mm tt"))
                                .FontSize(8);
                        });

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text("");

                            row.RelativeItem()
                                .AlignCenter()
                                .Text(
                                    $"Desde: " +
                                    $"{filtro.FechaDesde:dd/MM/yyyy}, " +
                                    $"Hasta: " +
                                    $"{filtro.FechaHasta:dd/MM/yyyy}.")
                                .FontSize(8);

                            row.RelativeItem()
                                .AlignRight()
                                .Text("NUEVO")
                                .FontSize(8);
                        });

                        col.Item()
                            .PaddingTop(6)
                            .LineHorizontal(0.5f);
                    });

                    page.Content()
                        .PaddingTop(6)
                        .Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(42);
                                columns.RelativeColumn(2.9f);
                                columns.RelativeColumn(1.35f);
                                columns.RelativeColumn(1.65f);
                                columns.RelativeColumn(1.25f);
                            });

                            table.Header(header =>
                            {
                                header.Cell()
                                    .Element(HeaderStyleResumen)
                                    .Text("Código")
                                    .FontSize(7)
                                    .Bold();

                                header.Cell()
                                    .Element(HeaderStyleResumen)
                                    .Text("Descripción")
                                    .FontSize(7)
                                    .Bold();

                                header.Cell()
                                    .Element(HeaderStyleResumen)
                                    .AlignRight()
                                    .Text("Monto")
                                    .FontSize(7)
                                    .Bold();

                                header.Cell()
                                    .Element(HeaderStyleResumen)
                                    .AlignRight()
                                    .Text("Monto Factura\nSin Impuesto")
                                    .FontSize(7)
                                    .Bold();

                                header.Cell()
                                    .Element(HeaderStyleResumen)
                                    .AlignRight()
                                    .Text("Monto Comisión")
                                    .FontSize(7)
                                    .Bold();
                            });

                            if (!agentes.Any())
                            {
                                table.Cell()
                                    .ColumnSpan(5)
                                    .PaddingTop(12)
                                    .AlignCenter()
                                    .Text(
                                        "No hay datos para los " +
                                        "filtros seleccionados.")
                                    .FontSize(9);

                                return;
                            }

                            var grupos = agentes
                                .GroupBy(x => new
                                {
                                    x.GrupoCodigo,
                                    x.GrupoDescripcion
                                })
                                .OrderBy(
                                    x => x.Key.GrupoCodigo)
                                .ToList();

                            foreach (var grupo in grupos)
                            {
                                table.Cell()
                                    .ColumnSpan(5)
                                    .PaddingTop(8)
                                    .PaddingBottom(2)
                                    .Text(text =>
                                    {
                                        text.Span("GRUPO ")
                                            .Bold()
                                            .FontSize(8);

                                        text.Span(
                                                grupo.Key.GrupoCodigo)
                                            .Bold()
                                            .FontSize(8);

                                        text.Span("  ")
                                            .FontSize(8);

                                        text.Span(
                                                grupo.Key
                                                    .GrupoDescripcion)
                                            .Bold()
                                            .FontSize(8);
                                    });

                                foreach (var agente in grupo
                                    .OrderBy(
                                        x => x.CodVendedor))
                                {
                                    table.Cell()
                                        .ColumnSpan(5)
                                        .PaddingTop(5)
                                        .PaddingBottom(2)
                                        .Text(text =>
                                        {
                                            text.Span("AGENTE ")
                                                .Bold()
                                                .FontSize(8);

                                            text.Span(
                                                    agente.CodVendedor)
                                                .Bold()
                                                .FontSize(8);

                                            text.Span("  ")
                                                .FontSize(8);

                                            text.Span(
                                                    agente
                                                        .NombreVendedor)
                                                .Bold()
                                                .FontSize(8);
                                        });

                                    foreach (
                                        var comision in
                                        agente.Comisiones
                                            .OrderBy(
                                                x => x.CodComision))
                                    {
                                        table.Cell()
                                            .Element(
                                                BodyStyleResumen)
                                            .Text(
                                                comision.CodComision)
                                            .FontSize(7);

                                        table.Cell()
                                            .Element(
                                                BodyStyleResumen)
                                            .Text(
                                                comision.DesComision)
                                            .FontSize(7);

                                        table.Cell()
                                            .Element(
                                                BodyStyleResumen)
                                            .AlignRight()
                                            .Text(
                                                FormatoMonto(
                                                    comision.Monto))
                                            .FontSize(7);

                                        table.Cell()
                                            .Element(
                                                BodyStyleResumen)
                                            .AlignRight()
                                            .Text(
                                                FormatoMonto(
                                                    comision
                                                        .MontoFacturaSinImpuesto))
                                            .FontSize(7);

                                        table.Cell()
                                            .Element(
                                                BodyStyleResumen)
                                            .AlignRight()
                                            .Text(
                                                FormatoMonto(
                                                    comision
                                                        .MontoComision))
                                            .FontSize(7);
                                    }

                                    table.Cell()
                                        .Element(BodyStyleResumen)
                                        .Text("");

                                    table.Cell()
                                        .Element(BodyStyleResumen)
                                        .AlignRight()
                                        .Text("TOTAL AGENTE")
                                        .FontSize(7)
                                        .Bold();

                                    table.Cell()
                                        .Element(BodyStyleResumen)
                                        .AlignRight()
                                        .Text(
                                            FormatoMonto(
                                                agente.Monto))
                                        .FontSize(7)
                                        .Bold();

                                    table.Cell()
                                        .Element(BodyStyleResumen)
                                        .AlignRight()
                                        .Text(
                                            FormatoMonto(
                                                agente
                                                    .MontoFacturaSinImpuesto))
                                        .FontSize(7)
                                        .Bold();

                                    table.Cell()
                                        .Element(BodyStyleResumen)
                                        .AlignRight()
                                        .Text(
                                            FormatoMonto(
                                                agente
                                                    .MontoComision))
                                        .FontSize(7)
                                        .Bold();
                                }

                                table.Cell()
                                    .Element(BodyStyleResumen)
                                    .Text("");

                                table.Cell()
                                    .Element(BodyStyleResumen)
                                    .AlignRight()
                                    .Text("TOTAL GRUPO")
                                    .FontSize(7)
                                    .Bold();

                                table.Cell()
                                    .Element(BodyStyleResumen)
                                    .AlignRight()
                                    .Text(
                                        FormatoMonto(
                                            grupo.Sum(
                                                x => x.Monto)))
                                    .FontSize(7)
                                    .Bold();

                                table.Cell()
                                    .Element(BodyStyleResumen)
                                    .AlignRight()
                                    .Text(
                                        FormatoMonto(
                                            grupo.Sum(
                                                x =>
                                                    x.MontoFacturaSinImpuesto)))
                                    .FontSize(7)
                                    .Bold();

                                table.Cell()
                                    .Element(BodyStyleResumen)
                                    .AlignRight()
                                    .Text(
                                        FormatoMonto(
                                            grupo.Sum(
                                                x =>
                                                    x.MontoComision)))
                                    .FontSize(7)
                                    .Bold();
                            }

                            table.Cell()
                                .ColumnSpan(5)
                                .PaddingTop(8)
                                .LineHorizontal(0.5f);

                            table.Cell()
                                .Element(BodyStyleResumen)
                                .Text("");

                            table.Cell()
                                .Element(BodyStyleResumen)
                                .AlignRight()
                                .Text("TOTAL GENERAL")
                                .FontSize(7)
                                .Bold();

                            table.Cell()
                                .Element(BodyStyleResumen)
                                .AlignRight()
                                .Text(
                                    FormatoMonto(
                                        agentes.Sum(
                                            x => x.Monto)))
                                .FontSize(7)
                                .Bold();

                            table.Cell()
                                .Element(BodyStyleResumen)
                                .AlignRight()
                                .Text(
                                    FormatoMonto(
                                        agentes.Sum(
                                            x =>
                                                x.MontoFacturaSinImpuesto)))
                                .FontSize(7)
                                .Bold();

                            table.Cell()
                                .Element(BodyStyleResumen)
                                .AlignRight()
                                .Text(
                                    FormatoMonto(
                                        agentes.Sum(
                                            x => x.MontoComision)))
                                .FontSize(7)
                                .Bold();
                        });
                });
            }).GeneratePdf();

            var fileName =
                $"Resumen_Cobros_Agente_" +
                $"{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

            return File(
                pdfBytes,
                "application/pdf",
                fileName);
        }


        private async Task<List<ResumenAgenteComisionesFila>>
            ObtenerResumenCobrosAgenteAsync(
                ResumenCobrosAgenteFiltroVm filtro,
                ParametrosCalculoComisionVm parametros)
        {
            var bu =
                (filtro.BuNombre ?? "LANCO_CR").Trim();

            /*
             * Se utiliza CXC_DETAGE_COBRO para conservar los filtros
             * por cliente. Luego se agrupa por:
             *
             * grupo + vendedor + código de comisión.
             *
             * SUM(MON_COBRADO) debe coincidir con el resumen de
             * CXC_AGE_COBRO cuando no se aplican filtros de cliente.
             */
            var detalles =
                await _context.CXC_DETAGE_COBROs
                    .AsNoTracking()
                    .Where(x =>
                        x.COD_CIA == bu &&
                        x.SUCURSAL == SucursalFija &&
                        x.ANO_FISCAL ==
                            parametros.AnoFiscal &&
                        x.PER_PROCESO ==
                            parametros.Periodo)
                    .ToListAsync();

            var catalogos =
                await ObtenerCatalogosAsync(bu);

            var descripcionesComision =
                await ObtenerDescripcionesComisionAsync(bu);

            detalles = AplicarRangosEnMemoria(
                detalles,
                filtro);

            var grupoFiltro =
                string.IsNullOrWhiteSpace(
                    filtro.GrupoAgente)
                    ? null
                    : Normalizar(filtro.GrupoAgente);

            var acumulados = new Dictionary<
                (
                    string GrupoCodigo,
                    string GrupoDescripcion,
                    string CodVendedor,
                    string NombreVendedor,
                    string CodComision
                ),
                ResumenComisionAgenteFila>();

            foreach (var detalle in detalles)
            {
                var codVendedor =
                    (detalle.COD_AGENTE ?? "").Trim();

                var vendedorKey =
                    Normalizar(codVendedor);

                if (!catalogos.Vendedores.TryGetValue(
                        vendedorKey,
                        out var vendedor))
                {
                    continue;
                }

                var grupoCodigo =
                    (vendedor.CATEGORIA ?? "").Trim();

                if (grupoFiltro != null &&
                    Normalizar(grupoCodigo) != grupoFiltro)
                {
                    continue;
                }

                var codComision =
                    (detalle.COD_COMISION ?? "").Trim();

                if (string.IsNullOrWhiteSpace(codComision))
                    continue;

                var codigoNormalizado =
                    Normalizar(codComision);

                var monto = ConvertirMonto(
                    detalle.MONTO,
                    detalle.COD_MONEDA,
                    filtro.TipoCambio);

                var descuento = ConvertirMonto(
                    detalle.DESCUENTO,
                    detalle.COD_MONEDA,
                    filtro.TipoCambio);

                var montoSinImpuesto = ConvertirMonto(
                    detalle.MON_COBRADO,
                    detalle.COD_MONEDA,
                    filtro.TipoCambio);

                var montoComision = ConvertirMonto(
                    detalle.MON_COMISION,
                    detalle.COD_MONEDA,
                    filtro.TipoCambio);

                var nombreVendedor =
                    vendedor.NOMBRE_VENDEDOR ??
                    codVendedor;

                var grupoDescripcion =
                    ObtenerDescripcionGrupo(
                        grupoCodigo);

                var clave = (
                    GrupoCodigo: grupoCodigo,
                    GrupoDescripcion:
                        grupoDescripcion,
                    CodVendedor: codVendedor,
                    NombreVendedor:
                        nombreVendedor,
                    CodComision:
                        codigoNormalizado);

                if (!acumulados.TryGetValue(
                        clave,
                        out var acumulado))
                {
                    descripcionesComision.TryGetValue(
                        codigoNormalizado,
                        out var descripcion);

                    acumulado =
                        new ResumenComisionAgenteFila
                        {
                            CodComision =
                                codComision,

                            DesComision =
                                string.IsNullOrWhiteSpace(
                                    descripcion)
                                    ? $"Comisión {codComision}"
                                    : descripcion
                        };

                    acumulados.Add(
                        clave,
                        acumulado);
                }

                // El descuento ya no se muestra como columna separada.
                // Se descuenta directamente del monto bruto.
                acumulado.Monto += monto - descuento;
                acumulado.MontoFacturaSinImpuesto +=
                    montoSinImpuesto;
                acumulado.MontoComision +=
                    montoComision;
            }

            /*
             * CXC_AGE_COBRO contiene filas en cero para las combinaciones
             * agente/comisión que no tuvieron cobros. El reporte no debe
             * mostrar esas combinaciones.
             *
             * El criterio solicitado es que el monto cobrado sin impuesto
             * sea estrictamente mayor que cero.
             */
            var comisionesConMonto = acumulados
                .Where(x =>
                    x.Value.MontoFacturaSinImpuesto > 0m)
                .ToList();

            return comisionesConMonto
                .GroupBy(x => new
                {
                    x.Key.GrupoCodigo,
                    x.Key.GrupoDescripcion,
                    x.Key.CodVendedor,
                    x.Key.NombreVendedor
                })
                .Select(grupo => new
                    ResumenAgenteComisionesFila
                {
                    GrupoCodigo =
                            grupo.Key.GrupoCodigo,

                    GrupoDescripcion =
                            grupo.Key.GrupoDescripcion,

                    CodVendedor =
                            grupo.Key.CodVendedor,

                    NombreVendedor =
                            grupo.Key.NombreVendedor,

                    Comisiones = grupo
                            .Select(x => x.Value)
                            .OrderBy(
                                x => x.CodComision)
                            .ToList()
                })
                .OrderBy(x => x.GrupoCodigo)
                .ThenBy(x => x.CodVendedor)
                .ToList();
        }


        private static List<CXC_DETAGE_COBRO> AplicarRangosEnMemoria(
            List<CXC_DETAGE_COBRO> datos,
            ResumenCobrosAgenteFiltroVm filtro)
        {
            var vendedorDesde = string.IsNullOrWhiteSpace(filtro.VendedorDesde)
                ? null
                : Normalizar(filtro.VendedorDesde);

            var vendedorHasta = string.IsNullOrWhiteSpace(filtro.VendedorHasta)
                ? null
                : Normalizar(filtro.VendedorHasta);

            /*
             * Los filtros de pantalla contienen
             * XXORA_CUSTOMER_MASTER.IDCLIENTE.
             *
             * CXC_DETAGE_COBRO.COD_CLIENTE guarda ese mismo IDCLIENTE,
             * por lo que la comparación debe hacerse directamente y sin
             * convertirlo a REGISTRY_ID/PARTY_NUMBER.
             */
            var clienteDesde = string.IsNullOrWhiteSpace(filtro.ClienteDesde)
                ? null
                : Normalizar(filtro.ClienteDesde);

            var clienteHasta = string.IsNullOrWhiteSpace(filtro.ClienteHasta)
                ? null
                : Normalizar(filtro.ClienteHasta);

            var moneda = string.IsNullOrWhiteSpace(filtro.Moneda)
                ? null
                : Normalizar(filtro.Moneda);

            return datos
                .Where(x =>
                {
                    var vendedor = Normalizar(x.COD_AGENTE);
                    var idCliente = Normalizar(x.COD_CLIENTE);

                    if (vendedorDesde != null &&
                        string.Compare(
                            vendedor,
                            vendedorDesde,
                            StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return false;
                    }

                    if (vendedorHasta != null &&
                        string.Compare(
                            vendedor,
                            vendedorHasta,
                            StringComparison.OrdinalIgnoreCase) > 0)
                    {
                        return false;
                    }

                    if (clienteDesde != null &&
                        string.Compare(
                            idCliente,
                            clienteDesde,
                            StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return false;
                    }

                    if (clienteHasta != null &&
                        string.Compare(
                            idCliente,
                            clienteHasta,
                            StringComparison.OrdinalIgnoreCase) > 0)
                    {
                        return false;
                    }

                    if (moneda != null &&
                        Normalizar(x.COD_MONEDA) != moneda)
                    {
                        return false;
                    }

                    return true;
                })
                .ToList();
        }


        private string ObtenerNombreJefe(string? grupoDescripcion)
        {
            var grupo = (grupoDescripcion ?? "").Trim();

            if (string.IsNullOrWhiteSpace(grupo))
                return "";

            // GetSection().GetChildren() permite comparar sin depender de
            // mayúsculas/minúsculas del nombre escrito en appsettings.json.
            var coincidencia = _configuration
                .GetSection("NombreJefes")
                .GetChildren()
                .FirstOrDefault(x =>
                    string.Equals(
                        (x.Key ?? "").Trim(),
                        grupo,
                        StringComparison.OrdinalIgnoreCase));

            return (coincidencia?.Value ?? "").Trim();
        }

        private static string ObtenerDescripcionGrupo(string? grupo)
        {
            return Normalizar(grupo) switch
            {
                "C1" => "COMERCIAL 1",
                "C2" => "COMERCIAL 2",
                "EX" => "EXPORTACION",
                "I1" => "INDUSTRIA 1",
                "KA" => "KAM 1",
                "KM" => "KAM 2",
                "OF" => "OFICINA",
                "PE" => "PEGAMENTOS",
                _ => (grupo ?? "").Trim()
            };
        }

        // El nombre del jefe se utiliza EXCLUSIVAMENTE en el reporte
        // Cobros Diarios por Agente (PDF / Excel).
        private string ObtenerTituloGrupoCobrosDiarios(
            string? grupoCodigo,
            string? grupoDescripcion)
        {
            var codigo = (grupoCodigo ?? "").Trim();
            var descripcion = string.IsNullOrWhiteSpace(grupoDescripcion)
                ? ObtenerDescripcionGrupo(codigo)
                : grupoDescripcion.Trim();

            var nombreJefe = ObtenerNombreJefe(descripcion);

            var partes = new List<string>();

            if (!string.IsNullOrWhiteSpace(codigo))
                partes.Add($"Grupo {codigo}");

            if (!string.IsNullOrWhiteSpace(descripcion))
                partes.Add(descripcion);

            if (!string.IsNullOrWhiteSpace(nombreJefe))
                partes.Add($"Jefe: {nombreJefe}");

            return string.Join(" - ", partes);
        }

        private static decimal ConvertirMontoImpulsador(
            decimal monto,
            string? monedaReporte,
            decimal tipoCambio)
        {
            var moneda = Normalizar(monedaReporte);

            if (moneda == "CRC")
                return monto;

            if (moneda == "USD")
            {
                if (tipoCambio <= 0)
                    throw new InvalidOperationException(
                        "El tipo de cambio debe ser mayor que cero para convertir impulsadores a USD.");

                return Math.Round(
                    monto / tipoCambio,
                    2,
                    MidpointRounding.AwayFromZero);
            }

            throw new InvalidOperationException(
                $"Moneda de reporte no soportada: {moneda}. Solo se permiten CRC y USD.");
        }

        private static decimal ConvertirMonedaReporte(
            decimal monto,
            string? monedaOrigen,
            string? monedaDestino,
            decimal tipoCambio)
        {
            var origen = Normalizar(monedaOrigen);
            var destino = Normalizar(monedaDestino);

            if (origen != "CRC" && origen != "USD")
            {
                throw new InvalidOperationException(
                    $"Moneda origen no soportada: {origen}. Solo se permiten CRC y USD.");
            }

            if (destino != "CRC" && destino != "USD")
            {
                throw new InvalidOperationException(
                    $"Moneda destino no soportada: {destino}. Solo se permiten CRC y USD.");
            }

            if (origen == destino)
                return monto;

            if (tipoCambio <= 0)
            {
                throw new InvalidOperationException(
                    "El tipo de cambio debe ser mayor que cero para convertir monedas.");
            }

            // TipoCambio = CRC por 1 USD.
            var convertido =
                origen == "USD" && destino == "CRC"
                    ? monto * tipoCambio
                    : monto / tipoCambio; // CRC -> USD

            return Math.Round(
                convertido,
                2,
                MidpointRounding.AwayFromZero);
        }

        private static decimal ConvertirMonto(decimal monto, string? moneda, decimal tipoCambio)
        {
            // CALCULA_COMISIONES ya guarda los importes de agentes en la
            // moneda seleccionada en P_MONEDA. No se vuelve a convertir aquí.
            return monto;
        }

        private static void PrepararFiltro(ResumenCobrosAgenteFiltroVm filtro)
        {
            filtro.BuNombre = string.IsNullOrWhiteSpace(filtro.BuNombre)
                ? "LANCO_CR"
                : filtro.BuNombre.Trim();

            if (!filtro.FechaDesde.HasValue)
                filtro.FechaDesde = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

            if (!filtro.FechaHasta.HasValue)
                filtro.FechaHasta = DateTime.Today;

            if (filtro.TipoCambio <= 0)
                filtro.TipoCambio = 1;

            filtro.Moneda = string.IsNullOrWhiteSpace(filtro.Moneda)
                ? "CRC"
                : filtro.Moneda.Trim().ToUpperInvariant();

            filtro.ChequeDevuelto = string.IsNullOrWhiteSpace(filtro.ChequeDevuelto)
                ? null
                : filtro.ChequeDevuelto.Trim().ToUpperInvariant();
        }

        private static void PrepararParametrosCalculo(
            ResumenCobrosAgenteFiltroVm filtro,
            ParametrosCalculoComisionVm parametros)
        {
            var fechaBase = filtro.FechaDesde ?? DateTime.Today;

            if (parametros.AnoFiscal <= 0)
                parametros.AnoFiscal = fechaBase.Year;

            if (parametros.Periodo < 1 || parametros.Periodo > 12)
                parametros.Periodo = fechaBase.Month;

            parametros.TipoChkDev1 = string.IsNullOrWhiteSpace(parametros.TipoChkDev1)
                ? "CHD"
                : parametros.TipoChkDev1.Trim().ToUpperInvariant();

            parametros.TipoChkDev2 = string.IsNullOrWhiteSpace(parametros.TipoChkDev2)
                ? "CH2"
                : parametros.TipoChkDev2.Trim().ToUpperInvariant();

            parametros.TipoDescuento = string.IsNullOrWhiteSpace(parametros.TipoDescuento)
                ? "NCD"
                : parametros.TipoDescuento.Trim().ToUpperInvariant();

            parametros.PorcentajeImpuesto =
                string.IsNullOrWhiteSpace(parametros.PorcentajeImpuesto)
                    ? "13"
                    : parametros.PorcentajeImpuesto.Trim();

            parametros.AplicarImpuesto =
                Normalizar(parametros.AplicarImpuesto) == "N"
                    ? "N"
                    : "S";

            // La configuración creada corresponde a periodos mensuales.
            filtro.FechaDesde = new DateTime(
                parametros.AnoFiscal,
                parametros.Periodo,
                1);

            filtro.FechaHasta = filtro.FechaDesde.Value
                .AddMonths(1)
                .AddDays(-1);
        }

        private async Task EjecutarCalculoAgentesAsync(
            ResumenCobrosAgenteFiltroVm filtro,
            ParametrosCalculoComisionVm parametros)
        {
            const string bloquePlSql = @"
                BEGIN
                    BG_INTUSER.COMISIONCOBRO_XXORA.CALCULA_COMISIONES
                    (
                        P_CIA             => :P_CIA,
                        P_SUC             => :P_SUC,
                        P_MONEDA          => :P_MONEDA,
                        P_ANO             => :P_ANO,
                        P_PER             => :P_PER,
                        P_COD_MONEDA_BASE => :P_COD_MONEDA_BASE,
                        P_TIPO_CAMBIO     => :P_TIPO_CAMBIO,
                        P_TIPO_CHKDEV     => :P_TIPO_CHKDEV,
                        P_TIPO_CHKDEV1    => :P_TIPO_CHKDEV1,
                        P_TIPO_DESCTO     => :P_TIPO_DESCTO,
                        P_POR_IMP_VENTA   => :P_POR_IMP_VENTA,
                        P_IMPUESTO        => :P_IMPUESTO
                    );
                END;";

            await EjecutarProcedimientoAsync(
                bloquePlSql,
                command =>
                {
                    AgregarParametro(command, "P_CIA", filtro.BuNombre, DbType.String);
                    AgregarParametro(command, "P_SUC", SucursalFija, DbType.String);
                    AgregarParametro(command, "P_MONEDA", filtro.Moneda, DbType.String);
                    AgregarParametro(command, "P_ANO", parametros.AnoFiscal, DbType.Int32);
                    AgregarParametro(command, "P_PER", parametros.Periodo, DbType.Int32);
                    AgregarParametro(command, "P_COD_MONEDA_BASE", MonedaBaseSistema, DbType.String);
                    AgregarParametro(command, "P_TIPO_CAMBIO", filtro.TipoCambio, DbType.Decimal);
                    AgregarParametro(command, "P_TIPO_CHKDEV", parametros.TipoChkDev1, DbType.String);
                    AgregarParametro(command, "P_TIPO_CHKDEV1", parametros.TipoChkDev2, DbType.String);
                    AgregarParametro(command, "P_TIPO_DESCTO", parametros.TipoDescuento, DbType.String);
                    AgregarParametro(command, "P_POR_IMP_VENTA", parametros.PorcentajeImpuesto, DbType.String);
                    AgregarParametro(command, "P_IMPUESTO", parametros.AplicarImpuesto, DbType.String);
                });
        }

        private async Task EjecutarCalculoImpulsadoresAsync(
            ResumenCobrosAgenteFiltroVm filtro,
            ParametrosCalculoComisionVm parametros)
        {
            const string bloquePlSql = @"
                BEGIN
                    BG_INTUSER.COMISIONCOBRO_XXORA.COBROSCLIENTE
                    (
                        P_CIA             => :P_CIA,
                        P_SUC             => :P_SUC,
                        P_ANO             => :P_ANO,
                        P_PERIODO         => :P_PERIODO,
                        P_COD_MONEDA_BASE => :P_COD_MONEDA_BASE,
                        P_TIPO_CAMBIO     => :P_TIPO_CAMBIO,
                        P_TIPO_CHKDEV     => :P_TIPO_CHKDEV,
                        P_TIPO_CHKDEV1    => :P_TIPO_CHKDEV1,
                        P_TIPO_DESCTO     => :P_TIPO_DESCTO
                    );
                END;";

            await EjecutarProcedimientoAsync(
                bloquePlSql,
                command =>
                {
                    AgregarParametro(command, "P_CIA", filtro.BuNombre, DbType.String);
                    AgregarParametro(command, "P_SUC", SucursalFija, DbType.String);
                    AgregarParametro(command, "P_ANO", parametros.AnoFiscal, DbType.Int32);
                    AgregarParametro(command, "P_PERIODO", parametros.Periodo, DbType.Int32);
                    AgregarParametro(command, "P_COD_MONEDA_BASE", MonedaBaseSistema, DbType.String);
                    AgregarParametro(command, "P_TIPO_CAMBIO", filtro.TipoCambio, DbType.Decimal);
                    AgregarParametro(command, "P_TIPO_CHKDEV", parametros.TipoChkDev1, DbType.String);
                    AgregarParametro(command, "P_TIPO_CHKDEV1", parametros.TipoChkDev2, DbType.String);
                    AgregarParametro(command, "P_TIPO_DESCTO", parametros.TipoDescuento, DbType.String);
                });
        }

        private async Task<T> EjecutarCalculoYConsultarAsync<T>(
            ResumenCobrosAgenteFiltroVm filtro,
            ParametrosCalculoComisionVm parametros,
            Func<Task<T>> consultarResultados)
        {
            /*
             * Los distintos PDF y Excel leen los mismos resultados calculados.
             * La firma contiene todos los parámetros del formulario, excepto el
             * tipo/formato de reporte, ya que ese valor solamente determina qué
             * salida se genera.
             */
            var firmaActual = ConstruirFirmaCalculo(filtro, parametros);

            await CalculoComisionesLock.WaitAsync();

            try
            {
                var existeFirmaAnterior = _cache.TryGetValue(
                    UltimaFirmaCalculoCacheKey,
                    out string? firmaAnterior);

                var parametrosCambiaron =
                    !existeFirmaAnterior ||
                    !string.Equals(
                        firmaAnterior,
                        firmaActual,
                        StringComparison.Ordinal);

                if (parametrosCambiaron)
                {
                    /*
                     * Se ejecutan ambos procesos una sola vez para dejar preparados
                     * todos los datos que pueden consumir los reportes de agentes e
                     * impulsadores. La firma solo se guarda si ambos finalizaron.
                     */
                    await EjecutarCalculoAgentesAsync(filtro, parametros);
                    await EjecutarCalculoImpulsadoresAsync(filtro, parametros);

                    _cache.Set(
                        UltimaFirmaCalculoCacheKey,
                        firmaActual);
                }

                return await consultarResultados();
            }
            finally
            {
                CalculoComisionesLock.Release();
            }
        }

        private static string ConstruirFirmaCalculo(
            ResumenCobrosAgenteFiltroVm filtro,
            ParametrosCalculoComisionVm parametros)
        {
            static string Texto(string? valor) =>
                Normalizar(valor);

            static string Fecha(DateTime? valor) =>
                valor.HasValue
                    ? valor.Value.ToString(
                        "yyyyMMdd",
                        CultureInfo.InvariantCulture)
                    : "";

            static string Numero(decimal valor) =>
                valor.ToString(
                    "0.############################",
                    CultureInfo.InvariantCulture);

            return string.Join(
                "|",
                Texto(filtro.BuNombre),
                Fecha(filtro.FechaDesde),
                Fecha(filtro.FechaHasta),
                Texto(filtro.Moneda),
                Numero(filtro.TipoCambio),
                Texto(filtro.ChequeDevuelto),
                Texto(filtro.GrupoAgente),
                Texto(filtro.ClienteDesde),
                Texto(filtro.ClienteHasta),
                Texto(filtro.VendedorDesde),
                Texto(filtro.VendedorHasta),
                parametros.AnoFiscal.ToString(
                    CultureInfo.InvariantCulture),
                parametros.Periodo.ToString(
                    CultureInfo.InvariantCulture),
                Texto(parametros.TipoChkDev1),
                Texto(parametros.TipoChkDev2),
                Texto(parametros.TipoDescuento),
                Texto(parametros.PorcentajeImpuesto),
                Texto(parametros.AplicarImpuesto));
        }

        private async Task EjecutarProcedimientoAsync(
            string bloquePlSql,
            Action<DbCommand> configurarParametros)
        {
            var connection = _context.Database.GetDbConnection();
            var cerrarConexion = connection.State != ConnectionState.Open;

            try
            {
                if (cerrarConexion)
                    await connection.OpenAsync();

                using var command = connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = bloquePlSql;
                command.CommandTimeout = 600;

                // ODP.NET usa BindByName. Se activa por reflexión para no
                // acoplar el controller a una versión específica del proveedor.
                var propiedadBindByName = command
                    .GetType()
                    .GetProperty("BindByName");

                if (propiedadBindByName?.CanWrite == true)
                    propiedadBindByName.SetValue(command, true);

                configurarParametros(command);

                await command.ExecuteNonQueryAsync();
            }
            catch (DbException ex)
            {
                throw new InvalidOperationException(
                    "No se pudo ejecutar el cálculo de comisiones en Oracle. " +
                    ex.Message,
                    ex);
            }
            finally
            {
                if (cerrarConexion &&
                    connection.State == ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }
        }

        private static void AgregarParametro(
            DbCommand command,
            string nombre,
            object? valor,
            DbType tipo)
        {
            var parametro = command.CreateParameter();
            parametro.ParameterName = nombre;
            parametro.DbType = tipo;
            parametro.Value = valor ?? DBNull.Value;
            command.Parameters.Add(parametro);
        }

        private static string Normalizar(string? valor)
        {
            return (valor ?? "").Trim().ToUpperInvariant();
        }

        // ---------------------------------------------------------------------
        // Reporte: Cobros Diarios por Agente (equivalente a cobros\cxccagd).
        //
        // Reglas:
        // - Fuente exclusiva: XXORA_COMISIONES.
        // - Factura: NUM_TRX_APLICADA.
        // - Cobros Dia: MONTO_ORIGINAL_FACTURA, conservando el impuesto.
        // - Moneda origen: MONEDA_FACTURA; CRC y USD se convierten a la moneda seleccionada.
        // - Descuentos: DESCUENTO.
        // - Chk Dev.: CHEQUE_DEVUELTO.
        // - Cobro Neto = Cobros Dia - Chk Dev. - Descuentos.
        // - Cobros Mes conserva el mismo neto del periodo seleccionado.
        // ---------------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> CobrosDiariosAgentePdf(
            ResumenCobrosAgenteFiltroVm filtro,
            ParametrosCalculoComisionVm parametros)
        {
            PrepararFiltro(filtro);
            PrepararParametrosCalculo(filtro, parametros);

            var filas = await EjecutarCalculoYConsultarAsync(
                filtro,
                parametros,
                () => ObtenerCobrosDiariosAgenteAsync(
                    filtro,
                    parametros));

            QuestPDF.Settings.License =
                QLicenseType.Community;

            var fechaGeneracion = DateTime.Now;

            var pdfBytes = QDocument.Create(container =>
            {
                container.Page(page =>
                {
                    // Reporte diario en orientación vertical.
                    page.Size(PageSizes.Letter);
                    page.MarginHorizontal(10);
                    page.MarginVertical(12);
                    page.DefaultTextStyle(x => x.FontSize(5.8f));

                    page.Header().Column(header =>
                    {
                        header.Item().Row(row =>
                        {
                            row.RelativeItem()
                                .Text("Cuentas Por Cobrar")
                                .FontSize(8);

                            row.RelativeItem(1.6f)
                                .AlignCenter()
                                .Text("LANCO & HARRIS MFG. CORP. SRL")
                                .FontSize(8);

                            row.RelativeItem()
                                .AlignRight()
                                .Text(text =>
                                {
                                    text.Span("Pagina     ");
                                    text.CurrentPageNumber();
                                    text.Span(" / ");
                                    text.TotalPages();
                                });
                        });

                        header.Item().Row(row =>
                        {
                            row.RelativeItem().Text("");

                            row.RelativeItem(1.6f)
                                .AlignCenter()
                                .Text("PRINCIPAL")
                                .FontSize(8);

                            row.RelativeItem()
                                .AlignRight()
                                .Text(
                                    fechaGeneracion.ToString(
                                        "dd/MM/yyyy HH:mm"))
                                .FontSize(8);
                        });

                        header.Item()
                            .AlignCenter()
                            .Text("Cobros Diarios por Agente")
                            .FontSize(9)
                            .Bold();

                        header.Item()
                            .AlignCenter()
                            .Text(
                                $"Desde: " +
                                $"{filtro.FechaDesde:dd/MM/yyyy}, " +
                                $"Hasta: " +
                                $"{filtro.FechaHasta:dd/MM/yyyy}.")
                            .FontSize(8);
                    });

                    page.Content()
                        .PaddingTop(8)
                        .Column(content =>
                        {
                            content.Item()
                                .Text(text =>
                                {
                                    text.Span("Linea:    ")
                                        .Bold();

                                    text.Span(
                                            $"{LineaCobroFija} " +
                                            DescripcionLineaCobroFija)
                                        .Bold();
                                });

                            content.Item()
                                .PaddingTop(3)
                                .Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.ConstantColumn(22);
                                        columns.RelativeColumn(1.80f);
                                        columns.RelativeColumn(1.05f);
                                        columns.RelativeColumn(0.85f);
                                        columns.RelativeColumn(0.95f);
                                        columns.RelativeColumn(1.05f);
                                        columns.RelativeColumn(1.05f);
                                    });

                                    table.Header(header =>
                                    {
                                        header.Cell()
                                            .ColumnSpan(2)
                                            .Element(HeaderStyleCobrosDiarios)
                                            .Text("Agte Nombre")
                                            .Bold();

                                        header.Cell()
                                            .Element(HeaderStyleCobrosDiarios)
                                            .AlignRight()
                                            .Text("Cobros Dia")
                                            .Bold();

                                        header.Cell()
                                            .Element(HeaderStyleCobrosDiarios)
                                            .AlignRight()
                                            .Text("Chk Dev.")
                                            .Bold();

                                        header.Cell()
                                            .Element(HeaderStyleCobrosDiarios)
                                            .AlignRight()
                                            .Text("Descuentos")
                                            .Bold();

                                        header.Cell()
                                            .Element(HeaderStyleCobrosDiarios)
                                            .AlignRight()
                                            .Text("Cobro Neto")
                                            .Bold();

                                        header.Cell()
                                            .Element(HeaderStyleCobrosDiarios)
                                            .AlignRight()
                                            .Text("Cobros Mes")
                                            .Bold();
                                    });

                                    if (!filas.Any())
                                    {
                                        table.Cell()
                                            .ColumnSpan(7)
                                            .PaddingTop(12)
                                            .AlignCenter()
                                            .Text(
                                                "No hay agentes para los " +
                                                "filtros seleccionados.")
                                            .FontSize(7);
                                    }
                                    else
                                    {
                                        var grupos = filas
                                            .GroupBy(x => new
                                            {
                                                x.GrupoCodigo,
                                                x.GrupoDescripcion
                                            })
                                            .OrderBy(
                                                x => x.Key.GrupoCodigo,
                                                StringComparer.OrdinalIgnoreCase)
                                            .ToList();

                                        foreach (var grupo in grupos)
                                        {
                                            var tituloGrupo =
                                                ObtenerTituloGrupoCobrosDiarios(
                                                    grupo.Key.GrupoCodigo,
                                                    grupo.Key.GrupoDescripcion);

                                            if (!string.IsNullOrWhiteSpace(tituloGrupo))
                                            {
                                                table.Cell()
                                                    .ColumnSpan(7)
                                                    .PaddingTop(5)
                                                    .PaddingBottom(2)
                                                    .Text(tituloGrupo)
                                                    .FontSize(6.5f)
                                                    .Bold();
                                            }

                                            foreach (var fila in grupo
                                                .OrderBy(
                                                    x => x.CodAgente,
                                                    StringComparer.OrdinalIgnoreCase))
                                            {
                                                table.Cell()
                                                    .Element(BodyStyleCobrosDiarios)
                                                    .Text(fila.CodAgente);

                                                table.Cell()
                                                    .Element(BodyStyleCobrosDiarios)
                                                    .Text(fila.NombreAgente);

                                                table.Cell()
                                                    .Element(BodyStyleCobrosDiarios)
                                                    .AlignRight()
                                                    .Text(
                                                        FormatoMontoCobrosDiarios(
                                                            fila.CobrosDia));

                                                table.Cell()
                                                    .Element(BodyStyleCobrosDiarios)
                                                    .AlignRight()
                                                    .Text(
                                                        FormatoMontoCobrosDiarios(
                                                            fila.ChequesDevueltos));

                                                table.Cell()
                                                    .Element(BodyStyleCobrosDiarios)
                                                    .AlignRight()
                                                    .Text(
                                                        FormatoMontoCobrosDiarios(
                                                            fila.Descuentos));

                                                table.Cell()
                                                    .Element(BodyStyleCobrosDiarios)
                                                    .AlignRight()
                                                    .Text(
                                                        FormatoMontoCobrosDiarios(
                                                            fila.CobroNeto));

                                                table.Cell()
                                                    .Element(BodyStyleCobrosDiarios)
                                                    .AlignRight()
                                                    .Text(
                                                        FormatoMontoCobrosDiarios(
                                                            fila.CobrosMes));
                                            }
                                        }

                                        table.Cell()
                                            .ColumnSpan(2)
                                            .Element(TotalStyleCobrosDiarios)
                                            .Text("Totales Por Moneda:");

                                        table.Cell()
                                            .Element(TotalStyleCobrosDiarios)
                                            .AlignRight()
                                            .Text(
                                                FormatoMontoCobrosDiarios(
                                                    filas.Sum(
                                                        x => x.CobrosDia)));

                                        table.Cell()
                                            .Element(TotalStyleCobrosDiarios)
                                            .AlignRight()
                                            .Text(
                                                FormatoMontoCobrosDiarios(
                                                    filas.Sum(
                                                        x =>
                                                            x.ChequesDevueltos)));

                                        table.Cell()
                                            .Element(TotalStyleCobrosDiarios)
                                            .AlignRight()
                                            .Text(
                                                FormatoMontoCobrosDiarios(
                                                    filas.Sum(
                                                        x => x.Descuentos)));

                                        table.Cell()
                                            .Element(TotalStyleCobrosDiarios)
                                            .AlignRight()
                                            .Text(
                                                FormatoMontoCobrosDiarios(
                                                    filas.Sum(
                                                        x => x.CobroNeto)));

                                        table.Cell()
                                            .Element(TotalStyleCobrosDiarios)
                                            .AlignRight()
                                            .Text(
                                                FormatoMontoCobrosDiarios(
                                                    filas.Sum(
                                                        x => x.CobrosMes)));
                                    }
                                });
                        });

                    page.Footer().Column(footer =>
                    {
                        footer.Item().LineHorizontal(0.5f);

                        footer.Item().PaddingTop(3).Row(row =>
                        {
                            row.RelativeItem()
                                .Text(@"cobros\cxccagd")
                                .FontSize(7);

                            row.RelativeItem()
                                .AlignRight()
                                .Text("NUEVO")
                                .FontSize(7);
                        });
                    });
                });
            }).GeneratePdf();

            var fileName =
                $"Cobros_Diarios_Agente_" +
                $"{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

            return File(
                pdfBytes,
                "application/pdf",
                fileName);
        }


        [HttpGet]
        public async Task<IActionResult> CobrosDiariosAgenteExcel(
            ResumenCobrosAgenteFiltroVm filtro,
            ParametrosCalculoComisionVm parametros)
        {
            PrepararFiltro(filtro);
            PrepararParametrosCalculo(filtro, parametros);

            var filas = await EjecutarCalculoYConsultarAsync(
                filtro,
                parametros,
                () => ObtenerCobrosDiariosAgenteAsync(
                    filtro,
                    parametros));

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Cobros diarios");

            var row = 1;

            ws.Cell(row, 1).Value = "Cuentas Por Cobrar";
            ws.Cell(row, 3).Value =
                "LANCO & HARRIS MFG. CORP. SRL";
            ws.Range(row, 3, row, 5).Merge();
            ws.Cell(row, 7).Value = "Pagina 1";
            row++;

            ws.Cell(row, 3).Value = "PRINCIPAL";
            ws.Range(row, 3, row, 5).Merge();
            ws.Cell(row, 7).Value =
                DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            row++;

            ws.Cell(row, 3).Value =
                "Cobros Diarios por Agente";
            ws.Range(row, 3, row, 5).Merge();
            ws.Range(row, 3, row, 5)
                .Style.Font.Bold = true;
            row++;

            ws.Cell(row, 3).Value =
                $"Desde: {filtro.FechaDesde:dd/MM/yyyy}, " +
                $"Hasta: {filtro.FechaHasta:dd/MM/yyyy}.";
            ws.Range(row, 3, row, 5).Merge();
            row += 2;

            ws.Cell(row, 1).Value = "Linea:";
            ws.Cell(row, 2).Value =
                $"{LineaCobroFija} " +
                DescripcionLineaCobroFija;
            ws.Range(row, 2, row, 4).Merge();
            ws.Range(row, 1, row, 4)
                .Style.Font.Bold = true;
            row++;

            var headerRow = row;

            ws.Cell(row, 1).Value = "Agte";
            ws.Cell(row, 2).Value = "Nombre";
            ws.Cell(row, 3).Value = "Cobros Dia";
            ws.Cell(row, 4).Value = "Chk Dev.";
            ws.Cell(row, 5).Value = "Descuentos";
            ws.Cell(row, 6).Value = "Cobro Neto";
            ws.Cell(row, 7).Value = "Cobros Mes";

            ws.Range(row, 1, row, 7)
                .Style.Font.Bold = true;
            ws.Range(row, 1, row, 7)
                .Style.Border.BottomBorder =
                    XLBorderStyleValues.Thin;
            row++;

            if (!filas.Any())
            {
                ws.Cell(row, 1).Value =
                    "No hay agentes para los filtros seleccionados.";
                ws.Range(row, 1, row, 7).Merge();
                ws.Range(row, 1, row, 7)
                    .Style.Alignment.Horizontal =
                        XLAlignmentHorizontalValues.Center;
            }
            else
            {
                var grupos = filas
                    .GroupBy(x => new
                    {
                        x.GrupoCodigo,
                        x.GrupoDescripcion
                    })
                    .OrderBy(
                        x => x.Key.GrupoCodigo,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var grupo in grupos)
                {
                    var tituloGrupo =
                        ObtenerTituloGrupoCobrosDiarios(
                            grupo.Key.GrupoCodigo,
                            grupo.Key.GrupoDescripcion);

                    if (!string.IsNullOrWhiteSpace(tituloGrupo))
                    {
                        ws.Cell(row, 1).Value = tituloGrupo;
                        ws.Range(row, 1, row, 7).Merge();
                        ws.Range(row, 1, row, 7)
                            .Style.Font.Bold = true;
                        row++;
                    }

                    foreach (var fila in grupo
                        .OrderBy(
                            x => x.CodAgente,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        ws.Cell(row, 1).Value = fila.CodAgente;
                        ws.Cell(row, 2).Value = fila.NombreAgente;
                        ws.Cell(row, 3).Value = fila.CobrosDia;
                        ws.Cell(row, 4).Value =
                            fila.ChequesDevueltos;
                        ws.Cell(row, 5).Value = fila.Descuentos;
                        ws.Cell(row, 6).Value = fila.CobroNeto;
                        ws.Cell(row, 7).Value = fila.CobrosMes;
                        row++;
                    }
                }

                ws.Cell(row, 1).Value =
                    "Totales Por Moneda:";
                ws.Range(row, 1, row, 2).Merge();
                ws.Cell(row, 3).Value =
                    filas.Sum(x => x.CobrosDia);
                ws.Cell(row, 4).Value =
                    filas.Sum(x => x.ChequesDevueltos);
                ws.Cell(row, 5).Value =
                    filas.Sum(x => x.Descuentos);
                ws.Cell(row, 6).Value =
                    filas.Sum(x => x.CobroNeto);
                ws.Cell(row, 7).Value =
                    filas.Sum(x => x.CobrosMes);

                ws.Range(row, 1, row, 7)
                    .Style.Font.Bold = true;
                ws.Range(row, 1, row, 7)
                    .Style.Border.TopBorder =
                        XLBorderStyleValues.Thin;
            }

            ws.SheetView.FreezeRows(headerRow);

            ws.Column(1).Width = 10;
            ws.Column(2).Width = 38;
            ws.Columns(3, 7).Width = 19;
            ws.Columns(3, 7)
                .Style.NumberFormat.Format =
                    "#,##0.00";

            ws.Range(1, 1, 4, 7)
                .Style.Alignment.Horizontal =
                    XLAlignmentHorizontalValues.Center;

            ws.Cell(1, 1)
                .Style.Alignment.Horizontal =
                    XLAlignmentHorizontalValues.Left;
            ws.Cell(1, 7)
                .Style.Alignment.Horizontal =
                    XLAlignmentHorizontalValues.Right;
            ws.Cell(2, 7)
                .Style.Alignment.Horizontal =
                    XLAlignmentHorizontalValues.Right;

            ws.PageSetup.PageOrientation =
                XLPageOrientation.Portrait;
            ws.PageSetup.FitToPages(1, 0);
            ws.PageSetup.Margins.Top = 0.25;
            ws.PageSetup.Margins.Bottom = 0.25;
            ws.PageSetup.Margins.Left = 0.25;
            ws.PageSetup.Margins.Right = 0.25;

            var stream = new MemoryStream();

            var fileName =
                $"Cobros_Diarios_Agente_" +
                $"{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

            wb.SaveAs(stream);
            stream.Position = 0;

            return File(
                stream,
                "application/vnd.openxmlformats-officedocument." +
                "spreadsheetml.sheet",
                fileName);
        }


        private async Task<List<CobrosDiariosAgenteFila>>
            ObtenerCobrosDiariosAgenteAsync(
                ResumenCobrosAgenteFiltroVm filtro,
                ParametrosCalculoComisionVm parametros)
        {
            var bu =
                (filtro.BuNombre ?? "LANCO_CR").Trim();

            var fechaDesde =
                (filtro.FechaDesde ?? DateTime.Today).Date;

            var fechaHasta =
                (filtro.FechaHasta ?? DateTime.Today).Date;

            var fechaHastaExclusiva =
                fechaHasta.AddDays(1);

            /*
             * Este filtro aplica únicamente al reporte
             * Cobros Diarios por Agente.
             *
             * Se leen facturas CRC y USD. La moneda seleccionada es la
             * moneda DESTINO del reporte:
             * - CRC: los CRC quedan igual y los USD se multiplican por tipo de cambio.
             * - USD: los USD quedan igual y los CRC se dividen por tipo de cambio.
             */
            var monedaReporte =
                string.Equals(
                    Normalizar(filtro.Moneda),
                    "USD",
                    StringComparison.OrdinalIgnoreCase)
                    ? "USD"
                    : "CRC";

            /*
             * El reporte diario ya no utiliza CXC_DETAGE_COBRO.
             * Se construye directamente desde XXORA_COMISIONES:
             *
             * - Factura: NUM_TRX_APLICADA.
             * - Monto con impuesto: MONTO_ORIGINAL_FACTURA.
             * - Descuento: DESCUENTO.
             * - Cheque devuelto: CHEQUE_DEVUELTO.
             * - Moneda: MONEDA_FACTURA.
             * - Vendedor: VENDEDOR.
             * - Fecha del cobro: FECHA_RECIBO.
             */
            /*
 * El reporte diario toma la información de XXORA_COMISIONES,
 * pero excluye completamente cualquier factura que esté
 * registrada en XXORA_FACTURAMANOOBRA.
 *
 * Relación:
 * XXORA_FACTURAMANOOBRA.DOCUMENTO
 *      =
 * XXORA_COMISIONES.NUM_TRX_APLICADA
 */
            var movimientos =
                await _context.XXORA_COMISIONEs
                    .FromSqlInterpolated($@"
            SELECT X.*
              FROM BG_INTUSER.XXORA_COMISIONES X
             WHERE TRIM(UPPER(X.BU_NOMBRE)) =
                   TRIM(UPPER({bu}))
               AND X.FECHA_RECIBO >= {fechaDesde}
               AND X.FECHA_RECIBO < {fechaHastaExclusiva}
               AND X.VENDEDOR IS NOT NULL
               AND X.NUM_TRX_APLICADA IS NOT NULL
               AND X.MONEDA_FACTURA IS NOT NULL
               AND TRIM(UPPER(X.MONEDA_FACTURA)) IN ('CRC', 'USD')

               -- Excluye las facturas registradas como mano de obra.
               AND NOT EXISTS
               (
                   SELECT 1
                     FROM BG_INTUSER.XXORA_FACTURAMANOOBRA M
                    WHERE TRIM(UPPER(M.DOCUMENTO)) =
                          TRIM(UPPER(X.NUM_TRX_APLICADA))
               )
        ")
                    .AsNoTracking()
                    .ToListAsync();

            var catalogos =
                await ObtenerCatalogosAsync(bu);

            var grupoFiltro =
                string.IsNullOrWhiteSpace(
                    filtro.GrupoAgente)
                    ? null
                    : Normalizar(filtro.GrupoAgente);

            var vendedorDesde =
                string.IsNullOrWhiteSpace(
                    filtro.VendedorDesde)
                    ? null
                    : Normalizar(filtro.VendedorDesde);

            var vendedorHasta =
                string.IsNullOrWhiteSpace(
                    filtro.VendedorHasta)
                    ? null
                    : Normalizar(filtro.VendedorHasta);

            var clienteDesde =
                string.IsNullOrWhiteSpace(
                    filtro.ClienteDesde)
                    ? null
                    : Normalizar(filtro.ClienteDesde);

            var clienteHasta =
                string.IsNullOrWhiteSpace(
                    filtro.ClienteHasta)
                    ? null
                    : Normalizar(filtro.ClienteHasta);

            var vendedores = catalogos.Vendedores.Values
                .Select(vendedor => new
                {
                    Codigo =
                        (vendedor.IDVENDEDOR ??
                         vendedor.REGISTRY_ID ??
                         "").Trim(),

                    Nombre =
                        (vendedor.NOMBRE_VENDEDOR ??
                         vendedor.IDVENDEDOR ??
                         vendedor.REGISTRY_ID ??
                         "").Trim(),

                    GrupoCodigo =
                        (vendedor.CATEGORIA ?? "").Trim()
                })
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.Codigo))
                .GroupBy(x => Normalizar(x.Codigo))
                .Select(x => x.First())
                .Where(x =>
                    grupoFiltro == null ||
                    Normalizar(x.GrupoCodigo) ==
                        grupoFiltro)
                .Where(x =>
                    vendedorDesde == null ||
                    string.Compare(
                        Normalizar(x.Codigo),
                        vendedorDesde,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(x =>
                    vendedorHasta == null ||
                    string.Compare(
                        Normalizar(x.Codigo),
                        vendedorHasta,
                        StringComparison.OrdinalIgnoreCase) <= 0)
                .OrderBy(
                    x => x.Codigo,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

            var trabajos = vendedores
                .ToDictionary(
                    x => Normalizar(x.Codigo),
                    x => new CobrosDiariosAgenteTrabajo
                    {
                        CodAgente = x.Codigo,
                        NombreAgente = x.Nombre,
                        GrupoCodigo = x.GrupoCodigo,
                        GrupoDescripcion =
                            ObtenerDescripcionGrupo(
                                x.GrupoCodigo)
                    },
                    StringComparer.OrdinalIgnoreCase);

            /*
             * NUM_TRX_APLICADA identifica la factura.
             *
             * Una factura puede aparecer en más de una fila por aplicaciones
             * parciales o recibos diferentes. Se agrupa por vendedor y factura
             * para que MONTO_ORIGINAL_FACTURA se contabilice una sola vez.
             */
            var facturas = movimientos
                .Where(x =>
                {
                    var vendedor =
                        Normalizar(x.VENDEDOR);

                    var cliente =
                        Normalizar(x.ID_CLIENTE);

                    if (string.IsNullOrWhiteSpace(
                            vendedor) ||
                        string.IsNullOrWhiteSpace(
                            Normalizar(
                                x.NUM_TRX_APLICADA)))
                    {
                        return false;
                    }

                    if (vendedorDesde != null &&
                        string.Compare(
                            vendedor,
                            vendedorDesde,
                            StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return false;
                    }

                    if (vendedorHasta != null &&
                        string.Compare(
                            vendedor,
                            vendedorHasta,
                            StringComparison.OrdinalIgnoreCase) > 0)
                    {
                        return false;
                    }

                    if (clienteDesde != null &&
                        string.Compare(
                            cliente,
                            clienteDesde,
                            StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return false;
                    }

                    if (clienteHasta != null &&
                        string.Compare(
                            cliente,
                            clienteHasta,
                            StringComparison.OrdinalIgnoreCase) > 0)
                    {
                        return false;
                    }

                    return true;
                })
                .GroupBy(x => new
                {
                    Vendedor =
                        Normalizar(x.VENDEDOR),

                    Factura =
                        Normalizar(
                            x.NUM_TRX_APLICADA),

                    MonedaOrigen =
                        Normalizar(x.MONEDA_FACTURA)
                })
                .ToList();

            foreach (var factura in facturas)
            {
                if (!catalogos.Vendedores.TryGetValue(
                        factura.Key.Vendedor,
                        out var vendedor))
                {
                    continue;
                }

                var codigoSalida =
                    Normalizar(
                        vendedor.IDVENDEDOR ??
                        vendedor.REGISTRY_ID ??
                        factura.Key.Vendedor);

                if (!trabajos.TryGetValue(
                        codigoSalida,
                        out var trabajo))
                {
                    continue;
                }

                /*
                 * MONTO_ORIGINAL_FACTURA contiene el total de la factura con
                 * impuesto. Como puede repetirse en las aplicaciones, se toma
                 * un único valor por NUM_TRX_APLICADA.
                 *
                 * Cuando el monto original venga nulo o en cero, se utiliza
                 * como respaldo la suma de CANTIDAD_APLICADA.
                 */
                var montoFactura =
                    factura.Sum(x =>
                        x.CANTIDAD_APLICADA ?? 0m);

                var descuento =
                    factura.Sum(x =>
                        Math.Abs(
                            x.DESCUENTO ??
                            0m));

                /*
                 * CHEQUE_DEVUELTO es texto en Oracle. Si la factura se repite,
                 * se conserva un solo valor, tomando el mayor monto válido.
                 */
                var chequeDevuelto = 0m;

                foreach (var movimiento in factura)
                {
                    if (!TryParseMontoChequeDevuelto(
                            movimiento.CHEQUE_DEVUELTO,
                            out var montoCheque))
                    {
                        continue;
                    }

                    chequeDevuelto =
                        Math.Max(
                            chequeDevuelto,
                            Math.Abs(montoCheque));
                }

                var montoFacturaConvertido = ConvertirMonedaReporte(
                    montoFactura,
                    factura.Key.MonedaOrigen,
                    monedaReporte,
                    filtro.TipoCambio);

                var descuentoConvertido = ConvertirMonedaReporte(
                    descuento,
                    factura.Key.MonedaOrigen,
                    monedaReporte,
                    filtro.TipoCambio);

                var chequeDevueltoConvertido = ConvertirMonedaReporte(
                    chequeDevuelto,
                    factura.Key.MonedaOrigen,
                    monedaReporte,
                    filtro.TipoCambio);

                trabajo.Periodo.Cobros +=
                    montoFacturaConvertido;

                trabajo.Periodo.Descuentos +=
                    descuentoConvertido;

                trabajo.Periodo.ChequesDevueltos +=
                    chequeDevueltoConvertido;
            }

            return trabajos.Values
                .OrderBy(
                    x => x.CodAgente,
                    StringComparer.OrdinalIgnoreCase)
                .Select(x => new CobrosDiariosAgenteFila
                {
                    CodAgente = x.CodAgente,
                    NombreAgente = x.NombreAgente,
                    GrupoCodigo = x.GrupoCodigo,
                    GrupoDescripcion =
                        x.GrupoDescripcion,

                    CobrosDia =
                        x.Periodo.Cobros,

                    ChequesDevueltos =
                        x.Periodo.ChequesDevueltos,

                    Descuentos =
                        x.Periodo.Descuentos,

                    CobroNeto =
                        x.Periodo.CobroNeto,

                    CobrosMes =
                        x.Periodo.CobroNeto
                })
                .ToList();
        }


        private static bool TryParseMontoChequeDevuelto(
            string? valor,
            out decimal monto)
        {
            monto = 0m;

            var texto =
                (valor ?? "").Trim();

            if (string.IsNullOrWhiteSpace(texto))
                return false;

            var estilos =
                NumberStyles.Number |
                NumberStyles.AllowLeadingSign |
                NumberStyles.AllowTrailingSign |
                NumberStyles.AllowParentheses;

            var culturas = new[]
            {
                CultureInfo.InvariantCulture,
                CultureInfo.GetCultureInfo("en-US"),
                CultureInfo.GetCultureInfo("es-CR")
            };

            foreach (var cultura in culturas)
            {
                if (decimal.TryParse(
                        texto,
                        estilos,
                        cultura,
                        out monto))
                {
                    return true;
                }
            }

            var normalizado = texto
                .Replace("₡", "")
                .Replace("$", "")
                .Replace(" ", "");

            if (normalizado.Contains(',') &&
                normalizado.Contains('.'))
            {
                if (normalizado.LastIndexOf(',') >
                    normalizado.LastIndexOf('.'))
                {
                    normalizado =
                        normalizado
                            .Replace(".", "")
                            .Replace(",", ".");
                }
                else
                {
                    normalizado =
                        normalizado.Replace(",", "");
                }
            }
            else if (normalizado.Contains(','))
            {
                normalizado =
                    normalizado.Replace(",", ".");
            }

            return decimal.TryParse(
                normalizado,
                NumberStyles.Number |
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out monto);
        }


        private static IContainer HeaderStyleCobrosDiarios(
            IContainer container)
        {
            return container
                .PaddingBottom(2)
                .BorderBottom(0.5f)
                .PaddingHorizontal(2);
        }


        private static IContainer BodyStyleCobrosDiarios(
            IContainer container)
        {
            return container
                .PaddingVertical(0.7f)
                .PaddingHorizontal(2);
        }


        private static IContainer TotalStyleCobrosDiarios(
            IContainer container)
        {
            return container
                .PaddingTop(4)
                .BorderTop(0.5f)
                .PaddingHorizontal(2)
                .DefaultTextStyle(x => x.Bold());
        }


        private static string FormatoMontoCobrosDiarios(
            decimal monto)
        {
            return monto.ToString(
                "N2",
                CultureInfo.GetCultureInfo("en-US"));
        }


        //Area del reporte detallado:
        [HttpGet]
        public async Task<IActionResult> DetalleCobrosAgente(ResumenCobrosAgenteFiltroVm filtro)
        {
            PrepararFiltro(filtro);

            var vm = new ResumenCobrosAgentePageVm
            {
                Filtro = filtro,
                GruposAgente = await ObtenerGruposAgenteAsync(filtro.BuNombre)
            };

            return View(vm);
        }

        private async Task<List<DetalleCobrosAgenteFilaVm>>
            ObtenerDetalleCobrosAgenteAsync(
                ResumenCobrosAgenteFiltroVm filtro,
                ParametrosCalculoComisionVm parametros)
        {
            var bu = (filtro.BuNombre ?? "LANCO_CR").Trim();
            var detalles = await _context.CXC_DETAGE_COBROs
                .AsNoTracking()
                .Where(x =>
                    x.COD_CIA == bu &&
                    x.SUCURSAL == SucursalFija &&
                    x.ANO_FISCAL == parametros.AnoFiscal &&
                    x.PER_PROCESO == parametros.Periodo)
                .ToListAsync();

            var catalogos = await ObtenerCatalogosAsync(bu);

            detalles = AplicarRangosEnMemoria(
                detalles,
                filtro);

            var grupoFiltro = string.IsNullOrWhiteSpace(filtro.GrupoAgente)
                ? null
                : Normalizar(filtro.GrupoAgente);

            var filas = new List<DetalleCobrosAgenteFilaVm>(detalles.Count);

            foreach (var detalle in detalles)
            {
                var codVendedor = (detalle.COD_AGENTE ?? "").Trim();
                var vendedorKey = Normalizar(codVendedor);

                if (!catalogos.Vendedores.TryGetValue(vendedorKey, out var vendedor))
                    continue;

                var grupoCodigo = (vendedor.CATEGORIA ?? "").Trim();

                if (grupoFiltro != null &&
                    Normalizar(grupoCodigo) != grupoFiltro)
                {
                    continue;
                }

                var codCliente = (detalle.COD_CLIENTE ?? "").Trim();

                var cliente = BuscarClienteRapido(
                    catalogos,
                    codCliente,
                    codVendedor);

                var monto = ConvertirMonto(
                    detalle.MONTO,
                    detalle.COD_MONEDA,
                    filtro.TipoCambio);

                var descuento = ConvertirMonto(
                    detalle.DESCUENTO,
                    detalle.COD_MONEDA,
                    filtro.TipoCambio);

                var montoFactura = ConvertirMonto(
                    detalle.MON_COBRADO,
                    detalle.COD_MONEDA,
                    filtro.TipoCambio);

                var montoComision = ConvertirMonto(
                    detalle.MON_COMISION,
                    detalle.COD_MONEDA,
                    filtro.TipoCambio);

                var porcentajeComision = detalle.MON_COBRADO == 0m
                    ? 0m
                    : Math.Round(
                        detalle.MON_COMISION /
                        detalle.MON_COBRADO *
                        100m,
                        4);

                filas.Add(new DetalleCobrosAgenteFilaVm
                {
                    GrupoCodigo = grupoCodigo,
                    GrupoDescripcion = ObtenerDescripcionGrupo(grupoCodigo),

                    CodVendedor = codVendedor,
                    NombreVendedor = vendedor.NOMBRE_VENDEDOR ?? codVendedor,

                    CodCliente = codCliente,
                    NombreCliente =
                        cliente?.NOMBRE_CLIENTE ??
                        cliente?.PARTY_NAME ??
                        codCliente,

                    PorcentajeComision = porcentajeComision,

                    FechaRecibo = detalle.FECHADOC,
                    Recibo = detalle.DOCUMENTO ?? "",
                    TipoDocumento = detalle.TIP_DOC ?? "",
                    Factura = detalle.FACTURA ?? detalle.NUM_DOC ?? "",
                    FechaFactura = detalle.FECHAFACTURA,

                    Monto = monto,
                    Descuento = descuento,
                    MontoFactura = montoFactura,
                    MontoComision = montoComision
                });
            }

            return filas
                .OrderBy(x => x.CodVendedor)
                .ThenBy(x => x.CodCliente)
                .ThenBy(x => x.FechaRecibo)
                .ThenBy(x => x.Recibo)
                .ThenBy(x => x.Factura)
                .ToList();
        }


        [HttpGet]
        public async Task<IActionResult> DetalleCobrosAgentePdf(
            ResumenCobrosAgenteFiltroVm filtro,
            ParametrosCalculoComisionVm parametros)
        {
            PrepararFiltro(filtro);
            PrepararParametrosCalculo(filtro, parametros);
            var filas = await EjecutarCalculoYConsultarAsync(
                filtro,
                parametros,
                () => ObtenerDetalleCobrosAgenteAsync(filtro, parametros));

            QuestPDF.Settings.License = QLicenseType.Community;

            var pdfBytes = QDocument.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(22);
                    page.Size(PageSizes.Letter.Landscape());

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text("Cuentas Por Cobrar").FontSize(9);
                            row.RelativeItem().AlignCenter().Text("LANCO & HARRIS MFG. CORP. SRL").FontSize(9);
                            row.RelativeItem().AlignRight().Text(text =>
                            {
                                text.Span("Pagina ");
                                text.CurrentPageNumber();
                                text.Span("/");
                                text.TotalPages();
                            });
                        });

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text(@"cobros\cxccomcl").FontSize(8);
                            row.RelativeItem().AlignCenter().Text("Cobros Por Agente y Comision").FontSize(10).Bold();
                            row.RelativeItem().AlignRight().Text(DateTime.Now.ToString("dd/MM/yyyy hh:mm tt")).FontSize(8);
                        });

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text("");
                            row.RelativeItem().AlignCenter()
                                .Text($"Desde: {filtro.FechaDesde:dd/MM/yyyy}, Hasta: {filtro.FechaHasta:dd/MM/yyyy}.")
                                .FontSize(8);
                            row.RelativeItem().AlignRight().Text("NUEVO").FontSize(8);
                        });

                        col.Item().PaddingTop(6).LineHorizontal(0.5f);
                    });

                    page.Content().PaddingTop(6).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(58);       // Fec.Recibo
                            columns.ConstantColumn(70);       // Recibo
                            columns.ConstantColumn(42);       // Tip.Doc
                            columns.ConstantColumn(75);       // Factura
                            columns.ConstantColumn(58);       // Fec.Factura
                            columns.RelativeColumn(1.2f);     // Monto
                            columns.RelativeColumn(1.2f);     // Descuento
                            columns.RelativeColumn(1.2f);     // Monto Factura
                            columns.RelativeColumn(1.2f);     // Monto Comision
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderStyleDetalle).Text("Fec.Recibo").FontSize(7).Bold();
                            header.Cell().Element(HeaderStyleDetalle).Text("Recibo").FontSize(7).Bold();
                            header.Cell().Element(HeaderStyleDetalle).Text("Tip.Doc").FontSize(7).Bold();
                            header.Cell().Element(HeaderStyleDetalle).Text("Factura").FontSize(7).Bold();
                            header.Cell().Element(HeaderStyleDetalle).Text("Fec.Factura").FontSize(7).Bold();
                            header.Cell().Element(HeaderStyleDetalle).AlignRight().Text("Monto").FontSize(7).Bold();
                            header.Cell().Element(HeaderStyleDetalle).AlignRight().Text("Descuento").FontSize(7).Bold();
                            header.Cell().Element(HeaderStyleDetalle).AlignRight().Text("Monto Factura").FontSize(7).Bold();
                            header.Cell().Element(HeaderStyleDetalle).AlignRight().Text("Monto Comision").FontSize(7).Bold();
                        });

                        if (!filas.Any())
                        {
                            table.Cell().ColumnSpan(9)
                                .PaddingTop(12)
                                .AlignCenter()
                                .Text("No hay datos para los filtros seleccionados.")
                                .FontSize(9);

                            return;
                        }

                        var agentes = filas
                            .GroupBy(x => new { x.CodVendedor, x.NombreVendedor, x.GrupoCodigo, x.GrupoDescripcion })
                            .OrderBy(g => g.Key.CodVendedor)
                            .ToList();

                        foreach (var agente in agentes)
                        {
                            table.Cell().ColumnSpan(9).PaddingTop(8).Text(text =>
                            {
                                text.Span("AGENTE ").Bold().FontSize(8);
                                text.Span(agente.Key.CodVendedor).Bold().FontSize(8);
                                text.Span("  ").FontSize(8);
                                text.Span(agente.Key.NombreVendedor).Bold().FontSize(8);
                            });

                            var clientes = agente
                                .GroupBy(x => new { x.CodCliente, x.NombreCliente, x.PorcentajeComision })
                                .OrderBy(g => g.Key.CodCliente)
                                .ToList();

                            foreach (var cliente in clientes)
                            {
                                table.Cell().ColumnSpan(9).PaddingTop(4).Text(text =>
                                {
                                    text.Span(cliente.Key.CodCliente).Bold().FontSize(7);
                                    text.Span("    CLIENTE    ").FontSize(7);
                                    text.Span(cliente.Key.NombreCliente).Bold().FontSize(7);
                                    text.Span("      ").FontSize(7);
                                    text.Span(FormatoPorcentaje(cliente.Key.PorcentajeComision)).FontSize(7);
                                    text.Span(" % Comision").FontSize(7);
                                });

                                foreach (var item in cliente.OrderBy(x => x.FechaRecibo).ThenBy(x => x.Recibo).ThenBy(x => x.Factura))
                                {
                                    table.Cell().Element(BodyStyleDetalle).Text(FormatoFecha(item.FechaRecibo)).FontSize(7);
                                    table.Cell().Element(BodyStyleDetalle).Text(item.Recibo).FontSize(7);
                                    table.Cell().Element(BodyStyleDetalle).Text(item.TipoDocumento).FontSize(7);
                                    table.Cell().Element(BodyStyleDetalle).Text(item.Factura).FontSize(7);
                                    table.Cell().Element(BodyStyleDetalle).Text(FormatoFecha(item.FechaFactura)).FontSize(7);
                                    table.Cell().Element(BodyStyleDetalle).AlignRight().Text(FormatoMonto(item.Monto)).FontSize(7);
                                    table.Cell().Element(BodyStyleDetalle).AlignRight().Text(FormatoMonto(item.Descuento)).FontSize(7);
                                    table.Cell().Element(BodyStyleDetalle).AlignRight().Text(FormatoMonto(item.MontoFactura)).FontSize(7);
                                    table.Cell().Element(BodyStyleDetalle).AlignRight().Text(FormatoMonto(item.MontoComision)).FontSize(7);
                                }

                                table.Cell().ColumnSpan(5).Element(BodyStyleDetalle).AlignRight().Text("Total Cliente").FontSize(7).Bold();
                                table.Cell().Element(BodyStyleDetalle).AlignRight().Text(FormatoMonto(cliente.Sum(x => x.Monto))).FontSize(7).Bold();
                                table.Cell().Element(BodyStyleDetalle).AlignRight().Text(FormatoMonto(cliente.Sum(x => x.Descuento))).FontSize(7).Bold();
                                table.Cell().Element(BodyStyleDetalle).AlignRight().Text(FormatoMonto(cliente.Sum(x => x.MontoFactura))).FontSize(7).Bold();
                                table.Cell().Element(BodyStyleDetalle).AlignRight().Text(FormatoMonto(cliente.Sum(x => x.MontoComision))).FontSize(7).Bold();
                            }

                            table.Cell().ColumnSpan(5).Element(BodyStyleDetalle).AlignRight().Text("Total Agente").FontSize(7).Bold();
                            table.Cell().Element(BodyStyleDetalle).AlignRight().Text(FormatoMonto(agente.Sum(x => x.Monto))).FontSize(7).Bold();
                            table.Cell().Element(BodyStyleDetalle).AlignRight().Text(FormatoMonto(agente.Sum(x => x.Descuento))).FontSize(7).Bold();
                            table.Cell().Element(BodyStyleDetalle).AlignRight().Text(FormatoMonto(agente.Sum(x => x.MontoFactura))).FontSize(7).Bold();
                            table.Cell().Element(BodyStyleDetalle).AlignRight().Text(FormatoMonto(agente.Sum(x => x.MontoComision))).FontSize(7).Bold();
                        }

                        table.Cell().ColumnSpan(9).PaddingTop(8).LineHorizontal(0.5f);

                        table.Cell().ColumnSpan(5).Element(BodyStyleDetalle).AlignRight().Text("Total General").FontSize(7).Bold();
                        table.Cell().Element(BodyStyleDetalle).AlignRight().Text(FormatoMonto(filas.Sum(x => x.Monto))).FontSize(7).Bold();
                        table.Cell().Element(BodyStyleDetalle).AlignRight().Text(FormatoMonto(filas.Sum(x => x.Descuento))).FontSize(7).Bold();
                        table.Cell().Element(BodyStyleDetalle).AlignRight().Text(FormatoMonto(filas.Sum(x => x.MontoFactura))).FontSize(7).Bold();
                        table.Cell().Element(BodyStyleDetalle).AlignRight().Text(FormatoMonto(filas.Sum(x => x.MontoComision))).FontSize(7).Bold();
                    });
                });
            }).GeneratePdf();

            var fileName = $"Detalle_Cobros_Agente_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

            return File(pdfBytes, "application/pdf", fileName);
        }


        [HttpGet]
        public async Task<IActionResult> DetalleCobrosAgenteExcel(
            ResumenCobrosAgenteFiltroVm filtro,
            ParametrosCalculoComisionVm parametros)
        {
            PrepararFiltro(filtro);
            PrepararParametrosCalculo(filtro, parametros);
            var filas = await EjecutarCalculoYConsultarAsync(
                filtro,
                parametros,
                () => ObtenerDetalleCobrosAgenteAsync(filtro, parametros));

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Detalle");

            var row = 1;

            ws.Cell(row, 1).Value = "Cuentas Por Cobrar";
            ws.Cell(row, 4).Value = "LANCO & HARRIS MFG. CORP. SRL";
            ws.Cell(row, 9).Value = "Pagina";
            row++;

            ws.Cell(row, 1).Value = @"cobros\cxccomcl";
            ws.Cell(row, 4).Value = "Cobros Por Agente y Comision";
            ws.Cell(row, 9).Value = DateTime.Now.ToString("dd/MM/yyyy hh:mm tt");
            row++;

            ws.Cell(row, 4).Value = $"Desde: {filtro.FechaDesde:dd/MM/yyyy}, Hasta: {filtro.FechaHasta:dd/MM/yyyy}.";
            ws.Cell(row, 9).Value = "NUEVO";
            row += 2;

            ws.Cell(row, 1).Value = "Fec.Recibo";
            ws.Cell(row, 2).Value = "Recibo";
            ws.Cell(row, 3).Value = "Tip.Doc";
            ws.Cell(row, 4).Value = "Factura";
            ws.Cell(row, 5).Value = "Fec.Factura";
            ws.Cell(row, 6).Value = "Monto";
            ws.Cell(row, 7).Value = "Descuento";
            ws.Cell(row, 8).Value = "Monto Factura";
            ws.Cell(row, 9).Value = "Monto Comision";

            ws.Range(row, 1, row, 9).Style.Font.Bold = true;
            ws.Range(row, 1, row, 9).Style.Border.BottomBorder = XLBorderStyleValues.Thin;

            row++;

            if (!filas.Any())
            {
                ws.Cell(row, 1).Value = "No hay datos para los filtros seleccionados.";
                ws.Range(row, 1, row, 9).Merge();
                ws.Range(row, 1, row, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            else
            {
                var agentes = filas
                    .GroupBy(x => new { x.CodVendedor, x.NombreVendedor, x.GrupoCodigo, x.GrupoDescripcion })
                    .OrderBy(g => g.Key.CodVendedor)
                    .ToList();

                foreach (var agente in agentes)
                {
                    row++;

                    ws.Cell(row, 1).Value = $"AGENTE {agente.Key.CodVendedor} {agente.Key.NombreVendedor}";
                    ws.Range(row, 1, row, 9).Merge();
                    ws.Range(row, 1, row, 9).Style.Font.Bold = true;
                    row++;

                    var clientes = agente
                        .GroupBy(x => new { x.CodCliente, x.NombreCliente, x.PorcentajeComision })
                        .OrderBy(g => g.Key.CodCliente)
                        .ToList();

                    foreach (var cliente in clientes)
                    {
                        ws.Cell(row, 1).Value = cliente.Key.CodCliente;
                        ws.Cell(row, 2).Value = "CLIENTE";
                        ws.Cell(row, 3).Value = cliente.Key.NombreCliente;
                        ws.Cell(row, 5).Value = $"{FormatoPorcentaje(cliente.Key.PorcentajeComision)} % Comision";
                        ws.Range(row, 1, row, 5).Style.Font.Bold = true;
                        row++;

                        foreach (var item in cliente.OrderBy(x => x.FechaRecibo).ThenBy(x => x.Recibo).ThenBy(x => x.Factura))
                        {
                            ws.Cell(row, 1).Value = item.FechaRecibo;
                            ws.Cell(row, 2).Value = item.Recibo;
                            ws.Cell(row, 3).Value = item.TipoDocumento;
                            ws.Cell(row, 4).Value = item.Factura;
                            ws.Cell(row, 5).Value = item.FechaFactura;
                            ws.Cell(row, 6).Value = item.Monto;
                            ws.Cell(row, 7).Value = item.Descuento;
                            ws.Cell(row, 8).Value = item.MontoFactura;
                            ws.Cell(row, 9).Value = item.MontoComision;

                            row++;
                        }

                        ws.Cell(row, 5).Value = "Total Cliente";
                        ws.Cell(row, 6).Value = cliente.Sum(x => x.Monto);
                        ws.Cell(row, 7).Value = cliente.Sum(x => x.Descuento);
                        ws.Cell(row, 8).Value = cliente.Sum(x => x.MontoFactura);
                        ws.Cell(row, 9).Value = cliente.Sum(x => x.MontoComision);

                        ws.Range(row, 5, row, 9).Style.Font.Bold = true;
                        row++;
                    }

                    ws.Cell(row, 5).Value = "Total Agente";
                    ws.Cell(row, 6).Value = agente.Sum(x => x.Monto);
                    ws.Cell(row, 7).Value = agente.Sum(x => x.Descuento);
                    ws.Cell(row, 8).Value = agente.Sum(x => x.MontoFactura);
                    ws.Cell(row, 9).Value = agente.Sum(x => x.MontoComision);

                    ws.Range(row, 5, row, 9).Style.Font.Bold = true;
                    ws.Range(row, 5, row, 9).Style.Border.TopBorder = XLBorderStyleValues.Thin;
                    row++;
                }

                row++;

                ws.Cell(row, 5).Value = "Total General";
                ws.Cell(row, 6).Value = filas.Sum(x => x.Monto);
                ws.Cell(row, 7).Value = filas.Sum(x => x.Descuento);
                ws.Cell(row, 8).Value = filas.Sum(x => x.MontoFactura);
                ws.Cell(row, 9).Value = filas.Sum(x => x.MontoComision);

                ws.Range(row, 5, row, 9).Style.Font.Bold = true;
                ws.Range(row, 5, row, 9).Style.Border.TopBorder = XLBorderStyleValues.Thin;
            }

            //ws.Columns().AdjustToContents();
            ws.Column(1).Width = 14;
            ws.Column(2).Width = 35;
            ws.Column(3).Width = 20;
            ws.Column(4).Width = 20;
            ws.Column(5).Width = 25;
            ws.Column(6).Width = 20;

            ws.Column(1).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Column(5).Style.DateFormat.Format = "dd/MM/yyyy";

            ws.Column(6).Style.NumberFormat.Format = "#,##0.00";
            ws.Column(7).Style.NumberFormat.Format = "#,##0.00";
            ws.Column(8).Style.NumberFormat.Format = "#,##0.00";
            ws.Column(9).Style.NumberFormat.Format = "#,##0.00";

            var stream = new MemoryStream();

            var fileName = $"Detalle_Cobros_Agente_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

            wb.SaveAs(stream);

            stream.Position = 0;

            return File(
                stream,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName
            );
        }

        private async Task<List<ComisionesAgenteClienteFilaVm>>
            ObtenerComisionesAgenteClienteAsync(
                ResumenCobrosAgenteFiltroVm filtro,
                ParametrosCalculoComisionVm parametros)
        {
            var bu = (filtro.BuNombre ?? "LANCO_CR").Trim();
            var detalles = await _context.CXC_DETAGE_COBROs
                .AsNoTracking()
                .Where(x =>
                    x.COD_CIA == bu &&
                    x.SUCURSAL == SucursalFija &&
                    x.ANO_FISCAL == parametros.AnoFiscal &&
                    x.PER_PROCESO == parametros.Periodo)
                .ToListAsync();

            var catalogos = await ObtenerCatalogosAsync(bu);

            detalles = AplicarRangosEnMemoria(
                detalles,
                filtro);

            var grupoFiltro = string.IsNullOrWhiteSpace(filtro.GrupoAgente)
                ? null
                : Normalizar(filtro.GrupoAgente);

            var acumulados = new Dictionary<
                (
                    string GrupoCodigo,
                    string GrupoDescripcion,
                    string CodVendedor,
                    string NombreVendedor,
                    string CodCliente,
                    string NombreCliente
                ),
                ComisionesAgenteClienteFilaVm>();

            foreach (var detalle in detalles)
            {
                var codVendedor = (detalle.COD_AGENTE ?? "").Trim();
                var vendedorKey = Normalizar(codVendedor);

                if (!catalogos.Vendedores.TryGetValue(vendedorKey, out var vendedor))
                    continue;

                var grupoCodigo = (vendedor.CATEGORIA ?? "").Trim();

                if (grupoFiltro != null &&
                    Normalizar(grupoCodigo) != grupoFiltro)
                {
                    continue;
                }

                var codCliente = (detalle.COD_CLIENTE ?? "").Trim();

                var cliente = BuscarClienteRapido(
                    catalogos,
                    codCliente,
                    codVendedor);

                var nombreVendedor = vendedor.NOMBRE_VENDEDOR ?? codVendedor;

                var nombreCliente =
                    cliente?.NOMBRE_CLIENTE ??
                    cliente?.PARTY_NAME ??
                    codCliente;

                var grupoDescripcion = ObtenerDescripcionGrupo(grupoCodigo);

                var monto = ConvertirMonto(
                    detalle.MONTO,
                    detalle.COD_MONEDA,
                    filtro.TipoCambio);

                var descuento = ConvertirMonto(
                    detalle.DESCUENTO,
                    detalle.COD_MONEDA,
                    filtro.TipoCambio);

                var montoFacturaSinImpuesto = ConvertirMonto(
                    detalle.MON_COBRADO,
                    detalle.COD_MONEDA,
                    filtro.TipoCambio);

                var montoComision = ConvertirMonto(
                    detalle.MON_COMISION,
                    detalle.COD_MONEDA,
                    filtro.TipoCambio);

                var clave = (
                    GrupoCodigo: grupoCodigo,
                    GrupoDescripcion: grupoDescripcion,
                    CodVendedor: codVendedor,
                    NombreVendedor: nombreVendedor,
                    CodCliente: codCliente,
                    NombreCliente: nombreCliente);

                if (!acumulados.TryGetValue(clave, out var acumulado))
                {
                    acumulado = new ComisionesAgenteClienteFilaVm
                    {
                        GrupoCodigo = grupoCodigo,
                        GrupoDescripcion = grupoDescripcion,
                        CodVendedor = codVendedor,
                        NombreVendedor = nombreVendedor,
                        CodCliente = codCliente,
                        NombreCliente = nombreCliente
                    };

                    acumulados.Add(clave, acumulado);
                }

                acumulado.Monto += monto;
                acumulado.Descuento += descuento;
                acumulado.MontoFacturaSinImpuesto += montoFacturaSinImpuesto;
                acumulado.MontoComision += montoComision;
            }

            return acumulados.Values
                .OrderBy(x => x.CodVendedor)
                .ThenBy(x => x.CodCliente)
                .ToList();
        }


        [HttpGet]
        public async Task<IActionResult> ComisionesAgenteClientePdf(
            ResumenCobrosAgenteFiltroVm filtro,
            ParametrosCalculoComisionVm parametros)
        {
            PrepararFiltro(filtro);
            PrepararParametrosCalculo(filtro, parametros);
            var filas = await EjecutarCalculoYConsultarAsync(
                filtro,
                parametros,
                () => ObtenerComisionesAgenteClienteAsync(filtro, parametros));

            QuestPDF.Settings.License = QLicenseType.Community;

            var pdfBytes = QDocument.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(25);
                    page.Size(PageSizes.Letter);

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text("Cuentas Por Cobrar").FontSize(9);
                            row.RelativeItem().AlignCenter().Text("LANCO & HARRIS MFG. CORP. SRL").FontSize(9);
                            row.RelativeItem().AlignRight().Text(text =>
                            {
                                text.Span("Pagina ");
                                text.CurrentPageNumber();
                                text.Span("/");
                                text.TotalPages();
                            });
                        });

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text(@"cobros\cxccocli").FontSize(8);
                            row.RelativeItem().AlignCenter().Text("Comisiones X Agente X Cliente").FontSize(10).Bold();
                            row.RelativeItem().AlignRight().Text(DateTime.Now.ToString("dd/MM/yyyy hh:mm tt")).FontSize(8);
                        });

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text("");
                            row.RelativeItem().AlignCenter()
                                .Text($"Desde: {filtro.FechaDesde:dd/MM/yyyy}, Hasta: {filtro.FechaHasta:dd/MM/yyyy}.")
                                .FontSize(8);
                            row.RelativeItem().AlignRight().Text("NUEVO").FontSize(8);
                        });

                        col.Item().PaddingTop(6).LineHorizontal(0.5f);
                    });

                    page.Content().PaddingTop(6).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(55);       // Cliente
                            columns.RelativeColumn(3.5f);     // Nombre
                            columns.RelativeColumn(1.4f);     // Monto
                            columns.RelativeColumn(1.4f);     // Descuento
                            columns.RelativeColumn(1.7f);     // Monto Factura Sin Impuesto
                            columns.RelativeColumn(1.4f);     // Monto Comision
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderStyleResumen).Text("Cliente").FontSize(8).Bold();
                            header.Cell().Element(HeaderStyleResumen).Text("Nombre").FontSize(8).Bold();
                            header.Cell().Element(HeaderStyleResumen).AlignRight().Text("Monto").FontSize(8).Bold();
                            header.Cell().Element(HeaderStyleResumen).AlignRight().Text("Descuento").FontSize(8).Bold();
                            header.Cell().Element(HeaderStyleResumen).AlignRight().Text("Monto Factura\nSin Impuesto").FontSize(8).Bold();
                            header.Cell().Element(HeaderStyleResumen).AlignRight().Text("Monto Comision").FontSize(8).Bold();
                        });

                        if (!filas.Any())
                        {
                            table.Cell().ColumnSpan(6)
                                .PaddingTop(12)
                                .AlignCenter()
                                .Text("No hay datos para los filtros seleccionados.")
                                .FontSize(9);

                            return;
                        }

                        var agentes = filas
                            .GroupBy(x => new
                            {
                                x.CodVendedor,
                                x.NombreVendedor,
                                x.GrupoCodigo,
                                x.GrupoDescripcion
                            })
                            .OrderBy(g => g.Key.CodVendedor)
                            .ToList();

                        foreach (var agente in agentes)
                        {
                            table.Cell().ColumnSpan(6).PaddingTop(8).Text(text =>
                            {
                                text.Span("AGENTE ").Bold().FontSize(8);
                                text.Span(agente.Key.CodVendedor).Bold().FontSize(8);
                                text.Span("    ").FontSize(8);
                                text.Span(agente.Key.NombreVendedor).Bold().FontSize(8);
                            });

                            foreach (var item in agente.OrderBy(x => x.CodCliente))
                            {
                                table.Cell().Element(BodyStyleResumen).Text(item.CodCliente).FontSize(8);
                                table.Cell().Element(BodyStyleResumen).Text(item.NombreCliente).FontSize(8);
                                table.Cell().Element(BodyStyleResumen).AlignRight().Text(FormatoMonto(item.Monto)).FontSize(8);
                                table.Cell().Element(BodyStyleResumen).AlignRight().Text(FormatoMonto(item.Descuento)).FontSize(8);
                                table.Cell().Element(BodyStyleResumen).AlignRight().Text(FormatoMonto(item.MontoFacturaSinImpuesto)).FontSize(8);
                                table.Cell().Element(BodyStyleResumen).AlignRight().Text(FormatoMonto(item.MontoComision)).FontSize(8);
                            }

                            table.Cell().ColumnSpan(2).Element(BodyStyleResumen).AlignRight().Text("Total Agente").Bold().FontSize(8);
                            table.Cell().Element(BodyStyleResumen).AlignRight().Text(FormatoMonto(agente.Sum(x => x.Monto))).Bold().FontSize(8);
                            table.Cell().Element(BodyStyleResumen).AlignRight().Text(FormatoMonto(agente.Sum(x => x.Descuento))).Bold().FontSize(8);
                            table.Cell().Element(BodyStyleResumen).AlignRight().Text(FormatoMonto(agente.Sum(x => x.MontoFacturaSinImpuesto))).Bold().FontSize(8);
                            table.Cell().Element(BodyStyleResumen).AlignRight().Text(FormatoMonto(agente.Sum(x => x.MontoComision))).Bold().FontSize(8);
                        }

                        table.Cell().ColumnSpan(6).PaddingTop(8).LineHorizontal(0.5f);

                        table.Cell().ColumnSpan(2).Element(BodyStyleResumen).AlignRight().Text("Total General").Bold().FontSize(8);
                        table.Cell().Element(BodyStyleResumen).AlignRight().Text(FormatoMonto(filas.Sum(x => x.Monto))).Bold().FontSize(8);
                        table.Cell().Element(BodyStyleResumen).AlignRight().Text(FormatoMonto(filas.Sum(x => x.Descuento))).Bold().FontSize(8);
                        table.Cell().Element(BodyStyleResumen).AlignRight().Text(FormatoMonto(filas.Sum(x => x.MontoFacturaSinImpuesto))).Bold().FontSize(8);
                        table.Cell().Element(BodyStyleResumen).AlignRight().Text(FormatoMonto(filas.Sum(x => x.MontoComision))).Bold().FontSize(8);
                    });
                });
            }).GeneratePdf();

            var fileName = $"Comisiones_Agente_Cliente_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

            return File(pdfBytes, "application/pdf", fileName);
        }

        [HttpGet]
        public async Task<IActionResult> ComisionesAgenteClienteExcel(
            ResumenCobrosAgenteFiltroVm filtro,
            ParametrosCalculoComisionVm parametros)
        {
            PrepararFiltro(filtro);
            PrepararParametrosCalculo(filtro, parametros);
            var filas = await EjecutarCalculoYConsultarAsync(
                filtro,
                parametros,
                () => ObtenerComisionesAgenteClienteAsync(filtro, parametros));

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("AgenteCliente");

            var row = 1;

            ws.Cell(row, 1).Value = "Cuentas Por Cobrar";
            ws.Cell(row, 3).Value = "LANCO & HARRIS MFG. CORP. SRL";
            ws.Cell(row, 6).Value = "Pagina";
            row++;

            ws.Cell(row, 1).Value = @"cobros\cxccocli";
            ws.Cell(row, 3).Value = "Comisiones X Agente X Cliente";
            ws.Cell(row, 6).Value = DateTime.Now.ToString("dd/MM/yyyy hh:mm tt");
            row++;

            ws.Cell(row, 3).Value = $"Desde: {filtro.FechaDesde:dd/MM/yyyy}, Hasta: {filtro.FechaHasta:dd/MM/yyyy}.";
            ws.Cell(row, 6).Value = "NUEVO";
            row += 2;

            ws.Cell(row, 1).Value = "Cliente";
            ws.Cell(row, 2).Value = "Nombre";
            ws.Cell(row, 3).Value = "Monto";
            ws.Cell(row, 4).Value = "Descuento";
            ws.Cell(row, 5).Value = "Monto Factura Sin Impuesto";
            ws.Cell(row, 6).Value = "Monto Comision";

            ws.Range(row, 1, row, 6).Style.Font.Bold = true;
            ws.Range(row, 1, row, 6).Style.Border.BottomBorder = XLBorderStyleValues.Thin;

            row++;

            if (!filas.Any())
            {
                ws.Cell(row, 1).Value = "No hay datos para los filtros seleccionados.";
                ws.Range(row, 1, row, 6).Merge();
                ws.Range(row, 1, row, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            else
            {
                var agentes = filas
                    .GroupBy(x => new
                    {
                        x.CodVendedor,
                        x.NombreVendedor,
                        x.GrupoCodigo,
                        x.GrupoDescripcion
                    })
                    .OrderBy(g => g.Key.CodVendedor)
                    .ToList();

                foreach (var agente in agentes)
                {
                    row++;

                    ws.Cell(row, 1).Value = $"AGENTE {agente.Key.CodVendedor} {agente.Key.NombreVendedor}";
                    ws.Range(row, 1, row, 6).Merge();
                    ws.Range(row, 1, row, 6).Style.Font.Bold = true;
                    row++;

                    foreach (var item in agente.OrderBy(x => x.CodCliente))
                    {
                        ws.Cell(row, 1).Value = item.CodCliente;
                        ws.Cell(row, 2).Value = item.NombreCliente;
                        ws.Cell(row, 3).Value = item.Monto;
                        ws.Cell(row, 4).Value = item.Descuento;
                        ws.Cell(row, 5).Value = item.MontoFacturaSinImpuesto;
                        ws.Cell(row, 6).Value = item.MontoComision;

                        row++;
                    }

                    ws.Cell(row, 2).Value = "Total Agente";
                    ws.Cell(row, 3).Value = agente.Sum(x => x.Monto);
                    ws.Cell(row, 4).Value = agente.Sum(x => x.Descuento);
                    ws.Cell(row, 5).Value = agente.Sum(x => x.MontoFacturaSinImpuesto);
                    ws.Cell(row, 6).Value = agente.Sum(x => x.MontoComision);

                    ws.Range(row, 2, row, 6).Style.Font.Bold = true;
                    ws.Range(row, 2, row, 6).Style.Border.TopBorder = XLBorderStyleValues.Thin;

                    row++;
                }

                row++;

                ws.Cell(row, 2).Value = "Total General";
                ws.Cell(row, 3).Value = filas.Sum(x => x.Monto);
                ws.Cell(row, 4).Value = filas.Sum(x => x.Descuento);
                ws.Cell(row, 5).Value = filas.Sum(x => x.MontoFacturaSinImpuesto);
                ws.Cell(row, 6).Value = filas.Sum(x => x.MontoComision);

                ws.Range(row, 2, row, 6).Style.Font.Bold = true;
                ws.Range(row, 2, row, 6).Style.Border.TopBorder = XLBorderStyleValues.Thin;
            }

            //ws.Columns().AdjustToContents();
            ws.Column(1).Width = 14;
            ws.Column(2).Width = 18;
            ws.Column(3).Width = 14;
            ws.Column(4).Width = 20;
            ws.Column(5).Width = 14;
            ws.Column(6).Width = 18;
            ws.Column(7).Width = 18;
            ws.Column(8).Width = 20;
            ws.Column(9).Width = 20;

            ws.Column(3).Style.NumberFormat.Format = "#,##0.00";
            ws.Column(4).Style.NumberFormat.Format = "#,##0.00";
            ws.Column(5).Style.NumberFormat.Format = "#,##0.00";
            ws.Column(6).Style.NumberFormat.Format = "#,##0.00";

            var stream = new MemoryStream();

            var fileName = $"Comisiones_Agente_Cliente_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

            wb.SaveAs(stream);

            stream.Position = 0;

            return File(
                stream,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName
            );
        }




        private async Task<Dictionary<(string Cliente, string Empleado), ImpulsadorOracleConfiguracion>>
            ObtenerImpulsadoresOracleAsync(string bu)
        {
            /*
             * La única fuente de configuración de impulsadores es
             * BG_INTUSER.IMPULSADORESORACLE.
             *
             * CLIENTE contiene el IDCLIENTE utilizado por COBROSCLIENTE y
             * EMPLEADO/PORCENTAJE son los mismos valores que el paquete copia
             * a CXC_EMPLEADO_COBRO.
             */
            var resultado =
                new Dictionary<
                    (string Cliente, string Empleado),
                    ImpulsadorOracleConfiguracion>();

            var connection = _context.Database.GetDbConnection();
            var cerrarConexion =
                connection.State != ConnectionState.Open;

            try
            {
                if (cerrarConexion)
                    await connection.OpenAsync();

                using var command = connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = @"
                    SELECT TRIM(CLIENTE) AS CLIENTE,
                           TRIM(EMPLEADO) AS EMPLEADO,
                           NVL(PORCENTAJE, 0) AS PORCENTAJE
                      FROM BG_INTUSER.IMPULSADORESORACLE
                     WHERE TRIM(UPPER(BU_NOMBRE)) = :P_BU
                       AND CLIENTE IS NOT NULL
                       AND EMPLEADO IS NOT NULL";

                var bindByName = command
                    .GetType()
                    .GetProperty("BindByName");

                if (bindByName?.CanWrite == true)
                    bindByName.SetValue(command, true);

                AgregarParametro(
                    command,
                    "P_BU",
                    Normalizar(bu),
                    DbType.String);

                using var reader =
                    await command.ExecuteReaderAsync();

                var ordinalCliente =
                    reader.GetOrdinal("CLIENTE");

                var ordinalEmpleado =
                    reader.GetOrdinal("EMPLEADO");

                var ordinalPorcentaje =
                    reader.GetOrdinal("PORCENTAJE");

                while (await reader.ReadAsync())
                {
                    var cliente = reader.IsDBNull(ordinalCliente)
                        ? ""
                        : reader.GetString(ordinalCliente).Trim();

                    var empleado = reader.IsDBNull(ordinalEmpleado)
                        ? ""
                        : reader.GetString(ordinalEmpleado).Trim();

                    if (string.IsNullOrWhiteSpace(cliente) ||
                        string.IsNullOrWhiteSpace(empleado))
                    {
                        continue;
                    }

                    var porcentaje = reader.IsDBNull(ordinalPorcentaje)
                        ? 0m
                        : Convert.ToDecimal(
                            reader.GetValue(ordinalPorcentaje),
                            CultureInfo.InvariantCulture);

                    var configuracion =
                        new ImpulsadorOracleConfiguracion
                        {
                            Cliente = cliente,
                            Empleado = empleado,
                            Porcentaje = porcentaje
                        };

                    resultado[
                        (
                            Cliente: Normalizar(cliente),
                            Empleado: Normalizar(empleado)
                        )
                    ] = configuracion;
                }

                return resultado;
            }
            finally
            {
                if (cerrarConexion &&
                    connection.State == ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }
        }

        private async Task<Dictionary<string, string>>
            ObtenerNombresImpulsadoresAsync()
        {
            /*
             * CXC_EMPLEADO_COBRO e IMPULSADORESORACLE guardan el código del
             * empleado. El nombre descriptivo se obtiene desde NUEVO.PLAEMPLEADO
             * usando CIA + EMPLEADO.
             *
             * No se filtra por estado para poder resolver también nombres de
             * impulsadores que estuvieron activos durante periodos históricos.
             */
            var empleados = await _lancoContext.PLAEMPLEADOs
                .AsNoTracking()
                .Where(x =>
                    x.CIA == SucursalFija &&
                    x.EMPLEADO != null)
                .Select(x => new
                {
                    x.EMPLEADO,
                    x.NOMBRE
                })
                .ToListAsync();

            return empleados
                .Where(x => !string.IsNullOrWhiteSpace(x.EMPLEADO))
                .GroupBy(
                    x => Normalizar(x.EMPLEADO),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    grupo => grupo.Key,
                    grupo =>
                        grupo
                            .Select(x => (x.NOMBRE ?? "").Trim())
                            .FirstOrDefault(nombre =>
                                !string.IsNullOrWhiteSpace(nombre))
                        ?? (grupo.First().EMPLEADO ?? "").Trim(),
                    StringComparer.OrdinalIgnoreCase);
        }

        private async Task<List<ImpulsadorClienteFila>>
            ObtenerComisionesImpulsadorAsync(
                ResumenCobrosAgenteFiltroVm filtro,
                ParametrosCalculoComisionVm parametros)
        {
            var bu = Normalizar(filtro.BuNombre);

            if (string.IsNullOrWhiteSpace(bu))
                bu = "LANCO_CR";

            /*
             * Se normaliza COD_CIA porque puede venir con espacios por el tipo
             * de dato utilizado en Oracle. Esto evita que el cálculo tenga datos
             * y el controller devuelva el reporte en blanco.
             */
            var clientesCobro = await _context.CXC_CLIENTE_COBROs
                .AsNoTracking()
                .Where(x =>
                    x.COD_CIA != null &&
                    x.COD_CIA.Trim().ToUpper() == bu &&
                    x.ANO_FISCAL == parametros.AnoFiscal &&
                    x.PER_PROCESO == parametros.Periodo)
                .ToListAsync();

            var empleadosCobro = await _context.CXC_EMPLEADO_COBROs
                .AsNoTracking()
                .Where(x =>
                    x.COD_CIA != null &&
                    x.COD_CIA.Trim().ToUpper() == bu &&
                    x.ANO_FISCAL == parametros.AnoFiscal &&
                    x.PER_PROCESO == parametros.Periodo)
                .ToListAsync();

            var empleadosPorCliente = empleadosCobro
                .GroupBy(x => (
                    Agente: Normalizar(x.COD_AGENTE),
                    Cliente: Normalizar(x.COD_CLIENTE)))
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .OrderBy(x => x.EMPLEADO)
                        .ToList());

            var catalogos = await ObtenerCatalogosAsync(bu);

            var impulsadoresOracle =
                await ObtenerImpulsadoresOracleAsync(bu);

            var nombresImpulsadores =
                await ObtenerNombresImpulsadoresAsync();

            var grupoFiltro = string.IsNullOrWhiteSpace(filtro.GrupoAgente)
                ? null
                : Normalizar(filtro.GrupoAgente);

            var vendedorDesde = string.IsNullOrWhiteSpace(filtro.VendedorDesde)
                ? null
                : Normalizar(filtro.VendedorDesde);

            var vendedorHasta = string.IsNullOrWhiteSpace(filtro.VendedorHasta)
                ? null
                : Normalizar(filtro.VendedorHasta);

            // Los filtros de cliente contienen XXORA_CUSTOMER_MASTER.IDCLIENTE.
            var clienteDesde = string.IsNullOrWhiteSpace(filtro.ClienteDesde)
                ? null
                : Normalizar(filtro.ClienteDesde);

            var clienteHasta = string.IsNullOrWhiteSpace(filtro.ClienteHasta)
                ? null
                : Normalizar(filtro.ClienteHasta);

            var filas = new List<ImpulsadorClienteFila>();

            foreach (var cobro in clientesCobro)
            {
                var codAgente = (cobro.COD_AGENTE ?? "").Trim();
                var agenteNormalizado = Normalizar(codAgente);

                if (vendedorDesde != null &&
                    string.Compare(
                        agenteNormalizado,
                        vendedorDesde,
                        StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (vendedorHasta != null &&
                    string.Compare(
                        agenteNormalizado,
                        vendedorHasta,
                        StringComparison.OrdinalIgnoreCase) > 0)
                {
                    continue;
                }

                catalogos.Vendedores.TryGetValue(
                    agenteNormalizado,
                    out var agente);

                var grupoCodigo = (agente?.CATEGORIA ?? "").Trim();

                if (grupoFiltro != null &&
                    Normalizar(grupoCodigo) != grupoFiltro)
                {
                    continue;
                }

                var codCliente = (cobro.COD_CLIENTE ?? "").Trim();
                var clienteNormalizado = Normalizar(codCliente);

                if (clienteDesde != null &&
                    string.Compare(
                        clienteNormalizado,
                        clienteDesde,
                        StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (clienteHasta != null &&
                    string.Compare(
                        clienteNormalizado,
                        clienteHasta,
                        StringComparison.OrdinalIgnoreCase) > 0)
                {
                    continue;
                }

                var cliente = BuscarClienteRapido(
                    catalogos,
                    codCliente,
                    codAgente);

                var idClienteImpulsador = clienteNormalizado;

                var fila = new ImpulsadorClienteFila
                {
                    CodAgente = codAgente,
                    NombreAgente =
                        agente?.NOMBRE_VENDEDOR ??
                        codAgente,

                    GrupoCodigo = grupoCodigo,
                    GrupoDescripcion =
                        ObtenerDescripcionGrupo(grupoCodigo),

                    CodCliente = codCliente,
                    NombreCliente =
                        cliente?.NOMBRE_CLIENTE ??
                        cliente?.PARTY_NAME ??
                        codCliente,

                    CobroBruto = ConvertirMontoImpulsador(
                     cobro.COBROBRUTO,
                     filtro.Moneda,
                     filtro.TipoCambio),

                    MontoSinImpuesto = ConvertirMontoImpulsador(
                     cobro.MON_COBRADO,
                     filtro.Moneda,
                     filtro.TipoCambio),

                    MontoComision = ConvertirMontoImpulsador(
                     cobro.MON_COMISION,
                     filtro.Moneda,
                     filtro.TipoCambio)
                };

                empleadosPorCliente.TryGetValue(
                    (
                        Agente: agenteNormalizado,
                        Cliente: clienteNormalizado
                    ),
                    out var empleados);

                foreach (var empleado in
                    empleados ??
                    Enumerable.Empty<CXC_EMPLEADO_COBRO>())
                {
                    var codigoEmpleado =
                        (empleado.EMPLEADO ?? "").Trim();

                    var empleadoNormalizado =
                        Normalizar(codigoEmpleado);

                    /*
                     * COBROSCLIENTE genera CXC_EMPLEADO_COBRO leyendo
                     * IMPULSADORESORACLE por IDCLIENTE + EMPLEADO. Se consulta
                     * esa misma llave para conservar una única fuente de verdad.
                     */
                    impulsadoresOracle.TryGetValue(
                        (
                            Cliente: idClienteImpulsador,
                            Empleado: empleadoNormalizado
                        ),
                        out var configuracionImpulsador);

                    /*
                     * El código del impulsador se resuelve contra PLAEMPLEADO.
                     * Si no existe en el catálogo, se conserva el código como
                     * respaldo para no dejar la columna vacía.
                     */
                    nombresImpulsadores.TryGetValue(
                        empleadoNormalizado,
                        out var nombreImpulsador);

                    fila.Impulsadores.Add(
                        new ImpulsadorDetalleFila
                        {
                            Empleado = codigoEmpleado,

                            NombreEmpleado =
                                !string.IsNullOrWhiteSpace(nombreImpulsador)
                                    ? nombreImpulsador.Trim()
                                    : codigoEmpleado,

                            Porcentaje =
                                configuracionImpulsador?.Porcentaje ??
                                empleado.PORCENTAJE,

                            MontoComision =
                                ConvertirMontoImpulsador(
                                    empleado.MON_COMISION,
                                    filtro.Moneda,
                                    filtro.TipoCambio)
                        });
                }

                filas.Add(fila);
            }

            return filas
                .OrderBy(x => x.CodAgente)
                .ThenBy(x => x.CodCliente)
                .ToList();
        }


        private static List<ImpulsadorDetalleFila>
            ObtenerImpulsadoresParaReporte(
                ImpulsadorClienteFila cliente)
        {
            var impulsadores = cliente.Impulsadores
                .OrderBy(x => x.NombreEmpleado)
                .ThenBy(x => x.Empleado)
                .ToList();

            /*
             * Cuando el cliente no tiene detalle en CXC_EMPLEADO_COBRO,
             * se conserva una línea para que el monto de comisión del
             * resumen CXC_CLIENTE_COBRO no desaparezca del reporte.
             */
            if (!impulsadores.Any())
            {
                impulsadores.Add(
                    new ImpulsadorDetalleFila
                    {
                        Empleado = "",
                        NombreEmpleado = "",
                        Porcentaje = 0m,
                        MontoComision = cliente.MontoComision
                    });
            }

            return impulsadores;
        }

        private static string ObtenerNombreImpulsador(
            ImpulsadorDetalleFila impulsador)
        {
            if (!string.IsNullOrWhiteSpace(
                    impulsador.NombreEmpleado))
            {
                return impulsador.NombreEmpleado.Trim();
            }

            return (impulsador.Empleado ?? "").Trim();
        }


        [HttpGet]
        public async Task<IActionResult> ComisionesImpulsadorPdf(
            ResumenCobrosAgenteFiltroVm filtro,
            ParametrosCalculoComisionVm parametros)
        {
            PrepararFiltro(filtro);
            PrepararParametrosCalculo(filtro, parametros);

            var filas = await EjecutarCalculoYConsultarAsync(
                filtro,
                parametros,
                () => ObtenerComisionesImpulsadorAsync(
                    filtro,
                    parametros));

            QuestPDF.Settings.License =
                QLicenseType.Community;

            var pdfBytes = QDocument.Create(container =>
            {
                container.Page(page =>
                {
                    /*
                     * El reporte de referencia utiliza hoja vertical.
                     * Con seis columnas se mantiene legible y permite
                     * mostrar varios agentes en una misma página.
                     */
                    page.MarginVertical(18);
                    page.MarginHorizontal(22);
                    page.Size(PageSizes.Letter);

                    page.DefaultTextStyle(
                        x => x.FontSize(7));

                    page.Header().Column(header =>
                    {
                        header.Item().Row(row =>
                        {
                            row.RelativeItem();

                            row.RelativeItem(3)
                                .AlignCenter()
                                .Column(col =>
                                {
                                    col.Item()
                                        .AlignCenter()
                                        .Text(
                                            "LANCO & HARRIS MFG. CORP. SRL")
                                        .FontSize(9)
                                        .Bold();

                                    col.Item()
                                        .AlignCenter()
                                        .Text(
                                            "Comisiones por Impulsador y Agente")
                                        .FontSize(8);

                                    col.Item()
                                        .AlignCenter()
                                        .Text(
                                            $"DESDE " +
                                            $"{filtro.FechaDesde:dd/MM/yyyy} " +
                                            $"HASTA " +
                                            $"{filtro.FechaHasta:dd/MM/yyyy}")
                                        .FontSize(8);
                                });

                            row.RelativeItem()
                                .AlignRight()
                                .Column(col =>
                                {
                                    col.Item()
                                        .AlignRight()
                                        .Text(text =>
                                        {
                                            text.Span("Página ");
                                            text.CurrentPageNumber();
                                            text.Span(" / ");
                                            text.TotalPages();
                                        });


                                    col.Item()
                                        .AlignRight()
                                        .Text(
                                            DateTime.Now.ToString(
                                                "dd/MM/yyyy hh:mm tt"))
                                        .FontSize(7);
                                });
                        });

                    });

                    page.Content()
                        .PaddingTop(5)
                        .Table(table =>
                        {
                            /*
                             * Diseño del reporte de referencia:
                             *
                             * Cliente | Monto Cobrado | Monto sin Impuesto
                             * Empleado | % | Monto Comisión
                             */
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2.15f);
                                columns.RelativeColumn(1.25f);
                                columns.RelativeColumn(1.35f);
                                columns.RelativeColumn(2.45f);
                                columns.RelativeColumn(0.55f);
                                columns.RelativeColumn(1.25f);
                            });

                            table.Header(header =>
                            {
                                header.Cell()
                                    .Element(
                                        HeaderStyleImpulsador)
                                    .Text("Cliente")
                                    .FontSize(7);

                                header.Cell()
                                    .Element(
                                        HeaderStyleImpulsador)
                                    .AlignRight()
                                    .Text("Monto\nCobrado")
                                    .FontSize(7);

                                header.Cell()
                                    .Element(
                                        HeaderStyleImpulsador)
                                    .AlignRight()
                                    .Text(
                                        "Monto sin\nImpuesto")
                                    .FontSize(7);

                                header.Cell()
                                    .Element(
                                        HeaderStyleImpulsador)
                                    .Text("Empleado")
                                    .FontSize(7);

                                header.Cell()
                                    .Element(
                                        HeaderStyleImpulsador)
                                    .AlignRight()
                                    .Text("%")
                                    .FontSize(7);

                                header.Cell()
                                    .Element(
                                        HeaderStyleImpulsador)
                                    .AlignRight()
                                    .Text(
                                        "Monto\nComisión")
                                    .FontSize(7);
                            });

                            if (!filas.Any())
                            {
                                table.Cell()
                                    .ColumnSpan(6)
                                    .PaddingTop(15)
                                    .AlignCenter()
                                    .Text(
                                        "No hay datos para los " +
                                        "filtros seleccionados.")
                                    .FontSize(8);

                                return;
                            }

                            var agentes = filas
                                .GroupBy(x => new
                                {
                                    x.CodAgente,
                                    x.NombreAgente
                                })
                                .OrderBy(
                                    x => x.Key.CodAgente)
                                .ToList();

                            foreach (var agente in agentes)
                            {
                                /*
                                 * Encabezado del agente en una sola línea,
                                 * igual al reporte físico.
                                 */
                                table.Cell()
                                    .ColumnSpan(6)
                                    .PaddingTop(8)
                                    .PaddingBottom(3)
                                    .Text(text =>
                                    {
                                        text.Span("Agente     ")
                                            .FontSize(7);

                                        text.Span(
                                                agente.Key.CodAgente)
                                            .Bold()
                                            .FontSize(7);

                                        text.Span("     ")
                                            .FontSize(7);

                                        text.Span(
                                                agente.Key.NombreAgente)
                                            .Bold()
                                            .FontSize(7);
                                    });

                                foreach (var cliente in agente
                                    .OrderBy(
                                        x => x.CodCliente))
                                {
                                    var impulsadores =
                                        ObtenerImpulsadoresParaReporte(
                                            cliente);

                                    /*
                                     * Cada cliente se dibuja como una tabla
                                     * interna indivisible:
                                     *
                                     * 1) Código, montos y primer impulsador.
                                     * 2) Nombre del cliente inmediatamente
                                     *    debajo, junto al segundo impulsador
                                     *    cuando exista.
                                     * 3) Restantes impulsadores.
                                     * 4) Total del cliente, cuando hay más de
                                     *    un impulsador.
                                     *
                                     * ShowEntire evita que el código quede al
                                     * final de una página y el nombre aparezca
                                     * solo al inicio de la siguiente.
                                     */
                                    table.Cell()
                                        .ColumnSpan(6)
                                        .ShowEntire()
                                        .Table(clienteTable =>
                                        {
                                            clienteTable
                                                .ColumnsDefinition(
                                                    columns =>
                                                    {
                                                        columns
                                                            .RelativeColumn(
                                                                2.15f);

                                                        columns
                                                            .RelativeColumn(
                                                                1.25f);

                                                        columns
                                                            .RelativeColumn(
                                                                1.35f);

                                                        columns
                                                            .RelativeColumn(
                                                                2.45f);

                                                        columns
                                                            .RelativeColumn(
                                                                0.55f);

                                                        columns
                                                            .RelativeColumn(
                                                                1.25f);
                                                    });

                                            var primerImpulsador =
                                                impulsadores[0];

                                            /*
                                             * Primera línea del cliente.
                                             */
                                            clienteTable.Cell()
                                                .Element(
                                                    BodyStyleImpulsador)
                                                .Text(
                                                    cliente.CodCliente)
                                                .FontSize(7);

                                            clienteTable.Cell()
                                                .Element(
                                                    BodyStyleImpulsador)
                                                .AlignRight()
                                                .Text(
                                                    FormatoMonto(
                                                        cliente.CobroBruto))
                                                .FontSize(7);

                                            clienteTable.Cell()
                                                .Element(
                                                    BodyStyleImpulsador)
                                                .AlignRight()
                                                .Text(
                                                    FormatoMonto(
                                                        cliente
                                                            .MontoSinImpuesto))
                                                .FontSize(7);

                                            clienteTable.Cell()
                                                .Element(
                                                    BodyStyleImpulsador)
                                                .Text(
                                                    ObtenerNombreImpulsador(
                                                        primerImpulsador))
                                                .FontSize(7);

                                            clienteTable.Cell()
                                                .Element(
                                                    BodyStyleImpulsador)
                                                .AlignRight()
                                                .Text(
                                                    FormatoPorcentaje(
                                                        primerImpulsador
                                                            .Porcentaje))
                                                .FontSize(7);

                                            clienteTable.Cell()
                                                .Element(
                                                    BodyStyleImpulsador)
                                                .AlignRight()
                                                .Text(
                                                    FormatoMonto(
                                                        primerImpulsador
                                                            .MontoComision))
                                                .FontSize(7);

                                            /*
                                             * Segunda línea:
                                             * el nombre siempre aparece aquí,
                                             * no después de todos los empleados.
                                             */
                                            clienteTable.Cell()
                                                .ColumnSpan(3)
                                                .Element(
                                                    BodyStyleImpulsador)
                                                .Text(
                                                    cliente.NombreCliente)
                                                .Bold()
                                                .FontSize(7);

                                            if (impulsadores.Count > 1)
                                            {
                                                var segundoImpulsador =
                                                    impulsadores[1];

                                                clienteTable.Cell()
                                                    .Element(
                                                        BodyStyleImpulsador)
                                                    .Text(
                                                        ObtenerNombreImpulsador(
                                                            segundoImpulsador))
                                                    .FontSize(7);

                                                clienteTable.Cell()
                                                    .Element(
                                                        BodyStyleImpulsador)
                                                    .AlignRight()
                                                    .Text(
                                                        FormatoPorcentaje(
                                                            segundoImpulsador
                                                                .Porcentaje))
                                                    .FontSize(7);

                                                clienteTable.Cell()
                                                    .Element(
                                                        BodyStyleImpulsador)
                                                    .AlignRight()
                                                    .Text(
                                                        FormatoMonto(
                                                            segundoImpulsador
                                                                .MontoComision))
                                                    .FontSize(7);
                                            }
                                            else
                                            {
                                                clienteTable.Cell()
                                                    .Element(
                                                        BodyStyleImpulsador)
                                                    .Text("");

                                                clienteTable.Cell()
                                                    .Element(
                                                        BodyStyleImpulsador)
                                                    .Text("");

                                                /*
                                                 * Con un único impulsador, el
                                                 * total del cliente comparte
                                                 * la línea del nombre, igual
                                                 * al reporte físico.
                                                 */
                                                clienteTable.Cell()
                                                    .Element(
                                                        BodyStyleImpulsador)
                                                    .AlignRight()
                                                    .Text(
                                                        FormatoMonto(
                                                            cliente
                                                                .MontoComision))
                                                    .Bold()
                                                    .FontSize(7);
                                            }

                                            /*
                                             * Tercer impulsador en adelante.
                                             */
                                            for (var i = 2;
                                                 i < impulsadores.Count;
                                                 i++)
                                            {
                                                var impulsador =
                                                    impulsadores[i];

                                                clienteTable.Cell()
                                                    .ColumnSpan(3)
                                                    .Element(
                                                        BodyStyleImpulsador)
                                                    .Text("");

                                                clienteTable.Cell()
                                                    .Element(
                                                        BodyStyleImpulsador)
                                                    .Text(
                                                        ObtenerNombreImpulsador(
                                                            impulsador))
                                                    .FontSize(7);

                                                clienteTable.Cell()
                                                    .Element(
                                                        BodyStyleImpulsador)
                                                    .AlignRight()
                                                    .Text(
                                                        FormatoPorcentaje(
                                                            impulsador
                                                                .Porcentaje))
                                                    .FontSize(7);

                                                clienteTable.Cell()
                                                    .Element(
                                                        BodyStyleImpulsador)
                                                    .AlignRight()
                                                    .Text(
                                                        FormatoMonto(
                                                            impulsador
                                                                .MontoComision))
                                                    .FontSize(7);
                                            }

                                            /*
                                             * Cuando existen varios
                                             * impulsadores, el total del
                                             * cliente se muestra después del
                                             * último empleado, sin repetir el
                                             * nombre abajo del todo.
                                             */
                                            if (impulsadores.Count > 1)
                                            {
                                                clienteTable.Cell()
                                                    .ColumnSpan(5)
                                                    .Element(
                                                        BodyStyleImpulsador)
                                                    .Text("");

                                                clienteTable.Cell()
                                                    .Element(
                                                        BodyStyleImpulsador)
                                                    .AlignRight()
                                                    .Text(
                                                        FormatoMonto(
                                                            cliente
                                                                .MontoComision))
                                                    .Bold()
                                                    .FontSize(7);
                                            }
                                        });
                                }

                                /*
                                 * Total del agente: los montos se suman una
                                 * sola vez por cliente.
                                 */
                                table.Cell()
                                    .Element(
                                        TotalStyleImpulsador)
                                    .Text("Total Agente")
                                    .Bold()
                                    .FontSize(7);

                                table.Cell()
                                    .Element(
                                        TotalStyleImpulsador)
                                    .AlignRight()
                                    .Text(
                                        FormatoMonto(
                                            agente.Sum(
                                                x =>
                                                    x.CobroBruto)))
                                    .Bold()
                                    .FontSize(7);

                                table.Cell()
                                    .Element(
                                        TotalStyleImpulsador)
                                    .AlignRight()
                                    .Text(
                                        FormatoMonto(
                                            agente.Sum(
                                                x =>
                                                    x.MontoSinImpuesto)))
                                    .Bold()
                                    .FontSize(7);

                                table.Cell()
                                    .Element(
                                        TotalStyleImpulsador)
                                    .Text("");

                                table.Cell()
                                    .Element(
                                        TotalStyleImpulsador)
                                    .Text("");

                                table.Cell()
                                    .Element(
                                        TotalStyleImpulsador)
                                    .AlignRight()
                                    .Text(
                                        FormatoMonto(
                                            agente.Sum(
                                                x =>
                                                    x.MontoComision)))
                                    .Bold()
                                    .FontSize(7);
                            }

                            table.Cell()
                                .Element(
                                    TotalGeneralStyleImpulsador)
                                .Text("Total General")
                                .Bold()
                                .FontSize(7);

                            table.Cell()
                                .Element(
                                    TotalGeneralStyleImpulsador)
                                .AlignRight()
                                .Text(
                                    FormatoMonto(
                                        filas.Sum(
                                            x =>
                                                x.CobroBruto)))
                                .Bold()
                                .FontSize(7);

                            table.Cell()
                                .Element(
                                    TotalGeneralStyleImpulsador)
                                .AlignRight()
                                .Text(
                                    FormatoMonto(
                                        filas.Sum(
                                            x =>
                                                x.MontoSinImpuesto)))
                                .Bold()
                                .FontSize(7);

                            table.Cell()
                                .Element(
                                    TotalGeneralStyleImpulsador)
                                .Text("");

                            table.Cell()
                                .Element(
                                    TotalGeneralStyleImpulsador)
                                .Text("");

                            table.Cell()
                                .Element(
                                    TotalGeneralStyleImpulsador)
                                .AlignRight()
                                .Text(
                                    FormatoMonto(
                                        filas.Sum(
                                            x =>
                                                x.MontoComision)))
                                .Bold()
                                .FontSize(7);
                        });
                });
            }).GeneratePdf();

            var fileName =
                $"Comisiones_Impulsador_Agente_" +
                $"{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

            return File(
                pdfBytes,
                "application/pdf",
                fileName);
        }


        [HttpGet]
        public async Task<IActionResult> ComisionesImpulsadorExcel(
            ResumenCobrosAgenteFiltroVm filtro,
            ParametrosCalculoComisionVm parametros)
        {
            PrepararFiltro(filtro);
            PrepararParametrosCalculo(filtro, parametros);

            var filas = await EjecutarCalculoYConsultarAsync(
                filtro,
                parametros,
                () => ObtenerComisionesImpulsadorAsync(
                    filtro,
                    parametros));

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Impulsadores");

            var row = 1;

            ws.Cell(row, 1).Value =
                "LANCO & HARRIS MFG. CORP. SRL";
            ws.Range(row, 1, row, 6).Merge();
            ws.Range(row, 1, row, 6)
                .Style.Alignment.Horizontal =
                    XLAlignmentHorizontalValues.Center;
            ws.Range(row, 1, row, 6)
                .Style.Font.Bold = true;
            row++;

            ws.Cell(row, 1).Value =
                "Comisiones por Impulsador y Agente";
            ws.Range(row, 1, row, 6).Merge();
            ws.Range(row, 1, row, 6)
                .Style.Alignment.Horizontal =
                    XLAlignmentHorizontalValues.Center;
            row++;

            ws.Cell(row, 1).Value =
                $"DESDE {filtro.FechaDesde:dd/MM/yyyy} " +
                $"HASTA {filtro.FechaHasta:dd/MM/yyyy}";

            ws.Range(row, 1, row, 4).Merge();

            ws.Cell(row, 5).Value = "Generado:";
            ws.Cell(row, 6).Value =
                DateTime.Now.ToString(
                    "dd/MM/yyyy hh:mm tt");

            row += 2;

            ws.Cell(row, 1).Value = "Cliente";
            ws.Cell(row, 2).Value = "Monto Cobrado";
            ws.Cell(row, 3).Value =
                "Monto sin Impuesto";
            ws.Cell(row, 4).Value = "Empleado";
            ws.Cell(row, 5).Value = "%";
            ws.Cell(row, 6).Value =
                "Monto Comisión";

            ws.Range(row, 1, row, 6)
                .Style.Font.Bold = true;

            ws.Range(row, 1, row, 6)
                .Style.Alignment.WrapText = true;

            row++;

            if (!filas.Any())
            {
                ws.Cell(row, 1).Value =
                    "No hay datos para los filtros seleccionados.";

                ws.Range(row, 1, row, 6).Merge();

                ws.Range(row, 1, row, 6)
                    .Style.Alignment.Horizontal =
                        XLAlignmentHorizontalValues.Center;
            }
            else
            {
                var agentes = filas
                    .GroupBy(x => new
                    {
                        x.CodAgente,
                        x.NombreAgente
                    })
                    .OrderBy(
                        x => x.Key.CodAgente)
                    .ToList();

                foreach (var agente in agentes)
                {
                    row++;

                    ws.Cell(row, 1).Value = "Agente";
                    ws.Cell(row, 2).Value =
                        agente.Key.CodAgente;

                    ws.Cell(row, 3).Value =
                        agente.Key.NombreAgente;

                    ws.Range(row, 3, row, 6).Merge();

                    ws.Range(row, 1, row, 6)
                        .Style.Font.Bold = true;

                    row++;

                    foreach (var cliente in agente
                        .OrderBy(
                            x => x.CodCliente))
                    {
                        var impulsadores =
                            ObtenerImpulsadoresParaReporte(
                                cliente);

                        var primerImpulsador =
                            impulsadores[0];

                        /*
                         * Primera línea: código, montos y primer
                         * impulsador.
                         */
                        ws.Cell(row, 1).Value =
                            cliente.CodCliente;

                        ws.Cell(row, 2).Value =
                            cliente.CobroBruto;

                        ws.Cell(row, 3).Value =
                            cliente.MontoSinImpuesto;

                        ws.Cell(row, 4).Value =
                            ObtenerNombreImpulsador(
                                primerImpulsador);

                        ws.Cell(row, 5).Value =
                            primerImpulsador.Porcentaje;

                        ws.Cell(row, 6).Value =
                            primerImpulsador.MontoComision;

                        row++;

                        /*
                         * Segunda línea: nombre del cliente y segundo
                         * impulsador, cuando exista.
                         */
                        ws.Cell(row, 1).Value =
                            cliente.NombreCliente;

                        ws.Range(row, 1, row, 3).Merge();
                        ws.Range(row, 1, row, 3)
                            .Style.Font.Bold = true;

                        if (impulsadores.Count > 1)
                        {
                            var segundoImpulsador =
                                impulsadores[1];

                            ws.Cell(row, 4).Value =
                                ObtenerNombreImpulsador(
                                    segundoImpulsador);

                            ws.Cell(row, 5).Value =
                                segundoImpulsador.Porcentaje;

                            ws.Cell(row, 6).Value =
                                segundoImpulsador.MontoComision;
                        }
                        else
                        {
                            ws.Cell(row, 6).Value =
                                cliente.MontoComision;

                            ws.Cell(row, 6)
                                .Style.Font.Bold = true;
                        }

                        row++;

                        /*
                         * Tercer impulsador en adelante.
                         */
                        for (var i = 2;
                             i < impulsadores.Count;
                             i++)
                        {
                            var impulsador =
                                impulsadores[i];

                            ws.Cell(row, 4).Value =
                                ObtenerNombreImpulsador(
                                    impulsador);

                            ws.Cell(row, 5).Value =
                                impulsador.Porcentaje;

                            ws.Cell(row, 6).Value =
                                impulsador.MontoComision;

                            row++;
                        }

                        /*
                         * Total del cliente con varios impulsadores.
                         * El nombre ya quedó inmediatamente debajo del
                         * código, por lo que aquí no se repite.
                         */
                        if (impulsadores.Count > 1)
                        {
                            ws.Cell(row, 6).Value =
                                cliente.MontoComision;

                            ws.Cell(row, 6)
                                .Style.Font.Bold = true;

                            row++;
                        }
                    }

                    ws.Cell(row, 1).Value =
                        "Total Agente";

                    ws.Cell(row, 2).Value =
                        agente.Sum(
                            x => x.CobroBruto);

                    ws.Cell(row, 3).Value =
                        agente.Sum(
                            x => x.MontoSinImpuesto);

                    ws.Cell(row, 6).Value =
                        agente.Sum(
                            x => x.MontoComision);

                    ws.Range(row, 1, row, 6)
                        .Style.Font.Bold = true;

                    row += 2;
                }

                ws.Cell(row, 1).Value =
                    "Total General";

                ws.Cell(row, 2).Value =
                    filas.Sum(
                        x => x.CobroBruto);

                ws.Cell(row, 3).Value =
                    filas.Sum(
                        x => x.MontoSinImpuesto);

                ws.Cell(row, 6).Value =
                    filas.Sum(
                        x => x.MontoComision);

                ws.Range(row, 1, row, 6)
                    .Style.Font.Bold = true;

            }

            ws.SheetView.FreezeRows(5);

            ws.Column(1).Width = 40;
            ws.Column(2).Width = 20;
            ws.Column(3).Width = 22;
            ws.Column(4).Width = 38;
            ws.Column(5).Width = 10;
            ws.Column(6).Width = 20;

            ws.Column(2).Style.NumberFormat.Format =
                "#,##0.00";
            ws.Column(3).Style.NumberFormat.Format =
                "#,##0.00";
            ws.Column(5).Style.NumberFormat.Format =
                "0.##";
            ws.Column(6).Style.NumberFormat.Format =
                "#,##0.00";

            ws.Columns().Style.Alignment.Vertical =
                XLAlignmentVerticalValues.Center;

            var stream = new MemoryStream();

            var fileName =
                $"Comisiones_Impulsador_Agente_" +
                $"{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

            wb.SaveAs(stream);
            stream.Position = 0;

            return File(
                stream,
                "application/vnd.openxmlformats-officedocument." +
                "spreadsheetml.sheet",
                fileName);
        }


        private static IContainer HeaderStyleImpulsador(
            IContainer container)
        {
            /*
             * Sin línea inferior en los títulos de columnas.
             */
            return container
                .PaddingBottom(3)
                .PaddingHorizontal(2);
        }

        private static IContainer BodyStyleImpulsador(
            IContainer container)
        {
            return container
                .PaddingVertical(1.1f)
                .PaddingHorizontal(2);
        }

        private static IContainer TotalStyleImpulsador(
            IContainer container)
        {
            /*
             * Sin línea superior en Total Agente.
             */
            return container
                .PaddingTop(3)
                .PaddingBottom(2)
                .PaddingHorizontal(2);
        }

        private static IContainer TotalGeneralStyleImpulsador(
            IContainer container)
        {
            return container
                .PaddingTop(3)
                .PaddingBottom(2)
                .PaddingHorizontal(2);
        }


        private static IContainer HeaderStyleDetalle(IContainer container)
        {
            return container
                .PaddingBottom(3)
                .BorderBottom(0.5f)
                .PaddingHorizontal(2);
        }

        private static IContainer BodyStyleDetalle(IContainer container)
        {
            return container
                .PaddingVertical(1.2f)
                .PaddingHorizontal(2);
        }

        private static string FormatoFecha(DateTime? fecha)
        {
            return fecha.HasValue
                ? fecha.Value.ToString("dd/MM/yyyy")
                : "";
        }

        private static string FormatoPorcentaje(decimal porcentaje)
        {
            var culture = new CultureInfo("es-CR");
            return porcentaje.ToString("0.##", culture);
        }

    }
}