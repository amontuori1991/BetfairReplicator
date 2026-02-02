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

        public StatisticsModel(
            BetfairSessionStoreFile sessionStore,
            BetfairAccountStoreFile accountStore,
            BetfairBettingApiService bettingApi)
        {
            _sessionStore = sessionStore;
            _accountStore = accountStore;
            _bettingApi = bettingApi;
        }

        // ✅ Vincolo: non andare prima del 30/01/2026
        private static readonly DateTime MinFromUtc = new DateTime(2026, 1, 30, 0, 0, 0, DateTimeKind.Utc);

        // Query params (date picker)
        [BindProperty(SupportsGet = true)]
        public DateTime? From { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? To { get; set; }

        public string? Error { get; private set; }

        // Info account
        public int ConnectedAccounts { get; private set; }
        public List<string> ConnectedAccountNames { get; private set; } = new();

        // Periodo effettivo usato (clamp)
        public DateTime FromUtcUsed { get; private set; }
        public DateTime ToUtcUsed { get; private set; }

        // KPI Totali periodo (mese corrente nel range? → qui facciamo KPI del mese più recente presente)
        public double TotalProfit { get; private set; }
        public double TotalStake { get; private set; }
        public int TotalBets { get; private set; }
        public double TotalRoiPct => TotalStake == 0 ? 0 : (TotalProfit / TotalStake) * 100.0;

        // KPI BACK/LAY (totale periodo)
        public SideKpi BackKpi { get; private set; } = new("BACK");
        public SideKpi LayKpi { get; private set; } = new("LAY");

        // Serie mensili
        public List<MonthlyRow> MonthlyTotal { get; private set; } = new();
        public List<MonthlyRow> MonthlyBack { get; private set; } = new();
        public List<MonthlyRow> MonthlyLay { get; private set; } = new();

        // Equity cumulata giornaliera
        public List<EquityPoint> Equity { get; private set; } = new();

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
        }

        public sealed class EquityPoint
        {
            public string Date { get; set; } = ""; // yyyy-MM-dd
            public double CumProfit { get; set; }
        }

        public async Task OnGetAsync()
        {
            // 1) Clamp date range
            var nowUtc = DateTime.UtcNow;

            var fromUtc = From.HasValue
                ? DateTime.SpecifyKind(From.Value.Date, DateTimeKind.Utc)
                : MinFromUtc;

            var toUtc = To.HasValue
                ? DateTime.SpecifyKind(To.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                : nowUtc;

            if (fromUtc < MinFromUtc) fromUtc = MinFromUtc;
            if (toUtc > nowUtc) toUtc = nowUtc;
            if (toUtc < fromUtc) toUtc = fromUtc;

            FromUtcUsed = fromUtc;
            ToUtcUsed = toUtc;

            // 2) Trova tutti gli account collegati (token presente)
            var accounts = await _accountStore.GetAllAsync();

            var connected = new List<BetfairAccountStoreFile.BetfairAccountRecord>();
            var connectedTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var a in accounts)
            {
                var token = await _sessionStore.GetTokenAsync(a.DisplayName);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    if (string.IsNullOrWhiteSpace(a.AppKeyDelayed))
                        continue; // senza appKey non possiamo chiamare betting api

                    connected.Add(a);
                    connectedTokens[a.DisplayName] = token!;
                }
            }

            ConnectedAccounts = connected.Count;
            ConnectedAccountNames = connected.Select(x => x.DisplayName).ToList();

            if (ConnectedAccounts == 0)
            {
                Error = "Nessun account Betfair utilizzabile (serve token + AppKeyDelayed). Vai su 'Collega' e/o 'Accounts'.";
                return;
            }

            // 3) Scarica tutti i cleared orders (multi-account) nel range
            var allOrders = new List<BetfairBettingApiService.ClearedOrderSummary>();

            foreach (var acc in connected)
            {
                var token = connectedTokens[acc.DisplayName];
                var (orders, err) = await _bettingApi.FetchClearedOrdersAsync(
                    displayName: acc.DisplayName,
                    appKey: acc.AppKeyDelayed,
                    sessionToken: token,
                    fromUtc: fromUtc,
                    toUtc: toUtc
                );

                if (err != null)
                {
                    Error = $"Errore su account '{acc.DisplayName}': {err}";
                    return;
                }

                allOrders.AddRange(orders);
            }

            // 4) Normalizza e filtra (solo quelli con settledDate)
            var normalized = allOrders
                .Where(o => o.settledDate.HasValue)
                .Select(o => new
                {
                    SettledUtc = DateTime.SpecifyKind(o.settledDate!.Value, DateTimeKind.Utc),
                    Side = (o.side ?? "").Trim().ToUpperInvariant(), // BACK / LAY
                    Profit = o.profit ?? 0.0,
                    Stake = o.sizeSettled ?? 0.0
                })
                .ToList();

            // 5) KPI Totali periodo
            TotalProfit = normalized.Sum(x => x.Profit);
            TotalStake = normalized.Sum(x => x.Stake);
            TotalBets = normalized.Count;

            // 6) KPI BACK/LAY periodo
            var back = normalized.Where(x => x.Side == "BACK").ToList();
            var lay = normalized.Where(x => x.Side == "LAY").ToList();

            BackKpi.Profit = back.Sum(x => x.Profit);
            BackKpi.Stake = back.Sum(x => x.Stake);
            BackKpi.Bets = back.Count;

            LayKpi.Profit = lay.Sum(x => x.Profit);
            LayKpi.Stake = lay.Sum(x => x.Stake);
            LayKpi.Bets = lay.Count;

            // 7) Aggregazione mensile: Totale / BACK / LAY
            MonthlyTotal = AggregateMonthly(normalized);
            MonthlyBack = AggregateMonthly(back);
            MonthlyLay = AggregateMonthly(lay);

            // 8) Equity cumulata per giorno (somma profitti giornaliera -> cumulata)
            Equity = BuildEquity(normalized);
        }

        private static List<MonthlyRow> AggregateMonthly(IEnumerable<dynamic> list)
        {
            return list
                .GroupBy(x => new { x.SettledUtc.Year, x.SettledUtc.Month })
                .Select(g => new MonthlyRow
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Profit = g.Sum(x => (double)x.Profit),
                    Stake = g.Sum(x => (double)x.Stake),
                    Bets = g.Count()
                })
                .OrderBy(x => x.Year).ThenBy(x => x.Month)
                .ToList();
        }

        private static List<EquityPoint> BuildEquity(IEnumerable<dynamic> list)
        {
            var daily = list
                .GroupBy(x => ((DateTime)x.SettledUtc).Date)
                .Select(g => new { Day = g.Key, Profit = g.Sum(x => (double)x.Profit) })
                .OrderBy(x => x.Day)
                .ToList();

            var cum = 0.0;
            var res = new List<EquityPoint>();

            foreach (var d in daily)
            {
                cum += d.Profit;
                res.Add(new EquityPoint
                {
                    Date = d.Day.ToString("yyyy-MM-dd"),
                    CumProfit = cum
                });
            }

            return res;
        }
    }
}
