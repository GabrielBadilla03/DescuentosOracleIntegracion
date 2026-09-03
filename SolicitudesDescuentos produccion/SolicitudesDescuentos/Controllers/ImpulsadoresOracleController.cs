using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SolicitudesDescuentos.Data;
using SolicitudesDescuentos.ModelsOracle;
using SolicitudesDescuentos.ModelsOracle.ViewModels.Impulsadores;

namespace SolicitudesDescuentos.Controllers
{
    public class ImpulsadoresOracleController : Controller
    {
        private const string BuFija = "LANCO_CR";
        private const string CiaLanco = "001";
        private const string SitioCliente = "SHIP_TO";

        private readonly OracleContext _oracleContext;
        private readonly LancoDbContext _lancoContext;

        public ImpulsadoresOracleController(
            OracleContext oracleContext,
            LancoDbContext lancoContext)
        {
            _oracleContext = oracleContext;
            _lancoContext = lancoContext;
        }

        public sealed class ActualizarPorcentajeRequest
        {
            public string? Cliente { get; set; }
            public string? Empleado { get; set; }
            public decimal Porcentaje { get; set; }
        }

        public sealed class EliminarImpulsadorRequest
        {
            public string? Cliente { get; set; }
            public string? Empleado { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string? clienteDesde,
            string? clienteHasta,
            string? empleadoDesde,
            string? empleadoHasta,
            string? agenteVenta)
        {
            var edicion = new ImpulsadorOracleEdicionVm();

            var vm = await ConstruirVistaAsync(
                clienteDesde,
                clienteHasta,
                empleadoDesde,
                empleadoHasta,
                agenteVenta,
                edicion);

            ViewBag.PorcentajeTexto = "";
            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> BuscarEmpleados(string? filtro)
        {
            var texto = Normalizar(filtro);

            var consulta = _lancoContext.PLAEMPLEADOs
                .AsNoTracking()
                .Where(x => x.CIA == CiaLanco);

            if (!string.IsNullOrWhiteSpace(texto))
            {
                consulta = consulta.Where(x =>
                    x.EMPLEADO.ToUpper().Contains(texto) ||
                    x.NOMBRE.ToUpper().Contains(texto));
            }

            var empleados = await consulta
                .OrderBy(x => x.NOMBRE)
                .ThenBy(x => x.EMPLEADO)
                .Select(x => new
                {
                    codigo = x.EMPLEADO,
                    nombre = x.NOMBRE
                })
                .Take(50)
                .ToListAsync();

            var resultado = empleados
                .Select(x => new
                {
                    codigo = (x.codigo ?? "").Trim(),
                    nombre = (x.nombre ?? "").Trim()
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.codigo))
                .ToList();

            return Json(resultado);
        }

        [HttpGet]
        public async Task<IActionResult> BuscarClientes(string? filtro)
        {
            var texto = Normalizar(filtro);

            var consulta = _oracleContext.XXORA_CUSTOMER_MASTERs
                .AsNoTracking()
                .Where(x =>
                    x.BU_NOMBRE == BuFija &&
                    x.SITIO == SitioCliente &&
                    x.IDCLIENTE != null);

            if (!string.IsNullOrWhiteSpace(texto))
            {
                consulta = consulta.Where(x =>
                    x.IDCLIENTE!.ToUpper().Contains(texto) ||
                    (x.NOMBRE_CLIENTE != null &&
                     x.NOMBRE_CLIENTE.ToUpper().Contains(texto)));
            }

            var clientes = await consulta
                .OrderBy(x => x.IDCLIENTE)
                .Select(x => new
                {
                    codigo = x.IDCLIENTE,
                    nombre = x.NOMBRE_CLIENTE
                })
                .Take(200)
                .ToListAsync();

            var resultado = clientes
                .Where(x => !string.IsNullOrWhiteSpace(x.codigo))
                .GroupBy(
                    x => Normalizar(x.codigo),
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    codigo = (g.First().codigo ?? "").Trim(),
                    nombre = g
                        .Select(x => (x.nombre ?? "").Trim())
                        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? ""
                })
                .OrderBy(x => x.codigo)
                .Take(50)
                .ToList();

            return Json(resultado);
        }

