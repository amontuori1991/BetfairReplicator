using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BetfairReplicator.Models;

namespace BetfairReplicator.Services;

public class BetfairBettingApiService
{
    private readonly HttpClient _http;

    public BetfairBettingApiService(IHttpClientFactory httpFactory)
    {
        _http = httpFactory.CreateClient("Betfair");
        _http.Timeout = TimeSpan.FromSeconds(30);
    }


    // NOTA: niente T? qui (evita CS8978)
    protected async Task<(T Result, string? Error)> CallAsync<T>(
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
        var body = await res.Content.ReadAsStringAsync();
        var ct = res.Content.Headers.ContentType?.ToString() ?? "(no content-type)";

        // Helper per mostrare un body "breve" nell'errore (senza spaccare UI/log)
        static string Trunc(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= 500 ? s : s.Substring(0, 500) + "...";
        }

        // Se status non OK, mostra body
        if (!res.IsSuccessStatusCode)
            return (null!, $"HTTP {(int)res.StatusCode} {res.StatusCode} CT={ct} BODY='{Trunc(body)}'");

        // 1) prova a parse come oggetto singolo
        try
        {
            var parsed = JsonSerializer.Deserialize<BetfairRpcResponse<T>>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (parsed?.error != null)
            {
                var code = parsed.error.code.ToString(); // se code è int
                return (null!, $"RPC_ERROR: {parsed.error.message ?? code ?? "unknown"} | BODY='{Trunc(body)}'");
            }


            if (parsed?.result == null)
                return (null!, $"EMPTY_RESULT | CT={ct} BODY='{Trunc(body)}'");

            return (parsed.result, null);
        }
        catch
        {
            // ignore, proviamo array
        }

        // 2) prova a parse come array (batch JSON-RPC)
        try
        {
            var arr = JsonSerializer.Deserialize<BetfairRpcResponse<T>[]>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var first = arr?.FirstOrDefault();

            if (first == null)
                return (null!, $"JSON_PARSE_FAILED (empty array) CT={ct} BODY='{Trunc(body)}'");

            if (first.error != null)
            {
                var code = first.error.code.ToString(); // se code è int
                return (null!, $"RPC_ERROR: {first.error.message ?? code ?? "unknown"} | BODY='{Trunc(body)}'");
            }


            if (first.result == null)
                return (null!, $"EMPTY_RESULT (array) | CT={ct} BODY='{Trunc(body)}'");

            return (first.result, null);
        }
        catch (Exception ex)
        {
            return (null!, $"JSON_PARSE_FAILED: {ex.Message} | CT={ct} BODY='{Trunc(body)}'");
        }
    }

    private static bool IsSessionExpired(string? err)
    {
        if (string.IsNullOrWhiteSpace(err)) return false;

        // Betfair tipicamente usa questi codici/messaggi quando il token non è più valido.
        // Non possiamo garantire il testo identico, quindi facciamo check "robusti".
        err = err.ToUpperInvariant();

        return err.Contains("INVALID_SESSION")
            || err.Contains("NO_SESSION")
            || err.Contains("SESSION")
            || err.Contains("TOKEN")
            || err.Contains("ANGX-0003");
    }

    public Task<(CurrentOrderSummaryReport Result, string? Error)> ListCurrentOrdersAsync(
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

        return CallAsync<CurrentOrderSummaryReport>(appKey, sessionToken, rpc);
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

        return CallAsync<PlaceExecutionReport>(appKey, sessionToken, rpc);
    }

    public Task<(List<EventTypeResult> Result, string? Error)> ListEventTypesAsync(string appKey, string sessionToken)
    {
        var rpc = new BetfairRpcRequest<MarketFilter>
        {
            method = "SportsAPING/v1.0/listEventTypes",
            @params = new MarketFilter(),
            id = 1
        };

        return CallAsync<List<EventTypeResult>>(appKey, sessionToken, rpc);
    }
    public Task<(List<MarketBook> Result, string? Error)> ListMarketBookAsync(
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

        return CallAsync<List<MarketBook>>(appKey, sessionToken, rpc);
    }

    public Task<(List<EventResult> Result, string? Error)> ListEventsAsync(
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

        return CallAsync<List<EventResult>>(appKey, sessionToken, rpc);
    }
    public Task<(List<MarketCatalogue> Result, string? Error)> GetMarketCatalogueByMarketIdAsync(
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

        return CallAsync<List<MarketCatalogue>>(appKey, sessionToken, rpc);
    }
    public Task<(List<MarketCatalogue> Result, string? Error)> ListAllMarketsByEventAsync(
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
                    // niente marketTypeCodes => ritorna tutti i mercati
                },
                marketProjection = new HashSet<string> { "RUNNER_DESCRIPTION", "MARKET_START_TIME" },
                sort = "FIRST_TO_START",
                maxResults = maxResults
            },
            id = 1
        };

        return CallAsync<List<MarketCatalogue>>(appKey, sessionToken, rpc);
    }

    public Task<(List<MarketCatalogue> Result, string? Error)> ListMatchOddsMarketsAsync(
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

        return CallAsync<List<MarketCatalogue>>(appKey, sessionToken, rpc);
    }
    public Task<(CancelExecutionReport Result, string? Error)> CancelOrderAsync(
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

        return CallAsync<CancelExecutionReport>(appKey, sessionToken, rpc);
    }


}
