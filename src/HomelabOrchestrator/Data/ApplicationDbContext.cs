using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HomelabOrchestrator.Data;

/// <summary>
/// Backs ASP.NET Core Identity's user store. This app expects exactly one operator account,
/// seeded once at startup from <see cref="Options.AdminOptions"/> — there is no registration page.
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<IdentityUser>(options);
