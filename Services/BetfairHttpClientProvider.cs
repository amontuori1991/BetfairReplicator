using System.Net;
using System.Security.Authentication;

namespace BetfairReplicator.Services;

public class BetfairHttpClientProvider
{
    private readonly BetfairCertificateProvider _certs;
    private readonly Dictionary<string, HttpClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public BetfairHttpClientProvider(BetfairCertificateProvider certs)
    {
        _certs = certs;
    }

    public async Task<HttpClient> GetAsync(string displayName)
    {
        if (_clients.TryGetValue(displayName, out var existing))
            return existing;

        var cert = await _certs.GetAsync(displayName);

        var handler = new HttpClientHandler
        {
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        handler.ClientCertificates.Add(cert);

        var http = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        _clients[displayName] = http;
        return http;
    }
}
