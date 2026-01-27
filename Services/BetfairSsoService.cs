using System.Net.Http.Headers;
using System.Text.Json;
using BetfairReplicator.Models;

namespace BetfairReplicator.Services;

public class BetfairSsoService
{
    private readonly HttpClient _http;

    public BetfairSsoService(HttpClient http)
    {
        _http = http;
    }

    public async Task<BetfairLoginResponse> LoginItalyAsync(string appKey, string username, string password)
    {
        // Endpoint ITA
        var url = "https://identitysso.betfair.it/api/login";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Add("X-Application", appKey);

        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = password
        });

        var res = await _http.SendAsync(req);
        var json = await res.Content.ReadAsStringAsync();

        // Anche se c’è un errore, Betfair spesso risponde JSON. In caso contrario, fai fallback.
        try
        {
            return JsonSerializer.Deserialize<BetfairLoginResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new BetfairLoginResponse { status = "FAIL", error = "EMPTY_RESPONSE" };
        }
        catch
        {
            return new BetfairLoginResponse { status = "FAIL", error = "NON_JSON_RESPONSE" };
        }
    }
}
