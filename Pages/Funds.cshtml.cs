using BetfairReplicator.Options;
using BetfairReplicator.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace BetfairReplicator.Pages;

public class FundsModel : PageModel
{
    private readonly BetfairOptions _options;
    private readonly BetfairSessionStoreFile _store;
    private readonly BetfairAccountApiService _accountApi;

    public List<FundsRow> Rows { get; private set; } = new();
    public double StakePercent { get; private set; } = 3.0; // default 3%

    public FundsModel(
        IOptions<BetfairOptions> options,
        BetfairSessionStoreFile store,
        BetfairAccountApiService accountApi)
    {
        _options = options.Value;
        _store = store;
        _accountApi = accountApi;
    }

    public async Task OnGetAsync(double? pct)
    {
        StakePercent = pct.HasValue ? Math.Clamp(pct.Value, 0.1, 100.0) : 3.0;

        Rows = new();

        foreach (var acc in _options.Accounts)
        {
            var token = await _store.GetTokenAsync(acc.DisplayName);

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
