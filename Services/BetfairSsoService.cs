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
    private readonly BetfairSessionStoreFile _sessions;

    public BetfairSsoService(
        BetfairHttpClientProvider httpProvider,
        BetfairAccountStoreFile accounts,
        BetfairSessionStoreFile sessions)
    {
        _httpProvider = httpProvider;
        _accounts = accounts;
        _sessions = sessions;
    }

    /// <summary>
    /// Login SSO cert (Italia) usando username/password passati.
    /// NOTA: questo metodo NON salva automaticamente il token nello store: lo fa chi chiama.
    /// </summary>
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

        var body = Normalize(bodyRaw);

        // 1) Querystring (robusto)
        {
            var parsed = HttpUtility.ParseQueryString(body);
            var status = parsed["status"] ?? parsed["Status"] ?? parsed["STATUS"];
            var token = parsed["token"] ?? parsed["Token"] ?? parsed["TOKEN"];
            var error = parsed["error"] ?? parsed["Error"] ?? parsed["ERROR"];

            if (!string.IsNullOrWhiteSpace(status))
                return new BetfairLoginResponse { status = status, token = token, error = error };
        }

        // 2) Regex robusta
        {
            string? status = FindKey(body, "status");
            string? token = FindKey(body, "token");
            string? error = FindKey(body, "error");

            if (!string.IsNullOrWhiteSpace(status))
                return new BetfairLoginResponse { status = status, token = token, error = error };
        }

        // 3) JSON (2 formati)
        try
        {
            var obj = JsonSerializer.Deserialize<BetfairLoginResponse>(bodyRaw, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (obj != null && !string.IsNullOrWhiteSpace(obj.status))
                return obj;

            var certObj = JsonSerializer.Deserialize<BetfairCertLoginJson>(bodyRaw, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (certObj != null && !string.IsNullOrWhiteSpace(certObj.loginStatus))
            {
                return new BetfairLoginResponse
                {
                    status = certObj.loginStatus,
                    token = certObj.sessionToken,
                    error = null
                };
            }
        }
        catch
        {
            // ignore
        }

        var snippet = bodyRaw;
        if (snippet.Length > 180) snippet = snippet.Substring(0, 180) + "...";

        return new BetfairLoginResponse
        {
            status = "FAIL",
            error = $"UNEXPECTED_RESPONSE (HTTP {(int)res.StatusCode}, CT={ct}, BODY='{snippet}')"
        };
    }

    /// <summary>
    /// AUTO-RELOGIN: usa le credenziali salvate in BetfairAccountStoreFile (cifrate),
    /// effettua certlogin e salva il nuovo token in BetfairSessionStoreFile.
    /// </summary>
    public async Task<(string? Token, string? Error)> ReLoginFromStoredCredentialsAsync(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return (null, "displayName mancante");

        var rec = await _accounts.GetAsync(displayName);
        if (rec is null)
            return (null, $"ACCOUNT_NOT_FOUND ({displayName})");

        var (u, p, _, _) = _accounts.UnprotectSecrets(rec);

        if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(p))
            return (null, "CREDENZIALI_MANCANTI (username/password non presenti nello store)");

        var login = await LoginItalyAsync(displayName, u!, p!);

        // SUCCESS = token valido
        if (!string.Equals(login.status, "SUCCESS", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(login.token))
        {
            var why = login.error ?? "LOGIN_FAILED";
            return (null, why);
        }

        await _sessions.SetTokenAsync(displayName, login.token!);
        return (login.token, null);
    }

    private static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";

        s = s.Trim().TrimStart('\uFEFF', '\u200B');
        s = s.Replace(";", "&");
        s = s.Replace("\r\n", "&").Replace("\n", "&").Replace("\r", "&");
        s = s.Trim();

        return s;
    }

    private static string? FindKey(string body, string key)
    {
        var m = Regex.Match(body, $@"(?i)(?:^|[&\s]){Regex.Escape(key)}\s*=\s*([^&]+)");
        if (!m.Success) return null;

        var val = m.Groups[1].Value?.Trim();
        return string.IsNullOrWhiteSpace(val) ? null : val;
    }

    private sealed class BetfairCertLoginJson
    {
        public string? sessionToken { get; set; }
        public string? loginStatus { get; set; }
        public string? lastLoginDate { get; set; }
    }
}
