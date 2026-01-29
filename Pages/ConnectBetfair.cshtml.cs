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
    private readonly BetfairAccountStoreFile _accountStore;

    public ConnectBetfairModel(
        IOptions<BetfairOptions> options,
        BetfairSessionStoreFile store,
        BetfairSsoService sso,
        BetfairAccountStoreFile accountStore)
    {
        _options = options.Value;
        _store = store;
        _sso = sso;
        _accountStore = accountStore;
    }

    public List<AccountRow> Rows { get; private set; } = new();
    public string? Error { get; private set; }
    public string? Success { get; private set; }

    public async Task OnGetAsync()
    {
        // importa eventuali DisplayName da appsettings nel file store (senza secrets)
        await _accountStore.EnsureSeedFromOptionsAsync(_options.Accounts.Select(a => (a.DisplayName, a.AppKeyDelayed)));

        var accounts = await _accountStore.GetAllAsync();

        Rows = new();
        foreach (var a in accounts)
        {
            var token = await _store.GetTokenAsync(a.DisplayName);

            Rows.Add(new AccountRow
            {
                DisplayName = a.DisplayName,
                IsConnected = !string.IsNullOrWhiteSpace(token)
            });
        }
    }

    public async Task<IActionResult> OnPostConnectAsync(string displayName, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            Error = "DisplayName mancante.";
            await OnGetAsync();
            return Page();
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            Error = "Inserisci username e password.";
            await OnGetAsync();
            return Page();
        }

        var login = await _sso.LoginItalyAsync(displayName, username, password);

        if (!string.Equals(login.status, "SUCCESS", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(login.token))
        {
            Error = $"Login fallito: status={login.status}, error={login.error}";
            await OnGetAsync();
            return Page();
        }

        await _store.SetTokenAsync(displayName, login.token);

        Success = $"Account '{displayName}' collegato correttamente.";
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
