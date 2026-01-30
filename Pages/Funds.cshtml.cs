using BetfairReplicator.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BetfairReplicator.Pages;

public class FundsModel : PageModel
{
    private readonly BetfairSessionStoreFile _sessionStore;
    private readonly BetfairAccountApiService _accountApi;
    private readonly BetfairAccountStoreFile _accountStore;

    public List<FundsRow> Rows { get; private set; } = new();
    public double StakePercent { get; private set; } = 3.0; // default 3%

    public FundsModel(
        BetfairSessionStoreFile sessionStore,
        BetfairAccountApiService accountApi,
        BetfairAccountStoreFile accountStore)
    {
        _sessionStore = sessionStore;
        _accountApi = accountApi;
        _accountStore = accountStore;
    }

    public async Task OnGetAsync(double? pct)
    {
        StakePercent = pct.HasValue ? Math.Clamp(pct.Value, 0.1, 100.0) : 3.0;
        Rows = new();

        // ✅ Fonte unica: store su /data/betfair-accounts.json
        var accounts = (await _accountStore.GetAllAsync())
            .OrderBy(a => a.DisplayName)
            .ToList();

        foreach (var acc in accounts)
        {
            var token = await _sessionStore.GetTokenAsync(acc.DisplayName);

            if (string.IsNullOrWhiteSpace(token))
            {
                Rows.Add(new FundsRow
                {
                    DisplayName = acc.DisplayName,
                    IsConnected = false,
                    Error = "Non collegato"
                });
                continue;
            }

            var (result, error) = await _accountApi.GetAccountFundsAsync(acc.AppKeyDelayed, token);

            // ✅ Se token scaduto => lo rimuoviamo e rendiamo lo stato coerente
            if (IsSessionExpired(error))
            {
                await _sessionStore.RemoveTokenAsync(acc.DisplayName);

                Rows.Add(new FundsRow
                {
                    DisplayName = acc.DisplayName,
                    IsConnected = false,
                    Error = "Sessione scaduta – ricollega"
                });
                continue;
            }

            double? stakePreview = null;
            if (error == null && result?.availableToBetBalance is double bal && bal > 0)
            {
                stakePreview = Math.Round(bal * (StakePercent / 100.0), 2, MidpointRounding.AwayFromZero);
            }

            Rows.Add(new FundsRow
            {
                DisplayName = acc.DisplayName,
                IsConnected = true,
                AvailableToBetBalance = result?.availableToBetBalance,
                Exposure = result?.exposure,
                StakePreview = stakePreview,
                Error = error
            });
        }
    }

    private static bool IsSessionExpired(string? err)
    {
        if (string.IsNullOrWhiteSpace(err)) return false;

        var up = err.ToUpperInvariant();

        // robusto (perché i testi possono variare)
        return up.Contains("INVALID_SESSION")
            || up.Contains("NO_SESSION")
            || up.Contains("SESSION")
            || up.Contains("TOKEN")
            || up.Contains("ANGX-0003");
    }

    public class FundsRow
    {
        public string DisplayName { get; set; } = "";
        public bool IsConnected { get; set; }
        public double? AvailableToBetBalance { get; set; }
        public double? Exposure { get; set; }
        public double? StakePreview { get; set; }
        public string? Error { get; set; }
    }
}
