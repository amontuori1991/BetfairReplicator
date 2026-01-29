using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace BetfairReplicator.Services;

public class BetfairAccountStoreFile
{
    private readonly string _path;
    private readonly IDataProtector _protector;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public BetfairAccountStoreFile(IWebHostEnvironment env, IDataProtectionProvider dp)
    {
        var flyDataDir = "/data";
        string dir = Directory.Exists(flyDataDir)
            ? flyDataDir
            : Path.Combine(env.ContentRootPath, "App_Data");

        Directory.CreateDirectory(dir);

        _path = Path.Combine(dir, "betfair-accounts.json");
        _protector = dp.CreateProtector("BetfairAccountStore.v1");
    }

    public sealed class BetfairAccountRecord
    {
        public string DisplayName { get; set; } = "";
        public string AppKeyDelayed { get; set; } = "";

        // Protetti (cifrati con DataProtection)
        public string? UsernameEnc { get; set; }
        public string? PasswordEnc { get; set; }
        public string? P12Base64Enc { get; set; }
        public string? P12PasswordEnc { get; set; }
    }

    public async Task<List<BetfairAccountRecord>> GetAllAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var dict = await LoadAsync();
            return dict.Values
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally { _lock.Release(); }
    }

    public async Task<BetfairAccountRecord?> GetAsync(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return null;

        await _lock.WaitAsync();
        try
        {
            var dict = await LoadAsync();
            dict.TryGetValue(displayName, out var rec);
            return rec;
        }
        finally { _lock.Release(); }
    }

    public async Task UpsertAsync(
        string displayName,
        string appKeyDelayed,
        string? username,
        string? password,
        string? p12Base64,
        string? p12Password)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("displayName mancante");

        await _lock.WaitAsync();
        try
        {
            var dict = await LoadAsync();

            dict[displayName] = new BetfairAccountRecord
            {
                DisplayName = displayName.Trim(),
                AppKeyDelayed = (appKeyDelayed ?? "").Trim(),

                UsernameEnc = string.IsNullOrWhiteSpace(username) ? null : _protector.Protect(username.Trim()),
                PasswordEnc = string.IsNullOrWhiteSpace(password) ? null : _protector.Protect(password),

                P12Base64Enc = string.IsNullOrWhiteSpace(p12Base64) ? null : _protector.Protect(p12Base64.Trim()),
                P12PasswordEnc = string.IsNullOrWhiteSpace(p12Password) ? null : _protector.Protect(p12Password),
            };

            await SaveAsync(dict);
        }
        finally { _lock.Release(); }
    }

    public async Task RemoveAsync(string displayName)
    {
        await _lock.WaitAsync();
        try
        {
            var dict = await LoadAsync();
            if (dict.Remove(displayName))
                await SaveAsync(dict);
        }
        finally { _lock.Release(); }
    }

    // Helper: se vuoi continuare ad avere la lista in appsettings, la “importiamo” una sola volta
    public async Task EnsureSeedFromOptionsAsync(IEnumerable<(string DisplayName, string AppKeyDelayed)> accounts)
    {
        await _lock.WaitAsync();
        try
        {
            var dict = await LoadAsync();
            var changed = false;

            foreach (var a in accounts)
            {
                if (string.IsNullOrWhiteSpace(a.DisplayName)) continue;

                if (!dict.TryGetValue(a.DisplayName, out var rec))
                {
                    dict[a.DisplayName] = new BetfairAccountRecord
                    {
                        DisplayName = a.DisplayName.Trim(),
                        AppKeyDelayed = (a.AppKeyDelayed ?? "").Trim()
                    };
                    changed = true;
                }
                else if (string.IsNullOrWhiteSpace(rec.AppKeyDelayed) && !string.IsNullOrWhiteSpace(a.AppKeyDelayed))
                {
                    rec.AppKeyDelayed = a.AppKeyDelayed.Trim();
                    dict[a.DisplayName] = rec;
                    changed = true;
                }
            }

            if (changed) await SaveAsync(dict);
        }
        finally { _lock.Release(); }
    }

    public (string? Username, string? Password, string? P12Base64, string? P12Password) UnprotectSecrets(BetfairAccountRecord rec)
    {
        string? Unp(string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            try { return _protector.Unprotect(v); } catch { return null; }
        }

        return (Unp(rec.UsernameEnc), Unp(rec.PasswordEnc), Unp(rec.P12Base64Enc), Unp(rec.P12PasswordEnc));
    }

    private async Task<Dictionary<string, BetfairAccountRecord>> LoadAsync()
    {
        if (!File.Exists(_path)) return new(StringComparer.OrdinalIgnoreCase);

        var json = await File.ReadAllTextAsync(_path);
        if (string.IsNullOrWhiteSpace(json)) return new(StringComparer.OrdinalIgnoreCase);

        return JsonSerializer.Deserialize<Dictionary<string, BetfairAccountRecord>>(json)
               ?? new Dictionary<string, BetfairAccountRecord>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task SaveAsync(Dictionary<string, BetfairAccountRecord> dict)
    {
        var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_path, json);
    }
}
