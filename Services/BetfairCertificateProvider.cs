using System.Security.Cryptography.X509Certificates;

namespace BetfairReplicator.Services;

public class BetfairCertificateProvider
{
    private readonly BetfairAccountStoreFile _accounts;
    private readonly Dictionary<string, X509Certificate2> _cache = new(StringComparer.OrdinalIgnoreCase);

    public BetfairCertificateProvider(BetfairAccountStoreFile accounts)
    {
        _accounts = accounts;
    }

    public async Task<X509Certificate2> GetAsync(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new InvalidOperationException("DisplayName mancante per il certificato.");

        if (_cache.TryGetValue(displayName, out var cached))
            return cached;

        var rec = await _accounts.GetAsync(displayName)
                  ?? throw new InvalidOperationException($"Account '{displayName}' non trovato.");

        var secrets = _accounts.UnprotectSecrets(rec);
        var b64 = secrets.P12Base64;
        var pwd = secrets.P12Password;

        if (string.IsNullOrWhiteSpace(b64))
            throw new InvalidOperationException($"Missing P12Base64 for '{displayName}'.");
        if (pwd is null)
            throw new InvalidOperationException($"Missing P12Password for '{displayName}'.");

        var pfxBytes = Convert.FromBase64String(b64);

        var cert = new X509Certificate2(
            pfxBytes,
            pwd,
            X509KeyStorageFlags.EphemeralKeySet
        );

        _cache[displayName] = cert;
        return cert;
    }
}
