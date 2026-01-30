using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BetfairReplicator.Models;

namespace BetfairReplicator.Services;

public class BetfairAccountApiService
{
    private readonly HttpClient _http;
    private readonly BetfairSsoService _sso;

    public BetfairAccountApiService(IHttpClientFactory httpFactory, BetfairSsoService sso)
    {
        _http = httpFactory.CreateClient("Betfair");
        _http.Timeout = TimeSpan.FromSeconds(30);
        _sso = sso;
    }

    public async Task<(BetfairAccountFundsResult? Result, string? Error)> GetAccountFundsAsync(
        string displayName,
        string appKey,
        string sessionToken)
    {
        var (res1, err1, retry) = await GetAccountFundsOnceAsync(appKey, sessionToken);

        if (retry)
        {
            var (newToken, relogErr) = await _sso.ReLoginFromStoredCredentialsAsync(displayName);
            if (relogErr != null || string.IsNullOrWhiteSpace(newToken))
                return (null, $"RELOGIN_FAILED: {relogErr ?? "unknown"}");


            var (res2, err2, _) = await GetAccountFundsOnceAsync(appKey, newToken);
            return (res2, err2);
        }

        return (res1, err1);
    }

    private async Task<(BetfairAccountFundsResult? Result, string? Error, bool ShouldRetry)> GetAccountFundsOnceAsync(
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
        var body = await res.Content.ReadAsStringAsync() ?? "";
        var ct = res.Content.Headers.ContentType?.ToString() ?? "(no content-type)";

        // 401/403 = token non valido/scaduto (retry)
        if (res.StatusCode == HttpStatusCode.Unauthorized || res.StatusCode == HttpStatusCode.Forbidden)
            return (null, $"HTTP {(int)res.StatusCode} {res.StatusCode} (CT={ct})", true);

        if (!res.IsSuccessStatusCode)
            return (null, $"HTTP {(int)res.StatusCode} {res.StatusCode} (CT={ct})", false);

        try
        {
            var parsed = JsonSerializer.Deserialize<BetfairRpcResponse<BetfairAccountFundsResult>>(
                body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (parsed?.error != null)
            {
                var normalized = NormalizeBetfairError(body, parsed.error.message ?? "BETFAIR_ERROR");

                // token scaduto -> retry
                if (LooksLikeSessionExpired(normalized) || LooksLikeSessionExpired(body))
                    return (null, "INVALID_SESSION", true);

                return (null, normalized, false);
            }

            if (parsed?.result == null)
                return (null, "Risposta vuota da Betfair", false);

            return (parsed.result, null, false);
        }
        catch (Exception ex)
        {
            Console.WriteLine("=== BETFAIR ACCOUNT JSON PARSE FAILED ===");
            Console.WriteLine($"CT={ct}");
            Console.WriteLine($"Exception: {ex.Message}");
            Console.WriteLine(body.Length > 1200 ? body.Substring(0, 1200) : body);
            Console.WriteLine("=== END ===");

            // non possiamo sapere se è sessione -> niente retry qui
            return (null, "Risposta Betfair non interpretabile (account parsing). Controlla i log server.", false);
        }
    }

    private static string NormalizeBetfairError(string rawBody, string fallbackMessage)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawBody);

            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("message", out var m) ? m.GetString() : null;

                string? errorCode = null;
                if (err.TryGetProperty("data", out var data))
                {
                    if (data.TryGetProperty("APINGException", out var aping)
                        && aping.TryGetProperty("errorCode", out var ec1))
                        errorCode = ec1.GetString();

                    if (errorCode == null && data.TryGetProperty("exceptionname", out var exn))
                        errorCode = exn.GetString();

                    if (errorCode == null && data.TryGetProperty("errorCode", out var ec2))
                        errorCode = ec2.GetString();
                }

                var merged = $"{errorCode ?? ""} {msg ?? ""}".Trim();

                if (LooksLikeSessionExpired(merged) || LooksLikeSessionExpired(rawBody))
                    return "INVALID_SESSION";

                if (!string.IsNullOrWhiteSpace(merged))
                    return merged;

                return fallbackMessage;
            }
        }
        catch { /* ignore */ }

        if (LooksLikeSessionExpired(rawBody) || LooksLikeSessionExpired(fallbackMessage))
            return "INVALID_SESSION";

        return fallbackMessage;
    }

    private static bool LooksLikeSessionExpired(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var up = text.ToUpperInvariant();

        return up.Contains("INVALID_SESSION")
            || up.Contains("NO_SESSION")
            || up.Contains("SESSION_EXPIRED")
            || up.Contains("UNAUTHORIZED")
            || up.Contains("NOT_AUTHORIZED")
            || up.Contains("EXPIRED")
            || up.Contains("TOKEN")
            || up.Contains("ANGX-0003");
    }
}