        [HttpGet]
        public async Task<IActionResult> BuscarAgentes(string? filtro)
        {
            var q = Normalizar(filtro);

            var vendedores = await _oracleContext.GEN_VENDEDORs
                .AsNoTracking()
                .Where(x => x.BU_NOMBRE == BuFija)
                .Where(x =>
                    string.IsNullOrEmpty(q)
                    || (x.IDVENDEDOR != null &&
                        x.IDVENDEDOR.ToUpper().Contains(q))
                    || (x.REGISTRY_ID != null &&
                        x.REGISTRY_ID.ToUpper().Contains(q))
                    || (x.NOMBRE_VENDEDOR != null &&
                        x.NOMBRE_VENDEDOR.ToUpper().Contains(q)))
                    .OrderBy(x => x.IDVENDEDOR)
                    .ThenBy(x => x.REGISTRY_ID)
                    .Take(50)
                    .ToListAsync();

            var resultado = vendedores
                .Select(x => new
                {
                    codigo = (x.IDVENDEDOR ?? x.REGISTRY_ID ?? "").Trim(),
                    nombre = (x.NOMBRE_VENDEDOR ?? "").Trim(),
                    categoria = (x.CATEGORIA ?? "").Trim()
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.codigo))
                .GroupBy(
                    x => Normalizar(x.codigo),
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.codigo)
                .ToList();

            return Json(resultado);
        }

