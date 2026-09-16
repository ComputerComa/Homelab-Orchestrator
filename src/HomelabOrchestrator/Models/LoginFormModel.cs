using System.ComponentModel.DataAnnotations;

namespace HomelabOrchestrator.Models;

public class LoginFormModel
{
    [Required(ErrorMessage = "Enter your username.")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "Enter your password.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";
}
