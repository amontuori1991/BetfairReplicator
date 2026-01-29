using BetfairReplicator.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BetfairReplicator.Pages.Admin;

public class AccountsModel : PageModel
{
    private readonly BetfairAccountStoreFile _store;

    public AccountsModel(BetfairAccountStoreFile store)
    {
        _store = store;
    }

    public List<AccountRow> Accounts { get; private set; } = new();

    public string? Error { get; private set; }
    public string? Success { get; private set; }

    public async Task OnGetAsync()
    {
        var all = await _store.GetAllAsync();

        Accounts = all
            .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(a => new AccountRow
            {
                DisplayName = a.DisplayName,
                AppKeyDelayed = a.AppKeyDelayed ?? "",
                // Nel record esistono solo i campi cifrati: se c'è P12Base64Enc => abbiamo un p12 salvato
                HasP12 = !string.IsNullOrWhiteSpace(a.P12Base64Enc)
            })
            .ToList();
    }

    // POST /Admin/Accounts?handler=Logout
    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync();
        return RedirectToPage("/Admin/Login");
    }

    // POST /Admin/Accounts?handler=Upsert
    public async Task<IActionResult> OnPostUpsertAsync(
        string displayName,
        string appKeyDelayed,
        string? p12Password,
        string? p12Base64,
        IFormFile? p12File,
        string? username,
        string? password)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            Error = "DisplayName mancante.";
            await OnGetAsync();
            return Page();
        }

        if (string.IsNullOrWhiteSpace(appKeyDelayed))
        {
            Error = "AppKeyDelayed mancante.";
            await OnGetAsync();
            return Page();
        }

        // Se arriva un file .p12/.pfx, lo convertiamo in Base64 (perché lo store salva stringhe)
        string? finalP12Base64 = null;

        if (p12File is { Length: > 0 })
        {
            using var ms = new MemoryStream();
            await p12File.CopyToAsync(ms);
            finalP12Base64 = Convert.ToBase64String(ms.ToArray());
        }
        else if (!string.IsNullOrWhiteSpace(p12Base64))
        {
            // Validazione base64 (se è invalido, Convert.FromBase64String lancia eccezione)
            try
            {
                _ = Convert.FromBase64String(p12Base64.Trim());
                finalP12Base64 = p12Base64.Trim();
            }
            catch
            {
                Error = "P12Base64 non valido (base64 errato).";
                await OnGetAsync();
                return Page();
            }
        }

        // Se stai salvando un p12 (file o base64), la password è obbligatoria
        if (finalP12Base64 != null && string.IsNullOrWhiteSpace(p12Password))
        {
            Error = "Password P12 mancante.";
            await OnGetAsync();
            return Page();
        }

        try
        {
            await _store.UpsertAsync(
                displayName: displayName.Trim(),
                appKeyDelayed: appKeyDelayed.Trim(),
                username: username,
                password: password,
                p12Base64: finalP12Base64,
                p12Password: p12Password
            );

            Success = $"Account '{displayName}' salvato.";
        }
        catch (Exception ex)
        {
            Error = $"Errore salvataggio: {ex.Message}";
        }

        await OnGetAsync();
        return Page();
    }

    // POST /Admin/Accounts?handler=Delete
    public async Task<IActionResult> OnPostDeleteAsync(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            Error = "DisplayName mancante.";
            await OnGetAsync();
            return Page();
        }

        await _store.RemoveAsync(displayName.Trim());
        Success = $"Account '{displayName}' eliminato.";

        await OnGetAsync();
        return Page();
    }

    public class AccountRow
    {
        public string DisplayName { get; set; } = "";
        public string AppKeyDelayed { get; set; } = "";
        public bool HasP12 { get; set; }
    }
}
