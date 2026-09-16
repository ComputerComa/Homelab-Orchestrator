namespace HomelabOrchestrator.Options;

/// <summary>
/// Bound from the "Admin" configuration section. Used only once, at startup, to seed the single
/// operator account when no account exists yet — see Program.cs's admin-seeding step. Never read
/// again afterward; change the password through the app's own change-password page, not this config.
/// </summary>
public class AdminOptions
{
    public const string SectionName = "Admin";

    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
