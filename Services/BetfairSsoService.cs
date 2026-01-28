using System.Net.Http.Headers;
using System.Text.Json;
using System.Web;
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
        var url = "https://identitysso.betfair.it/api/login";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);

        // Betfair SSO spesso risponde text/plain: "status=SUCCESS&token=..."
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        // Header richiesti
        req.Headers.Add("X-Application", appKey);

        // Alcuni gateway/WAF possono rispondere HTML se manca User-Agent
        req.Headers.UserAgent.ParseAdd("BetfairReplicator/1.0 (+https://localhost)");

        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = password
        });

        var res = await _http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        var ct = res.Content.Headers.ContentType?.ToString() ?? "(no content-type)";

        // DEBUG (Fly: fly logs)
        Console.WriteLine("=== BETFAIR SSO RESPONSE ===");
        Console.WriteLine($"HTTP {(int)res.StatusCode} {res.StatusCode}");
        Console.WriteLine($"Content-Type: {ct}");
        Console.WriteLine($"X-Application length: {(string.IsNullOrWhiteSpace(appKey) ? 0 : appKey.Length)}");
        Console.WriteLine(body.Length > 800 ? body.Substring(0, 800) : body);
        Console.WriteLine("=== END RESPONSE ===");

        // Se torna HTML, non è risposta API
        if (body.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase))
        {
            return new BetfairLoginResponse
            {
                status = "FAIL",
                error = $"HTML_RESPONSE (HTTP {(int)res.StatusCode})"
            };
        }

        // 1) Tentativo: querystring classico (status=...&token=...)
        {
            var parsed = HttpUtility.ParseQueryString(body);
            var status = parsed["status"];
            var token = parsed["token"];
            var error = parsed["error"];

            if (!string.IsNullOrWhiteSpace(status))
            {
                return new BetfairLoginResponse
                {
                    status = status,
                    token = token,
                    error = error
                };
            }
        }

        // 2) Tentativo: stessa cosa ma separata da newline (capita con alcuni proxy)
        //    Esempio: "status=SUCCESS\ntoken=...."
        {
            var normalized = body.Replace("\r\n", "&").Replace("\n", "&").Replace("\r", "&");
            var parsed = HttpUtility.ParseQueryString(normalized);
            var status = parsed["status"];
            var token = parsed["token"];
            var error = parsed["error"];

            if (!string.IsNullOrWhiteSpace(status))
            {
                return new BetfairLoginResponse
                {
                    status = status,
                    token = token,
                    error = error
                };
            }
        }

        // 3) Tentativo: JSON
        try
        {
            var obj = JsonSerializer.Deserialize<BetfairLoginResponse>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (obj != null && !string.IsNullOrWhiteSpace(obj.status))
                return obj;
        }
        catch
        {
            // ignore -> fallback sotto
        }

        // Fallback finale
        return new BetfairLoginResponse
        {
            status = "FAIL",
            error = $"UNEXPECTED_RESPONSE (HTTP {(int)res.StatusCode}, CT={ct})"
        };
    }
}
