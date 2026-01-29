using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BetfairReplicator.Pages.Admin;

public class LoginModel : PageModel
{
    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public IActionResult OnPost(string? password)
    {
        // Per ora NON implementiamo auth: facciamo passare sempre
        // Così sblocchiamo la pagina e poi mettiamo la sicurezza corretta nello step successivo
        return RedirectToPage("/Admin/Accounts");
    }
}
