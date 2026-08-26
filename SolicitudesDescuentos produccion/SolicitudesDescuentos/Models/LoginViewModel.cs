using System.ComponentModel.DataAnnotations;

namespace SolicitudesDescuentos.Models
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Debe ingresar el usuario.")]
        [Display(Name = "Usuario")]
        public string? CodUsuario { get; set; }

        [Required(ErrorMessage = "Debe ingresar la contraseña.")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string? Password { get; set; }
    }
}