using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BetfairReplicator.Pages.Admin;

public class AccountsModel : PageModel
{
    public string? Error { get; private set; }
    public string? Success { get; private set; }

    // Deve esistere perché Accounts.cshtml fa @foreach (var a in Model.Accounts)
    public List<AccountRow> Accounts { get; private set; } = new();

    public void OnGet()
    {
        // Per ora vuota: la pagina deve solo caricarsi (senza 404).
        // Nel prossimo step agganciamo BetfairAccountStoreFile.
    }

    public IActionResult OnPostLogout()
    {
        return RedirectToPage("/Admin/Login");
    }

    public IActionResult OnPostDelete(string displayName)
    {
        // placeholder: nello step successivo colleghiamo l'eliminazione allo store
        return RedirectToPage();
    }

    public IActionResult OnPostUpsert()
    {
        // placeholder: nello step successivo colleghiamo il salvataggio allo store
        return RedirectToPage();
    }

    public class AccountRow
    {
        public string DisplayName { get; set; } = "";
        public string AppKeyDelayed { get; set; } = "";
        public bool HasP12 { get; set; }
    }
}
