using BetfairReplicator.Options;
using BetfairReplicator.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace BetfairReplicator.Pages;

public class BetfairLoginModel : PageModel
{
    private readonly BetfairOptions _options;
    private readonly BetfairSsoService _sso;
    private readonly BetfairSessionStoreFile _store;
    private readonly BetfairAccountStoreFile _accountStore;

    public string DisplayName { get; private set; } = "";
    public string? ErrorMessage { get; private set; }

    public BetfairLoginModel(
        IOptions<BetfairOptions> options,
        BetfairSsoService sso,
        BetfairSessionStoreFile store,
        BetfairAccountStoreFile accountStore)
    {
        _options = options.Value;
        _sso = sso;
        _store = store;
        _accountStore = accountStore;
    }

    public async Task<IActionResult> OnGet(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return RedirectToPage("/ConnectBetfair");

        await _accountStore.EnsureSeedFromOptionsAsync(_options.Accounts.Select(a => (a.DisplayName, a.AppKeyDelayed)));

        var acc = await _accountStore.GetAsync(displayName);
        if (acc is null)
            return RedirectToPage("/ConnectBetfair");

        DisplayName = displayName;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string displayName, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return RedirectToPage("/ConnectBetfair");

        DisplayName = displayName;

        var res = await _sso.LoginItalyAsync(displayName, username, password);

        if (res.status == "SUCCESS" && !string.IsNullOrWhiteSpace(res.token))
        {
            await _store.SetTokenAsync(displayName, res.token);
            return RedirectToPage("/ConnectBetfair");
        }

        ErrorMessage = $"Login fallito: status={res.status}, error={res.error}";
        return Page();
    }
}
