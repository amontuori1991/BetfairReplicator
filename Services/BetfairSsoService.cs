using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
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
            return new BetfairLoginResponse { status = "FAIL", error = $"ACCOUNT_NOT_FOUND ({displayName})" };

        var appKey = rec.AppKeyDelayed;
        if (string.IsNullOrWhiteSpace(appKey) || appKey.Contains("*"))
            return new BetfairLoginResponse { status = "FAIL", error = "APPKEY_MISSING_OR_MASKED" };

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
            ["username"] = username ?? "",
            ["password"] = password ?? ""
        });

        var res = await http.SendAsync(req);
        var bodyRaw = await res.Content.ReadAsStringAsync();
        var ct = res.Content.Headers.ContentType?.ToString() ?? "(no content-type)";

        Console.WriteLine("=== BETFAIR SSO RESPONSE ===");
        Console.WriteLine($"HTTP {(int)res.StatusCode} {res.StatusCode}");
        Console.WriteLine($"Content-Type: {ct}");
        Console.WriteLine($"X-Application length: {(string.IsNullOrWhiteSpace(appKey) ? 0 : appKey.Length)}");
        Console.WriteLine(bodyRaw.Length > 800 ? bodyRaw.Substring(0, 800) : bodyRaw);
        Console.WriteLine("=== END RESPONSE ===");

        // HTML = non è risposta API
        if (bodyRaw.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
            bodyRaw.Contains("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase))
        {
            return new BetfairLoginResponse
            {
                status = "FAIL",
                error = $"HTML_RESPONSE (HTTP {(int)res.StatusCode})"
            };
        }

        // Normalizzazione aggressiva (BOM/spazi/; / newline)
        var body = Normalize(bodyRaw);

        // 1) Querystring via ParseQueryString (case-insensitive su chiavi non garantito)
        // quindi facciamo doppio tentativo: originale + lowercase
        {
            var parsed = HttpUtility.ParseQueryString(body);
            var status = parsed["status"] ?? parsed["Status"] ?? parsed["STATUS"];
            var token = parsed["token"] ?? parsed["Token"] ?? parsed["TOKEN"];
            var error = parsed["error"] ?? parsed["Error"] ?? parsed["ERROR"];

            if (!string.IsNullOrWhiteSpace(status))
                return new BetfairLoginResponse { status = status, token = token, error = error };
        }

        // 2) Regex robusta (prende status/token/error anche se separatori strani)
        {
            string? status = FindKey(body, "status");
            string? token = FindKey(body, "token");
            string? error = FindKey(body, "error");

            if (!string.IsNullOrWhiteSpace(status))
                return new BetfairLoginResponse { status = status, token = token, error = error };
        }

        // 3) JSON (alcuni gateway lo restituiscono così)
        try
        {
            var obj = JsonSerializer.Deserialize<BetfairLoginResponse>(bodyRaw, new JsonSerializerOptions
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

        // Fallback: includo snippet del body per capire cosa stiamo ricevendo
        var snippet = bodyRaw;
        if (snippet.Length > 180) snippet = snippet.Substring(0, 180) + "...";

        return new BetfairLoginResponse
        {
            status = "FAIL",
            error = $"UNEXPECTED_RESPONSE (HTTP {(int)res.StatusCode}, CT={ct}, BODY='{snippet}')"
        };
    }

    private static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";

        // rimuove BOM
        s = s.Trim().TrimStart('\uFEFF', '\u200B');

        // Betfair o proxy a volte usano ; come separatore
        s = s.Replace(";", "&");

        // newline -> &
        s = s.Replace("\r\n", "&").Replace("\n", "&").Replace("\r", "&");

        // alcuni proxy mettono spazi
        s = s.Trim();

        return s;
    }

    private static string? FindKey(string body, string key)
    {
        // cerca key=VALUE dove VALUE finisce su & o fine stringa
        // case-insensitive
        var m = Regex.Match(body, $@"(?i)(?:^|[&\s]){Regex.Escape(key)}\s*=\s*([^&]+)");
        if (!m.Success) return null;

        var val = m.Groups[1].Value?.Trim();
        return string.IsNullOrWhiteSpace(val) ? null : val;
    }
}
