using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SolicitudesDescuentos.ModelsOracle.ViewModels.Impulsadores
{
    public sealed class ImpulsadorOracleEdicionVm
    {
        [Required(ErrorMessage = "Debe seleccionar un cliente.")]
        [StringLength(30)]
        public string Cliente { get; set; } = "";

        public string NombreCliente { get; set; } = "";

        [Required(ErrorMessage = "Debe seleccionar un empleado.")]
        [StringLength(15)]
        public string Empleado { get; set; } = "";

        public string NombreEmpleado { get; set; } = "";

        [Range(typeof(decimal), "0", "100",
            ErrorMessage = "El porcentaje debe estar entre 0 y 100.")]
        public decimal Porcentaje { get; set; }

        public string? ClienteOriginal { get; set; }
        public string? EmpleadoOriginal { get; set; }

        public bool EsEdicion =>
            !string.IsNullOrWhiteSpace(ClienteOriginal) &&
            !string.IsNullOrWhiteSpace(EmpleadoOriginal);
    }

    public sealed class ImpulsadorOracleFilaVm
    {
        public string Cliente { get; set; } = "";
        public string NombreCliente { get; set; } = "";
        public string Empleado { get; set; } = "";
        public string NombreEmpleado { get; set; } = "";
        public decimal Porcentaje { get; set; }
    }

    public sealed class ImpulsadoresOracleIndexVm
    {
        public string Filtro { get; set; } = "";
        public ImpulsadorOracleEdicionVm Edicion { get; set; } = new();
        public List<ImpulsadorOracleFilaVm> Registros { get; set; } = new();
    }
}
