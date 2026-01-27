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

    public List<AccountRow> Rows { get; private set; } = new();

    public ConnectBetfairModel(IOptions<BetfairOptions> options, BetfairSessionStoreFile store)
    {
        _options = options.Value;
        _store = store;
    }

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

    public async Task<IActionResult> OnPostDisconnectAsync(string displayName)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            await _store.RemoveTokenAsync(displayName);

        // ricarico la tabella
        await OnGetAsync();

        return Page();
    }

    public class AccountRow
    {
        public string DisplayName { get; set; } = "";
        public bool IsConnected { get; set; }
    }
}
