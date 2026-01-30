using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BetfairReplicator.Pages.Admin;

public class LoginModel : PageModel
{
    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(string? password, bool remember, string? returnUrl)
    {
        var adminPwd = Environment.GetEnvironmentVariable("Admin__Password");

        if (string.IsNullOrWhiteSpace(adminPwd))
        {
            Error = "ADMIN_PASSWORD non configurata sul server.";
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