        // Este formulario se usa únicamente para agregar registros nuevos.
        // La edición de registros existentes se realiza en línea con
        // ActualizarPorcentaje, sin recargar la página.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Guardar(
            ImpulsadoresOracleIndexVm vista,
            string? porcentajeTexto)
        {
            var modelo = vista.Edicion ?? new ImpulsadorOracleEdicionVm();

            modelo.Cliente = Normalizar(modelo.Cliente);
            modelo.Empleado = Normalizar(modelo.Empleado);

            if (!TryParsePorcentaje(porcentajeTexto, out var porcentaje))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Ingrese un porcentaje válido. Puede utilizar punto o coma decimal.");
            }
            else
            {
                modelo.Porcentaje = porcentaje;

                if (porcentaje < 0 || porcentaje > 100)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "El porcentaje debe estar entre 0 y 100.");
                }
            }

            if (string.IsNullOrWhiteSpace(modelo.Cliente))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Debe seleccionar un cliente.");
            }

            if (string.IsNullOrWhiteSpace(modelo.Empleado))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Debe seleccionar un empleado.");
            }

            var empleadoValido = false;
            var clienteValido = false;

            if (!string.IsNullOrWhiteSpace(modelo.Empleado))
            {
                empleadoValido = await _lancoContext.PLAEMPLEADOs
                    .AsNoTracking()
                    .AnyAsync(x =>
                        x.CIA == CiaLanco &&
                        x.EMPLEADO == modelo.Empleado);

                if (!empleadoValido)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "El empleado seleccionado no existe en PLAEMPLEADO para la compañía 001.");
                }
            }

            if (!string.IsNullOrWhiteSpace(modelo.Cliente))
            {
                clienteValido = await _oracleContext.XXORA_CUSTOMER_MASTERs
                    .AsNoTracking()
                    .AnyAsync(x =>
                        x.BU_NOMBRE == BuFija &&
                        x.SITIO == SitioCliente &&
                        x.IDCLIENTE == modelo.Cliente);

                if (!clienteValido)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "El cliente seleccionado no existe como SHIP_TO en XXORA_CUSTOMER_MASTER.");
                }
            }

            if (empleadoValido)
            {
                modelo.NombreEmpleado = await _lancoContext.PLAEMPLEADOs
                    .AsNoTracking()
                    .Where(x =>
                        x.CIA == CiaLanco &&
                        x.EMPLEADO == modelo.Empleado)
                    .Select(x => x.NOMBRE)
                    .FirstOrDefaultAsync() ?? "";

                modelo.NombreEmpleado = modelo.NombreEmpleado.Trim();
            }

            if (clienteValido)
            {
                modelo.NombreCliente = await _oracleContext.XXORA_CUSTOMER_MASTERs
                    .AsNoTracking()
                    .Where(x =>
                        x.BU_NOMBRE == BuFija &&
                        x.SITIO == SitioCliente &&
                        x.IDCLIENTE == modelo.Cliente)
                    .Select(x => x.NOMBRE_CLIENTE)
                    .FirstOrDefaultAsync() ?? "";

                modelo.NombreCliente = modelo.NombreCliente.Trim();
            }

            if (!ModelState.IsValid)
            {
                ViewBag.PorcentajeTexto = porcentajeTexto ?? "";

                var vmError = await ConstruirVistaAsync(
                    null,
                    null,
                    null,
                    null,
                    null,
                    modelo);

                return View("Index", vmError);
            }

            var existe = await _oracleContext.IMPULSADORESORACLEs
                .AnyAsync(x =>
                    x.BU_NOMBRE == BuFija &&
                    x.CLIENTE == modelo.Cliente &&
                    x.EMPLEADO == modelo.Empleado);

            if (existe)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Ya existe un registro para ese cliente y empleado. Edite el porcentaje directamente en la tabla.");

                ViewBag.PorcentajeTexto = porcentajeTexto ?? "";

                var vmDuplicado = await ConstruirVistaAsync(
                    null,
                    null,
                    null,
                    null,
                    null,
                    modelo);

                return View("Index", vmDuplicado);
            }

            try
            {
                _oracleContext.IMPULSADORESORACLEs.Add(
                    new IMPULSADORESORACLE
                    {
                        BU_NOMBRE = BuFija,
                        CLIENTE = modelo.Cliente,
                        EMPLEADO = modelo.Empleado,
                        PORCENTAJE = modelo.Porcentaje
                    });

                await _oracleContext.SaveChangesAsync();

                TempData["Exito"] =
                    "El impulsador se agregó correctamente.";

                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateException ex)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "No se pudo guardar el registro en IMPULSADORESORACLE. " +
                    ex.GetBaseException().Message);

                ViewBag.PorcentajeTexto = porcentajeTexto ?? "";

                var vmErrorBd = await ConstruirVistaAsync(
                    null,
                    null,
                    null,
                    null,
                    null,
                    modelo);

                return View("Index", vmErrorBd);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActualizarPorcentaje(
            [FromBody] ActualizarPorcentajeRequest solicitud)
        {
            var cliente = Normalizar(solicitud.Cliente);
            var empleado = Normalizar(solicitud.Empleado);

            if (string.IsNullOrWhiteSpace(cliente) ||
                string.IsNullOrWhiteSpace(empleado))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El cliente y el empleado son obligatorios."
                });
            }

            if (solicitud.Porcentaje < 0 || solicitud.Porcentaje > 100)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El porcentaje debe estar entre 0 y 100."
                });
            }

            var registro = await _oracleContext.IMPULSADORESORACLEs
                .SingleOrDefaultAsync(x =>
                    x.BU_NOMBRE == BuFija &&
                    x.CLIENTE == cliente &&
                    x.EMPLEADO == empleado);

            if (registro == null)
            {
                return NotFound(new
                {
                    ok = false,
                    mensaje = "El registro ya no existe."
                });
            }

            registro.PORCENTAJE = decimal.Round(
                solicitud.Porcentaje,
                2,
                MidpointRounding.AwayFromZero);

            try
            {
                await _oracleContext.SaveChangesAsync();

                return Json(new
                {
                    ok = true,
                    mensaje = "Porcentaje guardado.",
                    porcentaje = registro.PORCENTAJE.ToString(
                        "0.00",
                        CultureInfo.InvariantCulture)
                });
            }
            catch (DbUpdateException ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje =
                        "No se pudo actualizar el porcentaje. " +
                        ex.GetBaseException().Message
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarAjax(
            [FromBody] EliminarImpulsadorRequest solicitud)
        {
            var cliente = Normalizar(solicitud.Cliente);
            var empleado = Normalizar(solicitud.Empleado);

            var registro = await _oracleContext.IMPULSADORESORACLEs
                .SingleOrDefaultAsync(x =>
                    x.BU_NOMBRE == BuFija &&
                    x.CLIENTE == cliente &&
                    x.EMPLEADO == empleado);

            if (registro == null)
            {
                return NotFound(new
                {
                    ok = false,
                    mensaje = "El registro que intentó eliminar ya no existe."
                });
            }

            try
            {
                _oracleContext.IMPULSADORESORACLEs.Remove(registro);
                await _oracleContext.SaveChangesAsync();

                return Json(new
                {
                    ok = true,
                    mensaje =
                        $"Se eliminó el empleado {empleado} del cliente {cliente}."
                });
            }
            catch (DbUpdateException ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje =
                        "No se pudo eliminar el registro. " +
                        ex.GetBaseException().Message
                });
            }
        }

        private async Task<ImpulsadoresOracleIndexVm> ConstruirVistaAsync(
            string? clienteDesde,
            string? clienteHasta,
            string? empleadoDesde,
            string? empleadoHasta,
            string? agenteVenta,
            ImpulsadorOracleEdicionVm edicion)
        {
            var configuraciones = await _oracleContext.IMPULSADORESORACLEs
                .AsNoTracking()
                .Where(x => x.BU_NOMBRE == BuFija)
                .OrderBy(x => x.CLIENTE)
                .ThenBy(x => x.EMPLEADO)
                .ToListAsync();

            var codigosEmpleados = configuraciones
                .Select(x => Normalizar(x.EMPLEADO))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var codigoFiltro in new[]
            {
                NormalizarONull(empleadoDesde),
                NormalizarONull(empleadoHasta)
            })
            {
                if (!string.IsNullOrWhiteSpace(codigoFiltro))
                {
                    codigosEmpleados.Add(codigoFiltro);
                }
            }

            var empleados = await _lancoContext.PLAEMPLEADOs
                .AsNoTracking()
                .Where(x => x.CIA == CiaLanco)
                .Select(x => new
                {
                    x.EMPLEADO,
                    x.NOMBRE
                })
                .ToListAsync();

            var nombresEmpleados = empleados
                .Where(x => codigosEmpleados.Contains(Normalizar(x.EMPLEADO)))
                .GroupBy(
                    x => Normalizar(x.EMPLEADO),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => (g.First().NOMBRE ?? "").Trim(),
                    StringComparer.OrdinalIgnoreCase);

            var codigosClientes = configuraciones
                .Select(x => Normalizar(x.CLIENTE))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var codigoFiltro in new[]
            {
                NormalizarONull(clienteDesde),
                NormalizarONull(clienteHasta)
            })
            {
                if (!string.IsNullOrWhiteSpace(codigoFiltro))
                {
                    codigosClientes.Add(codigoFiltro);
                }
            }

            var clientes = await _oracleContext.XXORA_CUSTOMER_MASTERs
                .AsNoTracking()
                .Where(x =>
                    x.BU_NOMBRE == BuFija &&
                    x.SITIO == SitioCliente &&
                    x.IDCLIENTE != null)
                .Select(x => new
                {
                    x.IDCLIENTE,
                    x.NOMBRE_CLIENTE
                })
                .ToListAsync();

            var nombresClientes = clientes
                .Where(x => codigosClientes.Contains(Normalizar(x.IDCLIENTE)))
                .GroupBy(
                    x => Normalizar(x.IDCLIENTE),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .Select(x => (x.NOMBRE_CLIENTE ?? "").Trim())
                        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "",
                    StringComparer.OrdinalIgnoreCase);

            var agentesPorCliente =
                await ObtenerAgentesPorClienteAsync();

            var filas = configuraciones
                .Select(x =>
                {
                    var cliente = Normalizar(x.CLIENTE);
                    var empleado = Normalizar(x.EMPLEADO);

                    nombresClientes.TryGetValue(
                        cliente,
                        out var nombreCliente);

                    nombresEmpleados.TryGetValue(
                        empleado,
                        out var nombreEmpleado);

                    return new ImpulsadorOracleFilaVm
                    {
                        Cliente = cliente,
                        NombreCliente = nombreCliente ?? "",
                        Empleado = empleado,
                        NombreEmpleado = nombreEmpleado ?? "",
                        Porcentaje = x.PORCENTAJE
                    };
                })
                .ToList();

            var clienteDesdeNormalizado = NormalizarONull(clienteDesde);
            var clienteHastaNormalizado = NormalizarONull(clienteHasta);
            var empleadoDesdeNormalizado = NormalizarONull(empleadoDesde);
            var empleadoHastaNormalizado = NormalizarONull(empleadoHasta);
            var agenteVentaNormalizado = NormalizarONull(agenteVenta);

            filas = filas
                .Where(x => EstaDentroDelRango(
                    x.Cliente,
                    clienteDesdeNormalizado,
                    clienteHastaNormalizado))
                .Where(x => EstaDentroDelRango(
                    x.Empleado,
                    empleadoDesdeNormalizado,
                    empleadoHastaNormalizado))
                .Where(x =>
                {
                    if (string.IsNullOrWhiteSpace(agenteVentaNormalizado))
                        return true;

                    return agentesPorCliente.TryGetValue(
                               Normalizar(x.Cliente),
                               out var agenteCliente) &&
                           string.Equals(
                               agenteCliente,
                               agenteVentaNormalizado,
                               StringComparison.OrdinalIgnoreCase);
                })

                // 1. Primero ordena alfabéticamente por nombre del cliente
                .OrderBy(
                    x => x.NombreCliente,
                    StringComparer.OrdinalIgnoreCase)

                // 2. Si el nombre es igual, ordena por código de cliente
                .ThenBy(
                    x => x.Cliente,
                    StringComparer.OrdinalIgnoreCase)

                // 3. Dentro de cada cliente, ordena por nombre del empleado
                .ThenBy(
                    x => x.NombreEmpleado,
                    StringComparer.OrdinalIgnoreCase)

                // 4. Desempate por código del empleado
                .ThenBy(
                    x => x.Empleado,
                    StringComparer.OrdinalIgnoreCase)

                .ToList();

            nombresClientes.TryGetValue(
                clienteDesdeNormalizado ?? "",
                out var clienteDesdeNombre);

            nombresClientes.TryGetValue(
                clienteHastaNormalizado ?? "",
                out var clienteHastaNombre);

            nombresEmpleados.TryGetValue(
                empleadoDesdeNormalizado ?? "",
                out var empleadoDesdeNombre);

            nombresEmpleados.TryGetValue(
                empleadoHastaNormalizado ?? "",
                out var empleadoHastaNombre);

            ViewBag.ClienteDesde = clienteDesde?.Trim() ?? "";
            ViewBag.ClienteDesdeNombre = clienteDesdeNombre ?? "";
            ViewBag.ClienteHasta = clienteHasta?.Trim() ?? "";
            ViewBag.ClienteHastaNombre = clienteHastaNombre ?? "";
            ViewBag.EmpleadoDesde = empleadoDesde?.Trim() ?? "";
            ViewBag.EmpleadoDesdeNombre = empleadoDesdeNombre ?? "";
            ViewBag.EmpleadoHasta = empleadoHasta?.Trim() ?? "";
            ViewBag.EmpleadoHastaNombre = empleadoHastaNombre ?? "";

            var agenteVentaNombre = "";

            if (!string.IsNullOrWhiteSpace(agenteVentaNormalizado))
            {
                var vendedores = await _oracleContext.GEN_VENDEDORs
                    .AsNoTracking()
                    .Where(x => x.BU_NOMBRE == BuFija)
                    .Select(x => new
                    {
                        x.IDVENDEDOR,
                        x.REGISTRY_ID,
                        x.NOMBRE_VENDEDOR
                    })
                    .ToListAsync();

                agenteVentaNombre = vendedores
                    .Where(x =>
                        string.Equals(
                            Normalizar(x.IDVENDEDOR),
                            agenteVentaNormalizado,
                            StringComparison.OrdinalIgnoreCase)
                        || string.Equals(
                            Normalizar(x.REGISTRY_ID),
                            agenteVentaNormalizado,
                            StringComparison.OrdinalIgnoreCase))
                    .Select(x => (x.NOMBRE_VENDEDOR ?? "").Trim())
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
            }

            ViewBag.AgenteVenta = agenteVenta?.Trim() ?? "";
            ViewBag.AgenteVentaNombre = agenteVentaNombre;
            ViewBag.AgentesPorCliente = agentesPorCliente;

            return new ImpulsadoresOracleIndexVm
            {
                Filtro = "",
                Edicion = edicion,
                Registros = filas
            };
        }

        private async Task<Dictionary<string, string>>
            ObtenerAgentesPorClienteAsync()
        {
            var resultado = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            var conexion = _oracleContext.Database.GetDbConnection();
            var cerrarConexion = conexion.State != ConnectionState.Open;

            try
            {
                if (cerrarConexion)
                    await conexion.OpenAsync();

                using var comando = conexion.CreateCommand();
                comando.CommandType = CommandType.Text;
                comando.CommandText = @"
                    SELECT IDCLIENTE,
                           COD_AGENTE
                      FROM
                      (
                          SELECT TRIM(C.IDCLIENTE) AS IDCLIENTE,
                                 NVL(
                                     TRIM(C.IDVENDEDOR),
                                     TRIM(C.VENDEDOR)
                                 ) AS COD_AGENTE,
                                 ROW_NUMBER() OVER
                                 (
                                     PARTITION BY TRIM(UPPER(C.IDCLIENTE))
                                     ORDER BY
                                         CASE
                                             WHEN TRIM(UPPER(C.PARTY_SITE_PRIMARY_FLAG)) = 'Y'
                                             THEN 0
                                             ELSE 1
                                         END,
                                         C.SITE_LAST_UPDATE_DATE DESC NULLS LAST,
                                         C.ROWID
                                 ) AS RN
                            FROM BG_INTUSER.XXORA_CUSTOMER_MASTER C
                           WHERE TRIM(UPPER(C.BU_NOMBRE)) = :P_BU
                             AND TRIM(UPPER(C.SITIO)) = :P_SITIO
                             AND C.IDCLIENTE IS NOT NULL
                             AND NVL(
                                     TRIM(C.IDVENDEDOR),
                                     TRIM(C.VENDEDOR)
                                 ) IS NOT NULL
                      )
                     WHERE RN = 1";

                var bindByName = comando
                    .GetType()
                    .GetProperty("BindByName");

                if (bindByName?.CanWrite == true)
                    bindByName.SetValue(comando, true);

                AgregarParametro(
                    comando,
                    "P_BU",
                    BuFija,
                    DbType.String);

                AgregarParametro(
                    comando,
                    "P_SITIO",
                    SitioCliente,
                    DbType.String);

                using var lector = await comando.ExecuteReaderAsync();

                var ordinalCliente = lector.GetOrdinal("IDCLIENTE");
                var ordinalAgente = lector.GetOrdinal("COD_AGENTE");

                while (await lector.ReadAsync())
                {
                    var cliente = lector.IsDBNull(ordinalCliente)
                        ? ""
                        : lector.GetString(ordinalCliente).Trim();

                    var agente = lector.IsDBNull(ordinalAgente)
                        ? ""
                        : lector.GetString(ordinalAgente).Trim();

                    if (string.IsNullOrWhiteSpace(cliente) ||
                        string.IsNullOrWhiteSpace(agente))
                    {
                        continue;
                    }

                    resultado[Normalizar(cliente)] =
                        Normalizar(agente);
                }

                return resultado;
            }
            finally
            {
                if (cerrarConexion &&
                    conexion.State == ConnectionState.Open)
                {
                    await conexion.CloseAsync();
                }
            }
        }


        private static void AgregarParametro(
            DbCommand comando,
            string nombre,
            object? valor,
            DbType tipo)
        {
            var parametro = comando.CreateParameter();
            parametro.ParameterName = nombre;
            parametro.DbType = tipo;
            parametro.Value = valor ?? DBNull.Value;
            comando.Parameters.Add(parametro);
        }

        private static bool EstaDentroDelRango(
            string? valor,
            string? desde,
            string? hasta)
        {
            var valorNormalizado = Normalizar(valor);

            if (!string.IsNullOrWhiteSpace(desde) &&
                string.Compare(
                    valorNormalizado,
                    desde,
                    StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(hasta) &&
                string.Compare(
                    valorNormalizado,
                    hasta,
                    StringComparison.OrdinalIgnoreCase) > 0)
            {
                return false;
            }

            return true;
        }

        private static bool TryParsePorcentaje(
            string? valor,
            out decimal porcentaje)
        {
            porcentaje = 0;

            var texto = (valor ?? "")
                .Trim()
                .Replace(" ", "")
                .Replace(',', '.');

            return decimal.TryParse(
                texto,
                NumberStyles.AllowLeadingSign |
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out porcentaje);
        }

        private static string Normalizar(string? valor)
        {
            return (valor ?? "").Trim().ToUpperInvariant();
        }

        private static string? NormalizarONull(string? valor)
        {
            var normalizado = Normalizar(valor);

            return string.IsNullOrWhiteSpace(normalizado)
                ? null
                : normalizado;
        }
    }
}
