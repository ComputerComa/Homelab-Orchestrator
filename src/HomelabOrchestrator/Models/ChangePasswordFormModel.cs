using System.ComponentModel.DataAnnotations;

namespace HomelabOrchestrator.Models;

public class ChangePasswordFormModel
{
    [Required(ErrorMessage = "Enter your current password.")]
    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = "";

    [Required(ErrorMessage = "Enter a new password.")]
    [MinLength(8, ErrorMessage = "The new password must be at least 8 characters.")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "Confirm the new password.")]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords don't match.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = "";
}
