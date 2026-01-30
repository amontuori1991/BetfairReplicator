using System.Text.Json;
using BetfairReplicator.Models;
using BetfairReplicator.Options;
using BetfairReplicator.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace BetfairReplicator.Pages;

public class OrderPreviewModel : PageModel
{
    private readonly BetfairOptions _options;
    private readonly BetfairSessionStoreFile _store;
    private readonly BetfairAccountApiService _accountApi;
    private readonly BetfairBettingApiService _betting;

    public OrderPreviewModel(
        IOptions<BetfairOptions> options,
        BetfairSessionStoreFile store,
        BetfairAccountApiService accountApi,
        BetfairBettingApiService betting)
    {
        _options = options.Value;
        _store = store;
        _accountApi = accountApi;
        _betting = betting;
    }

    // Input
    [BindProperty(SupportsGet = true)] public string MarketId { get; set; } = "";
    [BindProperty(SupportsGet = true)] public long SelectionId { get; set; }
    [BindProperty(SupportsGet = true)] public string Side { get; set; } = "LAY"; // default
    [BindProperty(SupportsGet = true)] public double Price { get; set; } = 2.00;

    // LAY: % del saldo
    [BindProperty(SupportsGet = true)] public double StakePercent { get; set; } = 3.0;

    // BACK: importo fisso in €
    [BindProperty(SupportsGet = true)] public double? BackStakeEuro { get; set; }

    // Output
    public List<PreviewRow> Rows { get; private set; } = new();
    public string? ErrorMessage { get; private set; }

    // Runner dropdown
    public List<RunnerItem> Runners { get; private set; } = new();

    // Quotes
    public Dictionary<long, (double? Back, double? Lay)> QuotesBySelection { get; private set; } = new();
    public string? QuotesInfo { get; private set; }

    public class RunnerItem
    {
        public long SelectionId { get; set; }
        public string Name { get; set; } = "";
    }

    public class PreviewRow
    {
        public string DisplayName { get; set; } = "";
        public double? Balance { get; set; }

        // size che inviamo a Betfair (per LAY = stake, per BACK = importo puntato)
        public double? Stake { get; set; }

        // solo per LAY
        public double? Liability { get; set; }

        public bool MinStakeApplied { get; set; }

        public string Status { get; set; } = "";

        // dopo invio reale
        public string? BetId { get; set; }
        public string? LiveStatus { get; set; }
        public double? SizeMatched { get; set; }
        public double? SizeRemaining { get; set; }
        public double? AvgPriceMatched { get; set; }
        public string? JsonRpcPreview { get; set; }
    }

    public async Task OnGetAsync()
    {
        await LoadRunnersAndQuotesIfPossible();
    }

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        await LoadRunnersAndQuotesIfPossible();

        Rows = new();

        if (string.IsNullOrWhiteSpace(MarketId))
        {
            ErrorMessage = "Inserisci MarketId.";
            return Page();
        }

        if (SelectionId <= 0)
        {
            ErrorMessage = "Seleziona un runner.";
            return Page();
        }

        Side = (Side ?? "BACK").ToUpperInvariant();
        if (Side != "BACK" && Side != "LAY")
        {
            ErrorMessage = "Side deve essere BACK o LAY.";
            return Page();
        }

        if (Price < 1.01)
        {
            ErrorMessage = "Price troppo bassa (min 1.01).";
            return Page();
        }

        // Validazione input BACK (importo fisso)
        if (Side == "BACK")
        {
            if (!BackStakeEuro.HasValue)
            {
                ErrorMessage = "Per BACK devi inserire l'importo in €.";
                return Page();
            }

            if (BackStakeEuro.Value < 1.0)
            {
                ErrorMessage = "Importo BACK troppo basso (min 1€).";
                return Page();
            }
        }

        StakePercent = Math.Clamp(StakePercent, 0.1, 100.0);

