using BetfairReplicator.Models;
using BetfairReplicator.Options;
using BetfairReplicator.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace BetfairReplicator.Pages;

public class MarketSearchModel : PageModel
{
    private readonly BetfairOptions _options;
    private readonly BetfairSessionStoreFile _store;
    private readonly BetfairBettingApiService _betting;

    public MarketSearchModel(IOptions<BetfairOptions> options, BetfairSessionStoreFile store, BetfairBettingApiService betting)
    {
        _options = options.Value;
        _store = store;
        _betting = betting;
    }

    [BindProperty(SupportsGet = true)] public string Account { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string Q { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? EventId { get; set; }
    [BindProperty(SupportsGet = true)] public string MarketType { get; set; } = "ALL";

    [BindProperty(SupportsGet = true)] public double StakePercent { get; set; } = 3.0;

    public string? Error { get; private set; }

    public List<EventResult> Events { get; private set; } = new();
    public List<MarketCatalogue> Markets { get; private set; } = new();
    public string? SelectedEventName { get; private set; }
    public bool IsInPlay { get; private set; } = false;


    public async Task OnGetAsync()
    {
        // 1) se non specificato, metti il primo account configurato (solo per riempire input)
        if (string.IsNullOrWhiteSpace(Account))
            Account = _options.Accounts.FirstOrDefault()?.DisplayName ?? "";

        // 2) prova a usare l'account selezionato
        BetfairAccountOptions? driver = _options.Accounts.FirstOrDefault(a => a.DisplayName == Account);
        string? token = null;

        if (driver != null)
            token = await _store.GetTokenAsync(driver.DisplayName);

        // 3) fallback: primo account con token valido
        // 3) modalità CONTROLLATA: niente fallback automatico
        if (string.IsNullOrWhiteSpace(token))
        {
            Error = $"Account '{Account}' non collegato. Vai su Collega Betfair.";
            return;
        }


        if (driver is null || string.IsNullOrWhiteSpace(token))
        {
            Error = "Nessun account collegato. Vai su Collega Betfair.";
            return;
        }

        // Calcio = eventTypeId "1"
        var soccerEventTypeId = "1";

        var from = DateTime.UtcNow.AddHours(-2);
        var to = DateTime.UtcNow.AddDays(3);

        if (string.IsNullOrWhiteSpace(EventId))
        {
            var (events, err) = await _betting.ListEventsAsync(driver.DisplayName, driver.AppKeyDelayed, token, soccerEventTypeId, Q, from, to);


            if (err != null)
            {
                // Con auto-relogin: NON rimuovere il token.
                // Se il relogin fallisce, arriva un messaggio esplicito e lo mostriamo.
                if (err.Contains("RELOGIN_FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    Error = err;
                    return;
                }


                Error = err;
                return;
            }


            Events = events ?? new();
            return;

        }

        var (markets, err2) = await _betting.ListAllMarketsByEventAsync(driver.DisplayName, driver.AppKeyDelayed, token, EventId);

        if (err2 != null)
        {
            // Con auto-relogin: NON rimuovere il token.
            // Se il relogin fallisce, arriva un messaggio esplicito e lo mostriamo.
            if (err2.Contains("RELOGIN_FAILED", StringComparison.OrdinalIgnoreCase))
            {
                Error = err2;
                return;
            }

            Error = err2;
            return;
        }



        Markets = markets ?? new();
        Markets = ApplyMarketFamilyFilter(Markets, MarketType);

        // ===== Header info: nome evento + LIVE/PRE =====
        SelectedEventName = null;
        IsInPlay = false;

        // 1) Nome evento: lo risolviamo tramite ListEvents (stesso range, nessuna chiamata "strana")
        try
        {
            var (evs, evErr) = await _betting.ListEventsAsync(driver.DisplayName, driver.AppKeyDelayed, token, soccerEventTypeId, Q, from, to);


            if (evErr == null && evs != null)
            {
                var match = evs.FirstOrDefault(x => x.Event?.id == EventId);
                SelectedEventName = match?.Event?.name;
            }
        }
        catch { /* non blocca la pagina */ }

        // 2) LIVE/PRE: leggiamo inplay dal primo market disponibile
        var firstMarketId = Markets.FirstOrDefault()?.marketId;
        if (!string.IsNullOrWhiteSpace(firstMarketId))
        {
            var bookRes = await _betting.ListMarketBookAsync(driver.DisplayName, driver.AppKeyDelayed, token, firstMarketId);


            if (bookRes.Error != null)
            {
                if (bookRes.Error.Contains("RELOGIN_FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    Error = bookRes.Error;
                    return;
                }
            }

            else
            {
                var books = bookRes.Result;
                if (books != null && books.Count > 0)
                    IsInPlay = books[0].inplay == true;
            }
        }

    }


    private static List<MarketCatalogue> ApplyMarketFamilyFilter(List<MarketCatalogue>? input, string marketType)
    {
        if (input == null) return new();
        if (string.IsNullOrWhiteSpace(marketType) || marketType.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            return input;

        bool NameContains(MarketCatalogue m, string s) =>
            (m.marketName ?? "").Contains(s, StringComparison.OrdinalIgnoreCase);

        var filtered = marketType.ToUpperInvariant() switch
        {
            "FULLTIME_RESULT" => input.Where(m =>
                    NameContains(m, "Match Odds") ||
                    NameContains(m, "Double Chance"))
                .ToList(),

            "OVER_UNDER_ALL" => input.Where(m =>
                    NameContains(m, "Over/Under") && NameContains(m, "Goals"))
                .ToList(),

            "FIRST_HALF_GROUP" => input.Where(m =>
                    NameContains(m, "First Half") ||
                    NameContains(m, "Half Time") ||
                    NameContains(m, "HT/FT") ||
                    NameContains(m, "Half Time/Full Time"))
                .ToList(),

            "BTTS" => input.Where(m => NameContains(m, "Both teams to Score")).ToList(),
            "CORRECT_SCORE" => input.Where(m => NameContains(m, "Correct Score")).ToList(),

            _ => input
        };

        return filtered;
    }
    
    public async Task<IActionResult> OnGetQuotesAsync(string marketId, string? account)
    {
        if (string.IsNullOrWhiteSpace(marketId))
            return new JsonResult(new { ok = false, error = "marketId mancante" });

        // 1) se account è passato, prova a usare quello
        var driver = !string.IsNullOrWhiteSpace(account)
            ? _options.Accounts.FirstOrDefault(a => a.DisplayName == account)
            : null;

        // 2) fallback: primo con token
        // 2) modalità CONTROLLATA: niente fallback
        if (driver == null)
            return new JsonResult(new { ok = false, error = "Account non valido o mancante" });


        if (driver == null)
            return new JsonResult(new { ok = false, error = "Nessun account disponibile" });

        var token = await _store.GetTokenAsync(driver.DisplayName);
        if (string.IsNullOrWhiteSpace(token))
            return new JsonResult(new { ok = false, error = "Account non collegato" });

        var result = await _betting.ListMarketBookAsync(driver.DisplayName, driver.AppKeyDelayed, token, marketId);

        var books = result.Result;
        var err = result.Error;

        if (err != null && err.Contains("RELOGIN_FAILED", StringComparison.OrdinalIgnoreCase))
        {
            return new JsonResult(new { ok = false, error = err });
        }



        if (err != null || books == null || books.Count == 0)
            return new JsonResult(new { ok = false, error = err ?? "Nessun dato" });

        var book = books[0];
        var delayed = book.isMarketDataDelayed == true;

        var dict = new Dictionary<long, object>();

        foreach (var r in book.runners ?? new List<RunnerBook>())
        {
            var bestBack = r.ex?.availableToBack?.FirstOrDefault();
            var bestLay = r.ex?.availableToLay?.FirstOrDefault();

            dict[r.selectionId] = new
            {
                back = bestBack?.price,
                backSize = bestBack?.size,
                lay = bestLay?.price,
                laySize = bestLay?.size
            };
        }

        return new JsonResult(new
        {
            ok = true,
            delayed,
            marketId = book.marketId,
            runners = dict
        });
    }

}
