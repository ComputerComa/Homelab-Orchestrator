using HomelabOrchestrator.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomelabOrchestrator.Pages.Account;

public class ChangePasswordModel(SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager) : PageModel
{
    [BindProperty]
    public ChangePasswordFormModel Form { get; set; } = new();

    public string? StatusMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return;
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            StatusMessage = "Your session has expired. Sign in again.";
            return;
        }

        var result = await userManager.ChangePasswordAsync(user, Form.CurrentPassword, Form.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return;
        }

        await signInManager.RefreshSignInAsync(user);
        StatusMessage = "Password changed.";
        Form = new ChangePasswordFormModel();
    }
}
