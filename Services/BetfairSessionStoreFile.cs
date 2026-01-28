using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace BetfairReplicator.Services;

public class BetfairSessionStoreFile
{
    private readonly string _path;
    private readonly IDataProtector _protector;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public BetfairSessionStoreFile(IWebHostEnvironment env, IDataProtectionProvider dp)
    {
        // Su Fly abbiamo il volume montato su /data
        // In locale continuiamo a usare App_Data
        var flyDataDir = "/data";
        string dir;

        if (Directory.Exists(flyDataDir))
        {
            dir = flyDataDir;
        }
        else
        {
            dir = Path.Combine(env.ContentRootPath, "App_Data");
        }

        Directory.CreateDirectory(dir);

        _path = Path.Combine(dir, "betfair-sessions.json");
        _protector = dp.CreateProtector("BetfairSessionToken.v1");
    }

    public async Task SetTokenAsync(string displayName, string token)
    {
        await _lock.WaitAsync();
        try
        {
            var dict = await LoadAsync();
            dict[displayName] = _protector.Protect(token);
            await SaveAsync(dict);
        }
        finally { _lock.Release(); }
    }

    public async Task<string?> GetTokenAsync(string displayName)
    {
        await _lock.WaitAsync();
        try
        {
            var dict = await LoadAsync();
            if (!dict.TryGetValue(displayName, out var enc)) return null;

            try { return _protector.Unprotect(enc); }
            catch { return null; }
        }
        finally { _lock.Release(); }
    }

    public async Task RemoveTokenAsync(string displayName)
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

    private async Task<Dictionary<string, string>> LoadAsync()
    {
        if (!File.Exists(_path)) return new();

        var json = await File.ReadAllTextAsync(_path);
        if (string.IsNullOrWhiteSpace(json)) return new();

        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
               ?? new Dictionary<string, string>();
    }

    private async Task SaveAsync(Dictionary<string, string> dict)
    {
        var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_path, json);
    }
}
