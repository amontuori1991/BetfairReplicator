using BetfairReplicator.Options;
using BetfairReplicator.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace BetfairReplicator.Pages;

public class ConnectBetfairModel : PageModel
{
    private readonly BetfairOptions _options;
    private readonly BetfairSessionStoreFile _store;
    private readonly BetfairSsoService _sso;

    public ConnectBetfairModel(
        IOptions<BetfairOptions> options,
        BetfairSessionStoreFile store,
        BetfairSsoService sso)
    {
        _options = options.Value;
        _store = store;
        _sso = sso;
    }

    public List<AccountRow> Rows { get; private set; } = new();

    public string? Error { get; private set; }
    public string? Success { get; private set; }

    public async Task OnGetAsync()
    {
        Rows = new();

        foreach (var a in _options.Accounts)
        {
            var token = await _store.GetTokenAsync(a.DisplayName);

            Rows.Add(new AccountRow
            {
                DisplayName = a.DisplayName,
                IsConnected = !string.IsNullOrWhiteSpace(token)
            });
        }
    }

    // POST: collega (login + salva token)
    public async Task<IActionResult> OnPostConnectAsync(string displayName, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            Error = "DisplayName mancante.";
            await OnGetAsync();
            return Page();
        }

        var acc = _options.Accounts.FirstOrDefault(x => x.DisplayName == displayName);
        if (acc == null)
        {
            Error = $"Account '{displayName}' non trovato in configurazione.";
            await OnGetAsync();
            return Page();
        }

        var appKey = acc.AppKeyDelayed;

        // IMPORTANT: se in locale hai messo "*********" o stringa vuota, Betfair risponde in modo “strano”
        if (string.IsNullOrWhiteSpace(appKey) || appKey.Contains("*"))
        {
            Error =
                "AppKey mancante o mascherata. " +
                "In locale mettila in appsettings.Development.json (NON committato) oppure come variabile ambiente/secrets su Fly.";
            await OnGetAsync();
            return Page();
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            Error = "Inserisci username e password.";
            await OnGetAsync();
            return Page();
        }

        var login = await _sso.LoginItalyAsync(appKey, username, password);

        if (!string.Equals(login.status, "SUCCESS", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(login.token))
        {
            // login.error può essere HTML_RESPONSE / UNEXPECTED_RESPONSE / ecc.
            Error = $"Login fallito: status={login.status}, error={login.error}";
            await OnGetAsync();
            return Page();
        }

        await _store.SetTokenAsync(acc.DisplayName, login.token);

        Success = $"Account '{acc.DisplayName}' collegato correttamente.";
        await OnGetAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDisconnectAsync(string displayName)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            await _store.RemoveTokenAsync(displayName);

        Success = $"Account '{displayName}' scollegato.";
        await OnGetAsync();
        return Page();
    }

    public class AccountRow
    {
        public string DisplayName { get; set; } = "";
        public bool IsConnected { get; set; }
    }
}
