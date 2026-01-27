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

    public string DisplayName { get; private set; } = "";
    public string? ErrorMessage { get; private set; }

    public BetfairLoginModel(
        IOptions<BetfairOptions> options,
        BetfairSsoService sso,
        BetfairSessionStoreFile store)
    {
        _options = options.Value;
        _sso = sso;
        _store = store;
    }

    public IActionResult OnGet(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return RedirectToPage("/ConnectBetfair");

        var acc = _options.Accounts.FirstOrDefault(a => a.DisplayName == displayName);
        if (acc is null)
            return RedirectToPage("/ConnectBetfair");

        DisplayName = displayName;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string displayName, string username, string password)
    {
        var acc = _options.Accounts.FirstOrDefault(a => a.DisplayName == displayName);
        if (acc is null)
            return RedirectToPage("/ConnectBetfair");

        DisplayName = displayName;

        var res = await _sso.LoginItalyAsync(acc.AppKeyDelayed, username, password);

        if (res.status == "SUCCESS" && !string.IsNullOrWhiteSpace(res.token))
        {
            await _store.SetTokenAsync(displayName, res.token);
            return RedirectToPage("/ConnectBetfair");
        }

        ErrorMessage = $"Login fallito: status={res.status}, error={res.error}";
        return Page();
    }
}
