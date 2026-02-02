using BetfairReplicator.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BetfairReplicator.Pages
{
    public class StatisticsModel : PageModel
    {
        private readonly BetfairSessionStoreFile _sessionStore;
        private readonly BetfairAccountStoreFile _accountStore;
        private readonly BetfairBettingApiService _bettingApi;

        public StatisticsModel(
            BetfairSessionStoreFile sessionStore,
            BetfairAccountStoreFile accountStore,
            BetfairBettingApiService bettingApi)
        {
            _sessionStore = sessionStore;
            _accountStore = accountStore;
            _bettingApi = bettingApi;
        }

        public string? Error { get; private set; }
        public string? MasterDisplayName { get; private set; }

        public double? CurrentMonthProfit { get; private set; }
        public double? CurrentMonthRoiPct { get; private set; }
        public int CurrentMonthBets { get; private set; }

        public List<BetfairBettingApiService.MonthlyPnlRow> MonthlyRows { get; private set; } = new();

        public async Task OnGetAsync()
        {
            var accounts = await _accountStore.GetAllAsync();

            BetfairAccountStoreFile.BetfairAccountRecord? master = null;

            foreach (var a in accounts)
            {
                var token = await _sessionStore.GetTokenAsync(a.DisplayName);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    master = a;
                    break;
                }
            }

            if (master == null)
            {
                Error = "Nessun account Betfair connesso. Vai su 'Collega' e connetti almeno un account.";
                return;
            }

            MasterDisplayName = master.DisplayName;

            var sessionToken = await _sessionStore.GetTokenAsync(MasterDisplayName!);
            if (string.IsNullOrWhiteSpace(sessionToken))
            {
                Error = $"Account '{MasterDisplayName}' non risulta connesso (token mancante).";
                return;
            }

            var appKey = master.AppKeyDelayed;
            if (string.IsNullOrWhiteSpace(appKey))
            {
                Error = $"AppKey mancante per l'account master '{MasterDisplayName}'. Vai su Accounts e inserisci AppKeyDelayed.";
                return;
            }

            var nowUtc = DateTime.UtcNow;
            var startUtc = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-11);

            var result = await _bettingApi.GetMonthlyPnlAsync(
                displayName: MasterDisplayName!,
                appKey: appKey!,
                sessionToken: sessionToken!,
                fromUtc: startUtc,
                toUtc: nowUtc);

            if (result.Error != null)
            {
                Error = result.Error;
                return;
            }

            MonthlyRows = result.Rows;

            var curr = MonthlyRows.LastOrDefault(r => r.Year == nowUtc.Year && r.Month == nowUtc.Month);
            if (curr != null)
            {
                CurrentMonthProfit = curr.Profit;
                CurrentMonthRoiPct = curr.RoiPct;
                CurrentMonthBets = curr.Bets;
            }
            else
            {
                CurrentMonthProfit = 0;
                CurrentMonthRoiPct = 0;
                CurrentMonthBets = 0;
            }
        }
    }
}