        foreach (var acc in _options.Accounts)
        {
            var token = await _store.GetTokenAsync(acc.DisplayName);
            if (string.IsNullOrWhiteSpace(token))
            {
                Rows.Add(new PreviewRow { DisplayName = acc.DisplayName, Status = "Non collegato" });
                continue;
            }

            var (funds, err) = await _accountApi.GetAccountFundsAsync(acc.DisplayName, acc.AppKeyDelayed, token);

            if (err != null || funds?.availableToBetBalance is null)
            {
                Rows.Add(new PreviewRow
                {
                    DisplayName = acc.DisplayName,
                    Status = $"Errore saldo: {err ?? "unknown"}"
                });
                continue;
            }

            var balance = funds.availableToBetBalance.Value;

            double rawStake;
            double stake;
            bool minApplied;

            if (Side == "BACK")
            {
                rawStake = BackStakeEuro ?? 0.0;
                stake = ApplyBetfairMinStake(rawStake);
                minApplied = stake > Math.Round(rawStake, 2, MidpointRounding.AwayFromZero);
            }
            else
            {
                rawStake = balance * (StakePercent / 100.0);
                stake = ApplyBetfairMinStake(rawStake);
                minApplied = stake > Math.Round(rawStake, 2, MidpointRounding.AwayFromZero);
            }

            double? liability = null;
            if (Side == "LAY")
            {
                liability = Math.Round((Price - 1.0) * stake, 2, MidpointRounding.AwayFromZero);
            }

            var place = new PlaceOrdersParams
            {
                marketId = MarketId,
                instructions = new List<PlaceInstruction>
                {
                    new PlaceInstruction
                    {
                        selectionId = SelectionId,
                        side = Side,
                        orderType = "LIMIT",
                        limitOrder = new LimitOrder
                        {
                            size = stake,
                            price = Price,
                            persistenceType = "LAPSE"
                        }
                    }
                },
                customerRef = null
            };

            var rpc = new BetfairRpcRequest<PlaceOrdersParams>
            {
                method = "SportsAPING/v1.0/placeOrders",
                @params = place,
                id = 1
            };

            var json = JsonSerializer.Serialize(rpc, new JsonSerializerOptions { WriteIndented = true });

            Rows.Add(new PreviewRow
            {
                DisplayName = acc.DisplayName,
                Balance = balance,
                Stake = stake,
                Liability = liability,
                MinStakeApplied = minApplied,
                Status = "OK (preview)",
                JsonRpcPreview = json
            });
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSendAsync()
    {
        await LoadRunnersAndQuotesIfPossible();

        Rows = new();

        if (string.IsNullOrWhiteSpace(MarketId))
        {
            ErrorMessage = "Inserisci MarketId.";
            return Page();
        }

        if (SelectionId <= 0)
        {
            ErrorMessage = "Seleziona un runner.";
            return Page();
        }

        Side = (Side ?? "BACK").ToUpperInvariant();
        if (Side != "BACK" && Side != "LAY")
        {
            ErrorMessage = "Side deve essere BACK o LAY.";
            return Page();
        }

        if (Price < 1.01)
        {
            ErrorMessage = "Price troppo bassa (min 1.01).";
            return Page();
        }

        // Validazione input BACK (importo fisso)
        if (Side == "BACK")
        {
            if (!BackStakeEuro.HasValue)
            {
                ErrorMessage = "Per BACK devi inserire l'importo in €.";
                return Page();
            }

            if (BackStakeEuro.Value < 1.0)
            {
                ErrorMessage = "Importo BACK troppo basso (min 1€).";
                return Page();
            }
        }

        StakePercent = Math.Clamp(StakePercent, 0.1, 100.0);

        // customerRef base per questa “batch”
        var batchRef = $"BFR-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}".Substring(0, 32);

        foreach (var acc in _options.Accounts)
        {
            var token = await _store.GetTokenAsync(acc.DisplayName);
            if (string.IsNullOrWhiteSpace(token))
            {
                Rows.Add(new PreviewRow { DisplayName = acc.DisplayName, Status = "Non collegato" });
                continue;
            }

            var (funds, fundErr) = await _accountApi.GetAccountFundsAsync(acc.DisplayName, acc.AppKeyDelayed, token);
            if (fundErr != null || funds?.availableToBetBalance is null)
            {
                Rows.Add(new PreviewRow
                {
                    DisplayName = acc.DisplayName,
                    Status = $"Errore saldo: {fundErr ?? "unknown"}"
                });
                continue;
            }

            var balance = funds.availableToBetBalance.Value;

            // blocco globale: se saldo < 1€ non può piazzare nulla
            if (balance < 1.0)
            {
                Rows.Add(new PreviewRow
                {
                    DisplayName = acc.DisplayName,
                    Balance = balance,
                    Stake = null,
                    Status = "Saldo insufficiente (min 1€)"
                });
                continue;
            }

            double rawStake;
            double stake;
            bool minApplied;

            if (Side == "BACK")
            {
                rawStake = BackStakeEuro ?? 0.0;
                stake = ApplyBetfairMinStake(rawStake);
                minApplied = stake > Math.Round(rawStake, 2, MidpointRounding.AwayFromZero);

                // extra sicurezza: stake non può superare il saldo disponibile
                if (stake > balance)
                {
                    Rows.Add(new PreviewRow
                    {
                        DisplayName = acc.DisplayName,
                        Balance = balance,
                        Stake = stake,
                        MinStakeApplied = minApplied,
                        Status = "Importo BACK superiore al saldo disponibile"
                    });
                    continue;
                }
            }
            else
            {
                rawStake = balance * (StakePercent / 100.0);
                stake = ApplyBetfairMinStake(rawStake);
                minApplied = stake > Math.Round(rawStake, 2, MidpointRounding.AwayFromZero);
            }

            double? liability = null;
            if (Side == "LAY")
            {
                liability = Math.Round((Price - 1.0) * stake, 2, MidpointRounding.AwayFromZero);

                // consigliato: non bancare se liability supera il saldo
                if (liability > balance)
                {
                    Rows.Add(new PreviewRow
                    {
                        DisplayName = acc.DisplayName,
                        Balance = balance,
                        Stake = stake,
                        Liability = liability,
                        MinStakeApplied = minApplied,
                        Status = "Liability superiore al saldo disponibile"
                    });
                    continue;
                }
            }

            // customerRef UNICO per account (evita doppi invii se premi 2 volte)
            var customerRef = $"{batchRef}-{acc.DisplayName}".Replace(" ", "");
            if (customerRef.Length > 32) customerRef = customerRef.Substring(0, 32);

            var placeParams = new PlaceOrdersParams
            {
                marketId = MarketId,
                instructions = new List<PlaceInstruction>
                {
                    new PlaceInstruction
                    {
                        selectionId = SelectionId,
                        side = Side,
                        orderType = "LIMIT",
                        limitOrder = new LimitOrder
                        {
                            size = stake,
                            price = Price,
                            persistenceType = "LAPSE"
                        }
                    }
                },
                customerRef = customerRef
            };

            var (report, placeErr) = await _betting.PlaceOrdersAsync(acc.DisplayName, acc.AppKeyDelayed, token, placeParams);


            if (placeErr != null)
            {
                Rows.Add(new PreviewRow
                {
                    DisplayName = acc.DisplayName,
                    Balance = balance,
                    Stake = stake,
                    Liability = liability,
                    MinStakeApplied = minApplied,
                    Status = $"ERRORE: {placeErr}"
                });
                continue;
            }

            var instr = report.instructionReports?.FirstOrDefault();
            var betId = instr?.betId;

            var status = report.status ?? "UNKNOWN";
            var errCode = report.errorCode ?? instr?.errorCode;
            var orderStatus = instr?.orderStatus; // EXECUTABLE / EXECUTION_COMPLETE ecc.

            var msg = status;

            if (!string.IsNullOrWhiteSpace(orderStatus))
                msg += $" — {orderStatus}";

            if (!string.IsNullOrWhiteSpace(errCode))
                msg += $" ({errCode})";

            if (!string.IsNullOrWhiteSpace(betId))
                msg += $" — BetId: {betId}";




            Rows.Add(new PreviewRow
            {
                DisplayName = acc.DisplayName,
                Balance = balance,
                Stake = stake,
                Liability = liability,
                MinStakeApplied = minApplied,
                Status = msg,
                BetId = betId,
                AvgPriceMatched = instr?.averagePriceMatched,     // <-- ADD
                SizeMatched = instr?.sizeMatched,                 // <-- ADD
                SizeRemaining = instr?.sizeRemaining,             // <-- ADD

                LiveStatus = string.IsNullOrWhiteSpace(betId) ? null : "…",
            });

        }

        return Page();
    }

    private async Task LoadRunnersAndQuotesIfPossible()
    {
        Runners = new();
        QuotesBySelection = new();
        QuotesInfo = null;

        if (string.IsNullOrWhiteSpace(MarketId))
            return;

        // Driver = primo account con token valido
        BetfairAccountOptions? driver = null;
        string? driverToken = null;

        foreach (var a in _options.Accounts)
        {
            var t = await _store.GetTokenAsync(a.DisplayName);
            if (!string.IsNullOrWhiteSpace(t))
            {
                driver = a;
                driverToken = t;
                break;
            }
        }

        if (driver is null || string.IsNullOrWhiteSpace(driverToken))
            return;

        // Runner list
        var catsRes = await _betting.GetMarketCatalogueByMarketIdAsync(driver.DisplayName, driver.AppKeyDelayed, driverToken, MarketId);

        var cats = catsRes.Result;
        var err = catsRes.Error;

        if (err == null && cats != null && cats.Count > 0)
        {
            var first = cats[0];
            if (first.runners != null)
            {
                Runners = first.runners
                    .Select(r => new RunnerItem
                    {
                        SelectionId = r.selectionId,
                        Name = r.runnerName ?? r.selectionId.ToString()
                    })
                    .OrderBy(r => r.Name)
                    .ToList();

                if (SelectionId <= 0 && Runners.Count > 0)
                    SelectionId = Runners[0].SelectionId;
            }
        }

        // Quotes
        var bookRes = await _betting.ListMarketBookAsync(driver.DisplayName, driver.AppKeyDelayed, driverToken, MarketId);

        var books = bookRes.Result;
        var qErr = bookRes.Error;

        if (qErr != null || books == null || books.Count == 0)
        {
            QuotesInfo = qErr ?? "Nessun dato quote";
            return;
        }

        var book = books[0];
        QuotesInfo = book.isMarketDataDelayed == true ? "Quote (DELAYED)" : "Quote (LIVE)";

        if (book.runners == null) return;

        foreach (var r in book.runners)
        {
            var bestBack = r.ex?.availableToBack?.FirstOrDefault()?.price;
            var bestLay = r.ex?.availableToLay?.FirstOrDefault()?.price;
            QuotesBySelection[r.selectionId] = (bestBack, bestLay);
        }
    }

    public async Task<IActionResult> OnGetOrderStatusAsync(string account, string betId)
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(betId))
            return new JsonResult(new { ok = false, error = "parametri mancanti" });

