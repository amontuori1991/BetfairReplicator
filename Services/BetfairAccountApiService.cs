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

        // 1) prova parse "tipizzato" (come facevi tu)
        try
        {
            var parsed = JsonSerializer.Deserialize<BetfairRpcResponse<BetfairAccountFundsResult>>(
                body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (parsed?.error != null)
            {
                // normalizzazione robusta
                var normalized = NormalizeBetfairError(body, parsed.error.message ?? "BETFAIR_ERROR");
                return (null, normalized);
            }

            if (parsed?.result == null)
                return (null, "Risposta vuota da Betfair");

            return (parsed.result, null);
        }
        catch
        {
            // 2) fallback: prova a capire se almeno è un errore di sessione
            var normalized = NormalizeBetfairError(body, "Risposta non valida (JSON) da Betfair");
            return (null, normalized);
        }
    }

    private static string NormalizeBetfairError(string rawBody, string fallbackMessage)
    {
        // Se Betfair risponde con errori noti, proviamo a estrarre un errorCode "stabile".
        // Se non troviamo nulla, ritorniamo il messaggio originale.

        try
        {
            using var doc = JsonDocument.Parse(rawBody);

            // Betfair JSON-RPC tipico: { "jsonrpc":"2.0","error":{ "code":-32099,"message":"....","data":{...}},"id":1 }
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                // message
                var msg = err.TryGetProperty("message", out var m) ? m.GetString() : null;

                // in alcuni casi c'è data.APINGException.errorCode oppure data.exceptionname ecc.
                string? errorCode = null;

                if (err.TryGetProperty("data", out var data))
                {
                    // data.APINGException.errorCode
                    if (data.TryGetProperty("APINGException", out var aping)
                        && aping.TryGetProperty("errorCode", out var ec1))
                        errorCode = ec1.GetString();

                    // data.exceptionname (alcune varianti)
                    if (errorCode == null && data.TryGetProperty("exceptionname", out var exn))
                        errorCode = exn.GetString();

                    // data.errorCode (varianti)
                    if (errorCode == null && data.TryGetProperty("errorCode", out var ec2))
                        errorCode = ec2.GetString();
                }

                var merged = $"{errorCode ?? ""} {msg ?? ""}".Trim();

                // Session invalidata/scaduta: normalizziamo
                if (LooksLikeSessionExpired(merged) || LooksLikeSessionExpired(rawBody))
                    return "INVALID_SESSION";

                // altrimenti ritorniamo qualcosa di utile
                if (!string.IsNullOrWhiteSpace(merged))
                    return merged;

                return fallbackMessage;
            }
        }
        catch
        {
            // ignore
        }

        // fallback: anche se non è JSON valido, cerchiamo pattern nel testo
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
            || up.Contains("EXPIRED")
            || up.Contains("TOKEN")
            || up.Contains("ANGX-0003");
    }
}
