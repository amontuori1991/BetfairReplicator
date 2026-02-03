using BetfairReplicator.Models;
using BetfairReplicator.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BetfairReplicator.Pages
{
    public class OrdersModel : PageModel
    {
        private readonly BetfairSessionStoreFile _sessionStore;
        private readonly BetfairAccountStoreFile _accountStore;
        private readonly BetfairBettingApiService _bettingApi;

        // vincolo come Statistiche
        private static readonly DateTime MinFromUtc = new DateTime(2026, 1, 30, 0, 0, 0, DateTimeKind.Utc);

        public OrdersModel(
            BetfairSessionStoreFile sessionStore,
            BetfairAccountStoreFile accountStore,
            BetfairBettingApiService bettingApi)
        {
            _sessionStore = sessionStore;
            _accountStore = accountStore;
            _bettingApi = bettingApi;
        }

        // 🔹 query params
        [BindProperty(SupportsGet = true)]
        public string? Master { get; set; } // displayName scelto

        [BindProperty(SupportsGet = true)]
        public DateTime? From { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? To { get; set; }

        // UI
        public string? Error { get; private set; }
        public List<string> ConnectedAccounts { get; private set; } = new();
        public string? MasterUsed { get; private set; }

        public DateTime FromUtcUsed { get; private set; }
        public DateTime ToUtcUsed { get; private set; }

        public List<OrderRow> OpenOrders { get; private set; } = new();
        public List<OrderRow> SettledOrders { get; private set; } = new();

        public sealed class OrderRow
        {
            public string Source { get; set; } = ""; // OPEN / SETTLED
            public string? BetId { get; set; }
            public string? MarketId { get; set; }
            public long? SelectionId { get; set; }
            public string? Side { get; set; } // BACK/LAY
            public double? Price { get; set; }
            public double? Size { get; set; }
            public string? EventName { get; set; }
            public string? RunnerName { get; set; }
            public string? ResultBadge { get; set; } // OPEN / WIN / LOSS / EVEN
            public string? ResultClass { get; set; } // css class per badge

            public DateTime? DateUtc { get; set; } // placed o settled
            public string? Status { get; set; }

            public double? Profit { get; set; } // solo settled
            public double? Stake { get; set; }  // sizeSettled su settled
        }
        private sealed class MarketInfo
        {
            public string? EventName { get; set; }
            public Dictionary<long, string> RunnerBySelectionId { get; set; } = new();
        }
        private async Task<Dictionary<string, MarketInfo>> LoadMarketInfosAsync(
    string displayName,
    string appKey,
    string token,
    IEnumerable<string?> marketIds)
        {
            var dict = new Dictionary<string, MarketInfo>(StringComparer.OrdinalIgnoreCase);

            var ids = marketIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var mid in ids)
            {
                var catsRes = await _bettingApi.GetMarketCatalogueByMarketIdAsync(displayName, appKey, token, mid);
                if (catsRes.Error != null) continue;

                var cat = catsRes.Result?.FirstOrDefault();
                if (cat == null) continue;

                var mi = new MarketInfo
                {
                    // ✅ Nomi squadre (es: "Inter v Milan")
                    // fallback: se EVENT non arriva, uso marketName
                    EventName = cat.@event?.name ?? cat.marketName
                };


                if (cat.runners != null)
                {
                    foreach (var r in cat.runners)
                    {
                        var name = r.runnerName ?? r.selectionId.ToString();
                        mi.RunnerBySelectionId[r.selectionId] = name;
                    }
                }

                dict[mid] = mi;
            }

            return dict;
        }

        public async Task OnGetAsync()
        {
            // 1) range date con clamp
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

            // 2) trova account collegati (token + appkey)
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
                Error = "Nessun account utilizzabile (serve token + AppKeyDelayed). Vai su 'Collega' e/o 'Accounts'.";
                return;
            }

            // 3) scegli master: query ?Master=... altrimenti primo collegato
            var masterAcc = !string.IsNullOrWhiteSpace(Master)
                ? usable.FirstOrDefault(x => x.DisplayName.Equals(Master, StringComparison.OrdinalIgnoreCase))
                : null;

            masterAcc ??= usable.First();

            MasterUsed = masterAcc.DisplayName;

            var masterToken = await _sessionStore.GetTokenAsync(MasterUsed);
            if (string.IsNullOrWhiteSpace(masterToken))
            {
                Error = $"Token mancante per master '{MasterUsed}'.";
                return;
            }

            var appKey = masterAcc.AppKeyDelayed;

            // 4) OPEN orders (currentOrders)
            var (curRep, curErr) = await _bettingApi.ListCurrentOrdersAllAsync(
                displayName: MasterUsed,
                appKey: appKey,
                sessionToken: masterToken,
                fromUtc: null,
                toUtc: null,
                fromRecord: 0,
                recordCount: 1000
            );

            if (curErr != null)
            {
                Error = curErr;
                return;
            }

            var open = curRep?.currentOrders ?? new List<CurrentOrderSummary>();

            OpenOrders = open.Select(o => new OrderRow
            {
                Source = "OPEN",
                BetId = o.betId,
                MarketId = o.marketId,
                SelectionId = o.selectionId,
                Side = o.side,

                // Nel tuo model priceSize è double? -> lo trattiamo come prezzo (best effort)
                Price = o.priceSize?.price,

                // per OPEN è utile vedere quanto resta ancora da prendere
                Size = o.sizeRemaining,

                // Nel tuo model non c'è placedDate: per ora null (non rompe nulla)
                DateUtc = o.placedDate,

                Status = o.status
            })
            // senza DateUtc non ha senso ordinare per data: ordiniamo per MarketId/BetId (stabile)
            .OrderByDescending(x => x.BetId ?? "")
            .ToList();

            // ✅ Enrich OPEN: MarketId + SelectionId -> EventName + RunnerName
            var marketInfos = await LoadMarketInfosAsync(
                displayName: MasterUsed,
                appKey: appKey,
                token: masterToken,
                marketIds: OpenOrders.Select(x => x.MarketId)
            );

            foreach (var o in OpenOrders)
            {
                if (!string.IsNullOrWhiteSpace(o.MarketId) && marketInfos.TryGetValue(o.MarketId, out var mi))
                {
                    o.EventName = mi.EventName;

                    if (o.SelectionId.HasValue && mi.RunnerBySelectionId.TryGetValue(o.SelectionId.Value, out var rn))
                        o.RunnerName = rn;
                }

                // Badge per OPEN
                o.ResultBadge = "OPEN";
                o.ResultClass = "badge bg-secondary";
            }


            // 5) SETTLED orders (clearedOrders) nel range date
            var (settled, sErr) = await _bettingApi.FetchClearedOrdersAsync(
                displayName: MasterUsed,
                appKey: appKey,
                sessionToken: masterToken,
                fromUtc: fromUtc,
                toUtc: toUtc
            );

            if (sErr != null)
            {
                Error = sErr;
                return;
            }

            var settledList = (settled ?? new List<BetfairBettingApiService.ClearedOrderSummary>())
               .Where(x => x.settledDate.HasValue)
               .ToList();


            SettledOrders = settledList
                .Select(x =>
                {
                    var p = x.profit ?? 0.0;

                    var badge = p > 0.0001 ? "WIN" : (p < -0.0001 ? "LOSS" : "EVEN");
                    var cls = p > 0.0001 ? "badge bg-success"
                            : (p < -0.0001 ? "badge bg-danger"
                            : "badge bg-warning text-dark");

                    return new OrderRow
                    {
                        Source = "SETTLED",
                        BetId = x.betId,
                        MarketId = x.marketId,
                        Side = x.side,
                        Stake = x.sizeSettled,
                        Profit = x.profit,
                        DateUtc = x.settledDate,
                        Status = "SETTLED",

                        // ✅ descrizioni umane da itemDescription (richiede includeItemDescription=true)
                        EventName = x.itemDescription?.eventDesc
                                 ?? x.itemDescription?.marketDesc
                                 ?? x.marketId,

                        RunnerName = x.itemDescription?.runnerDesc,

                        ResultBadge = badge,
                        ResultClass = cls
                    };
                })
                .OrderByDescending(x => x.DateUtc ?? DateTime.MinValue)
                .ToList();


        }
    }
}
