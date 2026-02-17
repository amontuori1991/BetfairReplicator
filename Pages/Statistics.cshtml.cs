using BetfairReplicator.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BetfairReplicator.Pages
{
    public class StatisticsModel : PageModel
    {
        private readonly BetfairSessionStoreFile _sessionStore;
        private readonly BetfairAccountStoreFile _accountStore;
        private readonly BetfairBettingApiService _bettingApi;
        private readonly BankrollStoreFile _bankrollStore;


        private static readonly DateTime MinFromUtc = new DateTime(2026, 1, 30, 0, 0, 0, DateTimeKind.Utc);

        private static readonly TimeZoneInfo RomeTz = ResolveRomeTimeZone();
        private static TimeZoneInfo ResolveRomeTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome"); }
            catch
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time"); }
                catch { return TimeZoneInfo.Utc; }
            }
        }

        public StatisticsModel(
            BetfairSessionStoreFile sessionStore,
            BetfairAccountStoreFile accountStore,
            BetfairBettingApiService bettingApi,
            BankrollStoreFile bankrollStore)
        {
            _sessionStore = sessionStore;
            _accountStore = accountStore;
            _bettingApi = bettingApi;
            _bankrollStore = bankrollStore;
        }


        // query
        [BindProperty(SupportsGet = true)]
        public string? Account { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? From { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? To { get; set; }

        [BindProperty(SupportsGet = true)]
        public double? Bankroll { get; set; }


        public string? Error { get; private set; }

        // UI
        public List<string> ConnectedAccounts { get; private set; } = new();
        public string? AccountUsed { get; private set; }

        public DateTime FromUtcUsed { get; private set; }
        public DateTime ToUtcUsed { get; private set; }

        // KPI
        public double TotalProfit { get; private set; }
        public double TotalStake { get; private set; }
        public int TotalBets { get; private set; }
        public double TotalRoiPct => TotalStake == 0 ? 0 : (TotalProfit / TotalStake) * 100.0;

        // Bankroll (saldo iniziale) - per ROI su capitale (per account)
        public double StartingBankroll { get; private set; }
        public double TotalRoiBankrollPct => StartingBankroll == 0 ? 0 : (TotalProfit / StartingBankroll) * 100.0;


        public double BackRoiBankrollPct => StartingBankroll <= 0 ? 0 : (BackKpi.Profit / StartingBankroll) * 100.0;
        public double LayRoiBankrollPct => StartingBankroll <= 0 ? 0 : (LayKpi.Profit / StartingBankroll) * 100.0;


        public SideKpi BackKpi { get; private set; } = new("BACK");
        public SideKpi LayKpi { get; private set; } = new("LAY");

        public List<MonthlyRow> MonthlyTotal { get; private set; } = new();
        public List<MonthlyRow> MonthlyBack { get; private set; } = new();
        public List<MonthlyRow> MonthlyLay { get; private set; } = new();

        // Giornaliero + Equity + DD
        public List<DailyPoint> DailyProfit { get; private set; } = new();
        public List<EquityPoint> Equity { get; private set; } = new();
        public double MaxDrawdown { get; private set; }

        public sealed class SideKpi
        {
            public SideKpi(string side) { Side = side; }
            public string Side { get; }
            public double Profit { get; set; }
            public double Stake { get; set; }
            public int Bets { get; set; }
            public double RoiPct => Stake == 0 ? 0 : (Profit / Stake) * 100.0;
        }

        public sealed class MonthlyRow
        {
            public int Year { get; set; }
            public int Month { get; set; }
            public double Profit { get; set; }
            public double Stake { get; set; }
            public int Bets { get; set; }

            public string Label => new DateTime(Year, Month, 1).ToString("yyyy-MM");
            public double RoiPct => Stake == 0 ? 0 : (Profit / Stake) * 100.0;
            public double RoiBankrollPct { get; set; } // (Profit / Bankroll) * 100

        }

        public sealed class DailyPoint
        {
            public string Date { get; set; } = ""; // yyyy-MM-dd
            public double Profit { get; set; }
        }

        public sealed class EquityPoint
        {
            public string Date { get; set; } = ""; // yyyy-MM-dd
            public double CumProfit { get; set; }
            public double Drawdown { get; set; } // <= 0
        }

        private sealed class NormalizedRow
        {
            public DateTime SettledUtc { get; set; }
            public string Side { get; set; } = "";
            public double Profit { get; set; }
            public double Stake { get; set; }
        }

        public async Task OnGetAsync()
        {
            // 1) clamp date (interpretate come date ROMA → UTC)
            var nowUtc = DateTime.UtcNow;

            DateTime fromUtc;
            if (From.HasValue)
            {
                var fromLocal = new DateTime(From.Value.Year, From.Value.Month, From.Value.Day, 0, 0, 0, DateTimeKind.Unspecified);
                fromUtc = TimeZoneInfo.ConvertTimeToUtc(fromLocal, RomeTz);
            }
            else fromUtc = MinFromUtc;

            DateTime toUtc;
            if (To.HasValue)
            {
                var endLocal = new DateTime(To.Value.Year, To.Value.Month, To.Value.Day, 23, 59, 59, 999, DateTimeKind.Unspecified);
                endLocal = endLocal.AddTicks(9999);
                toUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, RomeTz);
            }
            else toUtc = nowUtc;

            if (fromUtc < MinFromUtc) fromUtc = MinFromUtc;
            if (toUtc > nowUtc) toUtc = nowUtc;
            if (toUtc < fromUtc) toUtc = fromUtc;

            FromUtcUsed = fromUtc;
            ToUtcUsed = toUtc;


            // 2) account collegati
            var accounts = await _accountStore.GetAllAsync();
            var usable = new List<BetfairAccountStoreFile.BetfairAccountRecord>();

            foreach (var a in accounts.OrderBy(x => x.DisplayName))
            {
                var token = await _sessionStore.GetTokenAsync(a.DisplayName);
                if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(a.AppKeyDelayed))
                    usable.Add(a);
            }

            ConnectedAccounts = usable.Select(x => x.DisplayName).ToList();

            if (usable.Count == 0)
            {
                Error = "Nessun account Betfair utilizzabile (serve token + AppKeyDelayed). Vai su 'Collega' e/o 'Accounts'.";
                return;
            }

            // 3) seleziona account
            var acc = !string.IsNullOrWhiteSpace(Account)
                ? usable.FirstOrDefault(x => x.DisplayName.Equals(Account, StringComparison.OrdinalIgnoreCase))
                : null;

            acc ??= usable.First();
            AccountUsed = acc.DisplayName;
            // ✅ Bankroll: priorità a querystring (?Bankroll=), altrimenti da JSON per account
            if (Bankroll.HasValue && Bankroll.Value >= 0)
            {
                StartingBankroll = Bankroll.Value;
                await _bankrollStore.SetAsync(AccountUsed!, StartingBankroll);
            }
            else
            {
                StartingBankroll = await _bankrollStore.GetAsync(AccountUsed!) ?? 0.0;
            }

            var tokenUsed = await _sessionStore.GetTokenAsync(AccountUsed);
            if (string.IsNullOrWhiteSpace(tokenUsed))
            {
                Error = $"Token mancante per account '{AccountUsed}'.";
                return;
            }

            // 4) fetch cleared orders
            var (orders, err) = await _bettingApi.FetchClearedOrdersAsync(
                displayName: AccountUsed,
                appKey: acc.AppKeyDelayed,
                sessionToken: tokenUsed,
                fromUtc: fromUtc,
                toUtc: toUtc
            );

            if (err != null)
            {
                Error = err;
                return;
            }

            var allOrders = orders ?? new List<BetfairBettingApiService.ClearedOrderSummary>();

            var normalized = allOrders
                .Where(o => o.settledDate.HasValue)
                .Select(o => new NormalizedRow
                {
                    SettledUtc = DateTime.SpecifyKind(o.settledDate!.Value, DateTimeKind.Utc),
                    Side = (o.side ?? "").Trim().ToUpperInvariant(),
                    Profit = o.profit ?? 0.0,
                    Stake = o.sizeSettled ?? 0.0
                })
                .ToList();

            // 5) KPI
            TotalProfit = normalized.Sum(x => x.Profit);
            TotalStake = normalized.Sum(x => x.Stake);
            TotalBets = normalized.Count;

            var back = normalized.Where(x => x.Side == "BACK").ToList();
            var lay = normalized.Where(x => x.Side == "LAY").ToList();

            BackKpi.Profit = back.Sum(x => x.Profit);
            BackKpi.Stake = back.Sum(x => x.Stake);
            BackKpi.Bets = back.Count;

            LayKpi.Profit = lay.Sum(x => x.Profit);
            LayKpi.Stake = lay.Sum(x => x.Stake);
            LayKpi.Bets = lay.Count;

            // 6) Mensile
            MonthlyTotal = AggregateMonthly(normalized, StartingBankroll);
            MonthlyBack = AggregateMonthly(back, StartingBankroll);
            MonthlyLay = AggregateMonthly(lay, StartingBankroll);


            // 7) Giornaliero (profit)
            DailyProfit = BuildDailyProfit(normalized);

            // 8) Equity + Drawdown
            Equity = BuildEquityWithDrawdown(normalized, out var maxDd);
            MaxDrawdown = maxDd;
        }

        private static List<MonthlyRow> AggregateMonthly(IEnumerable<NormalizedRow> list, double startingBankroll)

        {
            return list
                .GroupBy(x => new { x.SettledUtc.Year, x.SettledUtc.Month })
                .Select(g => new MonthlyRow
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Profit = g.Sum(x => x.Profit),
                    Stake = g.Sum(x => x.Stake),
                    Bets = g.Count(),
                    RoiBankrollPct = startingBankroll <= 0 ? 0 : (g.Sum(x => x.Profit) / startingBankroll) * 100.0

                })

                .OrderBy(x => x.Year).ThenBy(x => x.Month)
                .ToList();
        }

        private static List<DailyPoint> BuildDailyProfit(IEnumerable<NormalizedRow> list)
        {
            return list
                .GroupBy(x => x.SettledUtc.Date)
                .Select(g => new DailyPoint
                {
                    Date = g.Key.ToString("yyyy-MM-dd"),
                    Profit = g.Sum(x => x.Profit)
                })
                .OrderBy(x => x.Date)
                .ToList();
        }

        private static List<EquityPoint> BuildEquityWithDrawdown(IEnumerable<NormalizedRow> list, out double maxDrawdownAbs)
        {
            var daily = list
                .GroupBy(x => x.SettledUtc.Date)
                .Select(g => new { Day = g.Key, Profit = g.Sum(x => x.Profit) })
                .OrderBy(x => x.Day)
                .ToList();

            var res = new List<EquityPoint>();

            double cum = 0.0;
            double peak = 0.0;
            double maxDd = 0.0; // negativo

            foreach (var d in daily)
            {
                cum += d.Profit;
                if (cum > peak) peak = cum;

                var dd = cum - peak; // <= 0
                if (dd < maxDd) maxDd = dd;

                res.Add(new EquityPoint
                {
                    Date = d.Day.ToString("yyyy-MM-dd"),
                    CumProfit = cum,
                    Drawdown = dd
                });
            }

            maxDrawdownAbs = Math.Abs(maxDd);
            return res;
        }

        public async Task<IActionResult> OnGetBankrollAsync(string account)
        {
            if (string.IsNullOrWhiteSpace(account))
                return new JsonResult(new { bankroll = 0.0 });

            var v = await _bankrollStore.GetAsync(account) ?? 0.0;
            return new JsonResult(new { bankroll = v });
        }

    }
}
