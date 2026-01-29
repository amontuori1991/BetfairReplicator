using System.Net.Http.Headers;
using System.Text.Json;
using System.Web;
using BetfairReplicator.Models;

namespace BetfairReplicator.Services;

public class BetfairSsoService
{
    private readonly BetfairHttpClientProvider _httpProvider;
    private readonly BetfairAccountStoreFile _accounts;

    public BetfairSsoService(BetfairHttpClientProvider httpProvider, BetfairAccountStoreFile accounts)
    {
        _httpProvider = httpProvider;
        _accounts = accounts;
    }

    public async Task<BetfairLoginResponse> LoginItalyAsync(string displayName, string username, string password)
    {
        var rec = await _accounts.GetAsync(displayName);
        if (rec is null)
        {
            return new BetfairLoginResponse { status = "FAIL", error = $"ACCOUNT_NOT_FOUND ({displayName})" };
        }

        var appKey = rec.AppKeyDelayed;

        if (string.IsNullOrWhiteSpace(appKey) || appKey.Contains("*"))
        {
            return new BetfairLoginResponse
            {
                status = "FAIL",
                error = "APPKEY_MISSING_OR_MASKED"
            };
        }

        var url = "https://identitysso-cert.betfair.it/api/certlogin";
        Console.WriteLine($"SSO URL: {url}");

        var http = await _httpProvider.GetAsync(displayName);

        using var req = new HttpRequestMessage(HttpMethod.Post, url);

        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        req.Headers.Add("X-Application", appKey);
        req.Headers.UserAgent.ParseAdd("BetfairReplicator/1.0 (+https://localhost)");

        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = password
        });

        var res = await http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        var ct = res.Content.Headers.ContentType?.ToString() ?? "(no content-type)";

        Console.WriteLine("=== BETFAIR SSO RESPONSE ===");
        Console.WriteLine($"HTTP {(int)res.StatusCode} {res.StatusCode}");
        Console.WriteLine($"Content-Type: {ct}");
        Console.WriteLine($"X-Application length: {(string.IsNullOrWhiteSpace(appKey) ? 0 : appKey.Length)}");
        Console.WriteLine(body.Length > 800 ? body.Substring(0, 800) : body);
        Console.WriteLine("=== END RESPONSE ===");

        if (body.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase))
        {
            return new BetfairLoginResponse
            {
                status = "FAIL",
                error = $"HTML_RESPONSE (HTTP {(int)res.StatusCode})"
            };
        }

        // 1) querystring
        {
            var parsed = HttpUtility.ParseQueryString(body);
            var status = parsed["status"];
            var token = parsed["token"];
            var error = parsed["error"];

            if (!string.IsNullOrWhiteSpace(status))
            {
                return new BetfairLoginResponse { status = status, token = token, error = error };
            }
        }

        // 2) newline
        {
            var normalized = body.Replace("\r\n", "&").Replace("\n", "&").Replace("\r", "&");
            var parsed = HttpUtility.ParseQueryString(normalized);
            var status = parsed["status"];
            var token = parsed["token"];
            var error = parsed["error"];

            if (!string.IsNullOrWhiteSpace(status))
            {
                return new BetfairLoginResponse { status = status, token = token, error = error };
            }
        }

        // 3) JSON
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
            // ignore
        }

        return new BetfairLoginResponse
        {
            status = "FAIL",
            error = $"UNEXPECTED_RESPONSE (HTTP {(int)res.StatusCode}, CT={ct})"
        };
    }
}
