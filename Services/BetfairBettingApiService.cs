using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BetfairReplicator.Models;
using System.Collections.Concurrent;

namespace BetfairReplicator.Services;

public class BetfairBettingApiService
{
    private readonly HttpClient _http;
    private readonly BetfairSsoService _sso;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _reloginLocks = new(StringComparer.OrdinalIgnoreCase);

    public BetfairBettingApiService(IHttpClientFactory httpFactory, BetfairSsoService sso)
    {
        _http = httpFactory.CreateClient("Betfair");
        _http.Timeout = TimeSpan.FromSeconds(30);

        _sso = sso;
    }

    // ====== CORE ======

    protected async Task<(T Result, string? Error)> CallAsync<T>(
        string displayName,
        string appKey,
        string sessionToken,
        object rpcRequest)
        where T : class
    {
        // 1) prima chiamata con token attuale
        var (result, error, shouldRetry) = await CallOnceAsync<T>(appKey, sessionToken, rpcRequest);

        // 2) se sessione scaduta -> relogin + retry 1 volta
        if (shouldRetry)
        {
            var (newToken, relogErr) = await EnsureReloginSingleFlightAsync(displayName);
            if (relogErr != null || string.IsNullOrWhiteSpace(newToken))
                return (null!, $"RELOGIN_FAILED: {relogErr ?? "unknown"}");



            var (result2, error2, _) = await CallOnceAsync<T>(appKey, newToken!, rpcRequest);
            return (result2, error2);
        }

        return (result, error);
    }
    private async Task<(string? Token, string? Error)> EnsureReloginSingleFlightAsync(string displayName)
    {
        var sem = _reloginLocks.GetOrAdd(displayName, _ => new SemaphoreSlim(1, 1));

        await sem.WaitAsync();
        try
        {
            // 1) magari un'altra request ha già fatto relogin mentre io aspettavo
            // quindi prima guardo nello store
            // NB: qui non hai accesso al SessionStore, quindi facciamo solo relogin "vero".
            // La logica di riuso token la puoi fare direttamente nel SsoService (vedi nota sotto).
            return await _sso.ReLoginFromStoredCredentialsAsync(displayName);
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task<(T Result, string? Error, bool ShouldRetry)> CallOnceAsync<T>(
        string appKey,
        string sessionToken,
        object rpcRequest)
        where T : class
    {
        var url = "https://api.betfair.com/exchange/betting/json-rpc/v1";
        var json = JsonSerializer.Serialize(rpcRequest);

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Add("X-Application", appKey);
        req.Headers.Add("X-Authentication", sessionToken);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var res = await _http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync() ?? "";
        var ct = res.Content.Headers.ContentType?.ToString() ?? "(no content-type)";

        static string Trunc(string s, int max = 500)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }

        // HTTP 401/403 -> quasi sempre token scaduto / non valido
        if (res.StatusCode == HttpStatusCode.Unauthorized || res.StatusCode == HttpStatusCode.Forbidden)
        {
            // retry
            return (null!, $"HTTP {(int)res.StatusCode} {res.StatusCode}", true);
        }

        // 1) altri HTTP error
        if (!res.IsSuccessStatusCode)
            return (null!, $"HTTP {(int)res.StatusCode} {res.StatusCode} (CT={ct}) — {Trunc(body)}", false);

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var trimmed = body.TrimStart();

        static (T Result, string? Error, bool ShouldRetry) MapRpc(BetfairRpcResponse<T>? rpc)
        {
            if (rpc == null)
                return (null!, "Risposta Betfair non interpretabile (RPC null).", false);

            if (rpc.error != null)
            {
                var codeStr = rpc.error.code.ToString();
                var msg = string.IsNullOrWhiteSpace(rpc.error.message) ? "RPC error" : rpc.error.message!;
                var err = $"BETFAIR RPC ERROR: {codeStr} - {msg}";

                // session expired?
                return (null!, err, IsSessionExpired(err));
            }

            if (rpc.result == null)
                return (null!, "Risposta Betfair vuota (result null).", false);

            return (rpc.result, null, false);
        }

        // 2) Parse robusto
        try
        {
            if (trimmed.StartsWith("["))
            {
                var arr = JsonSerializer.Deserialize<BetfairRpcResponse<T>[]>(body, opts);
                var first = arr?.FirstOrDefault();

                if (first == null)
                    return (null!, "Risposta Betfair vuota (batch JSON-RPC).", false);

                return MapRpc(first);
            }
            else
            {
                var rpc = JsonSerializer.Deserialize<BetfairRpcResponse<T>>(body, opts);
                return MapRpc(rpc);
            }
        }
        catch (Exception ex)
        {
            // log dettagliato
            Console.WriteLine("=== BETFAIR JSON PARSE FAILED ===");
            Console.WriteLine($"CT={ct}");
            Console.WriteLine($"Exception: {ex.Message}");
            Console.WriteLine($"BODY: {Trunc(body, 2000)}");
            Console.WriteLine("=== END ===");

            // UI user-friendly (non retry: qui non sappiamo se è sessione)
            return (null!, "Risposta Betfair non interpretabile (errore parsing). Controlla i log server.", false);
        }
    }

    private static bool IsSessionExpired(string? err)
    {
        if (string.IsNullOrWhiteSpace(err)) return false;

        err = err.ToUpperInvariant();

        // NB: includo match "robusti"
        return err.Contains("INVALID_SESSION")
            || err.Contains("NO_SESSION")
            || err.Contains("UNAUTHORIZED")
            || err.Contains("NOT_AUTHORIZED")
            || err.Contains("TOKEN")
            || err.Contains("SESSION")
            || err.Contains("ANGX-0003");
    }

    // ====== PUBLIC API METHODS (aggiunto displayName) ======

    public Task<(CurrentOrderSummaryReport Result, string? Error)> ListCurrentOrdersAsync(
        string displayName,
        string appKey,
        string sessionToken,
        string betId)
    {
        var rpc = new BetfairRpcRequest<ListCurrentOrdersParams>
        {
            method = "SportsAPING/v1.0/listCurrentOrders",
            @params = new ListCurrentOrdersParams
            {
                betIds = new List<string> { betId },
                recordCount = 1
            },
            id = 1
        };

        return CallAsync<CurrentOrderSummaryReport>(displayName, appKey, sessionToken, rpc);
    }

    public class CancelOrdersParams
    {
        public string? betId { get; set; }
        public string? marketId { get; set; }
    }

    public class CancelExecutionReport
    {
        public string? status { get; set; }   // SUCCESS / FAILURE
        public string? errorCode { get; set; }
    }

    public Task<(PlaceExecutionReport Result, string? Error)> PlaceOrdersAsync(
        string displayName,
        string appKey,
        string sessionToken,
        PlaceOrdersParams placeParams)
    {
        var rpc = new BetfairRpcRequest<PlaceOrdersParams>
        {
            method = "SportsAPING/v1.0/placeOrders",
            @params = placeParams,
            id = 1
        };

        return CallAsync<PlaceExecutionReport>(displayName, appKey, sessionToken, rpc);
    }

    public Task<(List<EventTypeResult> Result, string? Error)> ListEventTypesAsync(
        string displayName,
        string appKey,
        string sessionToken)
    {
        var rpc = new BetfairRpcRequest<MarketFilter>
        {
            method = "SportsAPING/v1.0/listEventTypes",
            @params = new MarketFilter(),
            id = 1
        };

        return CallAsync<List<EventTypeResult>>(displayName, appKey, sessionToken, rpc);
    }

    public Task<(List<MarketBook> Result, string? Error)> ListMarketBookAsync(
        string displayName,
        string appKey,
        string sessionToken,
        string marketId)
    {
        var rpc = new BetfairRpcRequest<ListMarketBookParams>
        {
            method = "SportsAPING/v1.0/listMarketBook",
            @params = new ListMarketBookParams
            {
                marketIds = new List<string> { marketId },
                priceProjection = new PriceProjection
                {
                    priceData = new HashSet<string> { "EX_BEST_OFFERS" }
                }
            },
            id = 1
        };

        return CallAsync<List<MarketBook>>(displayName, appKey, sessionToken, rpc);
    }

    public Task<(List<EventResult> Result, string? Error)> ListEventsAsync(
        string displayName,
        string appKey,
        string sessionToken,
        string eventTypeId,
        string textQuery,
        DateTime from,
        DateTime to)
    {
        var rpc = new BetfairRpcRequest<ListEventsParams>
        {
            method = "SportsAPING/v1.0/listEvents",
            @params = new ListEventsParams
            {
                filter = new MarketFilter
                {
                    eventTypeIds = new HashSet<string> { eventTypeId },
                    textQuery = string.IsNullOrWhiteSpace(textQuery) ? null : textQuery,
                    marketStartTime = new TimeRange { from = from, to = to }
                }
            },
            id = 1
        };

        return CallAsync<List<EventResult>>(displayName, appKey, sessionToken, rpc);
    }

    public Task<(List<MarketCatalogue> Result, string? Error)> GetMarketCatalogueByMarketIdAsync(
        string displayName,
        string appKey,
        string sessionToken,
        string marketId)
    {
        var rpc = new BetfairRpcRequest<ListMarketCatalogueParams>
        {
            method = "SportsAPING/v1.0/listMarketCatalogue",
            @params = new ListMarketCatalogueParams
            {
                filter = new MarketFilter
                {
                    marketIds = new HashSet<string> { marketId }
                },
                marketProjection = new HashSet<string> { "RUNNER_DESCRIPTION" },
                sort = "FIRST_TO_START",
                maxResults = 1
            },
            id = 1
        };

        return CallAsync<List<MarketCatalogue>>(displayName, appKey, sessionToken, rpc);
    }

    public Task<(List<MarketCatalogue> Result, string? Error)> ListAllMarketsByEventAsync(
        string displayName,
        string appKey,
        string sessionToken,
        string eventId,
        int maxResults = 200)
    {
        var rpc = new BetfairRpcRequest<ListMarketCatalogueParams>
        {
            method = "SportsAPING/v1.0/listMarketCatalogue",
            @params = new ListMarketCatalogueParams
            {
                filter = new MarketFilter
                {
                    eventIds = new HashSet<string> { eventId }
                },
                marketProjection = new HashSet<string> { "RUNNER_DESCRIPTION", "MARKET_START_TIME" },
                sort = "FIRST_TO_START",
                maxResults = maxResults
            },
            id = 1
        };

        return CallAsync<List<MarketCatalogue>>(displayName, appKey, sessionToken, rpc);
    }

    public Task<(List<MarketCatalogue> Result, string? Error)> ListMatchOddsMarketsAsync(
        string displayName,
        string appKey,
        string sessionToken,
        string eventId)
    {
        var rpc = new BetfairRpcRequest<ListMarketCatalogueParams>
        {
            method = "SportsAPING/v1.0/listMarketCatalogue",
            @params = new ListMarketCatalogueParams
            {
                filter = new MarketFilter
                {
                    eventIds = new HashSet<string> { eventId },
                    marketTypeCodes = new HashSet<string> { "MATCH_ODDS" }
                },
                marketProjection = new HashSet<string> { "RUNNER_DESCRIPTION", "MARKET_START_TIME" },
                sort = "FIRST_TO_START",
                maxResults = 50
            },
            id = 1
        };

        return CallAsync<List<MarketCatalogue>>(displayName, appKey, sessionToken, rpc);
    }

    public Task<(CancelExecutionReport Result, string? Error)> CancelOrderAsync(
        string displayName,
        string appKey,
        string sessionToken,
        string betId)
    {
        var rpc = new BetfairRpcRequest<CancelOrdersParams>
        {
            method = "SportsAPING/v1.0/cancelOrders",
            @params = new CancelOrdersParams
            {
                betId = betId
            },
            id = 1
        };

        return CallAsync<CancelExecutionReport>(displayName, appKey, sessionToken, rpc);
    }
}
