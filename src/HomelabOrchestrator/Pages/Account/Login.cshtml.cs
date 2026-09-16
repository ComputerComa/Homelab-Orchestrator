using HomelabOrchestrator.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomelabOrchestrator.Pages.Account;

[AllowAnonymous]
public class LoginModel(SignInManager<IdentityUser> signInManager, ILogger<LoginModel> logger) : PageModel
{
    [BindProperty]
    public LoginFormModel Form { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(
            Form.Username, Form.Password, isPersistent: true, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            logger.LogInformation("{Username} signed in", Form.Username);
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
        }

        ErrorMessage = result.IsLockedOut
            ? "Too many failed attempts. Try again in a few minutes."
            : "Incorrect username or password.";
        return Page();
    }
}
