using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BetfairReplicator.Pages.Admin;

public class LoginModel : PageModel
{
    private readonly IConfiguration _cfg;

    public LoginModel(IConfiguration cfg)
    {
        _cfg = cfg;
    }

    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(string? password, bool remember, string? returnUrl)
    {
        // ✅ Legge da IConfiguration:
        // - in locale: User Secrets (Development)
        // - in produzione: Environment Variables (Admin__Password)
        var adminPwd =
            _cfg["Admin:Password"]               // user secrets / json style
            ?? _cfg["Admin__Password"];          // fallback (se qualcuno l'ha messa così)

        if (string.IsNullOrWhiteSpace(adminPwd))
        {
            Error = "Admin password non configurata (Admin:Password o Admin__Password).";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(password) || password != adminPwd)
        {
            Error = "Password non valida.";
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "admin")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        var expires = remember
            ? DateTimeOffset.UtcNow.AddDays(7)
            : DateTimeOffset.UtcNow.AddHours(12);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = remember,
                ExpiresUtc = expires
            });

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);

        return RedirectToPage("/Index");
    }
}