        var acc = _options.Accounts.FirstOrDefault(a => a.DisplayName == account);
        if (acc is null)
            return new JsonResult(new { ok = false, error = "account non valido" });

        var token = await _store.GetTokenAsync(acc.DisplayName);
        if (string.IsNullOrWhiteSpace(token))
            return new JsonResult(new { ok = false, error = "account non collegato" });

        var (rep, err) = await _betting.ListCurrentOrdersAsync(acc.DisplayName, acc.AppKeyDelayed, token, betId);
        if (err != null)
            return new JsonResult(new { ok = false, error = err });

        var ord = rep.currentOrders?.FirstOrDefault(o => o.betId == betId);
        if (ord is null)
            return new JsonResult(new { ok = true, status = "NOT_IN_CURRENT_ORDERS" });

        return new JsonResult(new
        {
            ok = true,
            status = ord.status,              // EXECUTABLE / EXECUTION_COMPLETE / ecc.
            sizeMatched = ord.sizeMatched,
            sizeRemaining = ord.sizeRemaining,
            avgPriceMatched = ord.averagePriceMatched
        });



    }

    public async Task<IActionResult> OnPostCancelOrderAsync(string account, string betId)
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(betId))
            return new JsonResult(new { ok = false, error = "parametri mancanti" });

        var acc = _options.Accounts.FirstOrDefault(a => a.DisplayName == account);
        if (acc is null)
            return new JsonResult(new { ok = false, error = "account non valido" });

        var token = await _store.GetTokenAsync(acc.DisplayName);
        if (string.IsNullOrWhiteSpace(token))
            return new JsonResult(new { ok = false, error = "account non collegato" });

        var (rep, err) = await _betting.CancelOrderAsync(acc.DisplayName, acc.AppKeyDelayed, token, betId);
        if (err != null)
            return new JsonResult(new { ok = false, error = err });

        return new JsonResult(new
        {
            ok = true,
            status = rep.status ?? "UNKNOWN",
            errorCode = rep.errorCode
        });
    }

    private static double ApplyBetfairMinStake(double rawStake)
    {
        // Betfair: stake minimo 1.00
        var stake = Math.Round(rawStake, 2, MidpointRounding.AwayFromZero);
        return stake < 1.0 ? 1.0 : stake;
    }
}
