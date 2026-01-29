using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BetfairReplicator.Models;

namespace BetfairReplicator.Services;

public class BetfairAccountApiService
{
    private readonly HttpClient _http;

    public BetfairAccountApiService(IHttpClientFactory httpFactory)
    {
        _http = httpFactory.CreateClient("Betfair");
        _http.Timeout = TimeSpan.FromSeconds(30);
    }


    public async Task<(BetfairAccountFundsResult? Result, string? Error)> GetAccountFundsAsync(
        string appKey,
        string sessionToken)
    {
        var url = "https://api.betfair.com/exchange/account/json-rpc/v1";

        var rpc = new BetfairRpcRequest<BetfairAccountFundsParams>
        {
            method = "AccountAPING/v1.0/getAccountFunds",
            @params = new BetfairAccountFundsParams { wallet = null },
            id = 1
        };

        var json = JsonSerializer.Serialize(rpc);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);

        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Add("X-Application", appKey);
        req.Headers.Add("X-Authentication", sessionToken);

        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var res = await _http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();

        try
        {
            var parsed = JsonSerializer.Deserialize<BetfairRpcResponse<BetfairAccountFundsResult>>(
                body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (parsed?.error != null)
                return (null, $"{parsed.error.message}");

            if (parsed?.result == null)
                return (null, "Risposta vuota da Betfair");

            return (parsed.result, null);
        }
        catch
        {
            return (null, "Risposta non valida (JSON) da Betfair");
        }
    }
}
