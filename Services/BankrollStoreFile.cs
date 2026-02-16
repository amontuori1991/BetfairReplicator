using System.Text.Json;

namespace BetfairReplicator.Services
{
    /// <summary>
    /// Salva bankroll iniziale per account in un file JSON.
    /// Path default: /data/bankroll.json se esiste /data, altrimenti nella content root.
    /// </summary>
    public class BankrollStoreFile
    {
        private readonly string _filePath;
        private static readonly SemaphoreSlim _lock = new(1, 1);

        public BankrollStoreFile(IWebHostEnvironment env)
        {
            // Se su VPS hai /data, meglio lì (persistente)
            var baseDir = Directory.Exists("/data") ? "/data" : env.ContentRootPath;
            _filePath = Path.Combine(baseDir, "bankroll.json");
        }

        public async Task<double?> GetAsync(string account)
        {
            if (string.IsNullOrWhiteSpace(account))
                return null;

            await _lock.WaitAsync();
            try
            {
                if (!File.Exists(_filePath))
                    return null;

                var json = await File.ReadAllTextAsync(_filePath);
                if (string.IsNullOrWhiteSpace(json))
                    return null;

                var dict = JsonSerializer.Deserialize<Dictionary<string, double>>(json)
                           ?? new Dictionary<string, double>();

                return dict.TryGetValue(account, out var v) ? v : null;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task SetAsync(string account, double bankroll)
        {
            if (string.IsNullOrWhiteSpace(account))
                return;

            if (bankroll < 0)
                bankroll = 0;

            await _lock.WaitAsync();
            try
            {
                Dictionary<string, double> dict;

                if (File.Exists(_filePath))
                {
                    var json = await File.ReadAllTextAsync(_filePath);
                    dict = string.IsNullOrWhiteSpace(json)
                        ? new Dictionary<string, double>()
                        : (JsonSerializer.Deserialize<Dictionary<string, double>>(json)
                           ?? new Dictionary<string, double>());
                }
                else
                {
                    dict = new Dictionary<string, double>();
                }

                dict[account] = bankroll;

                var outJson = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_filePath, outJson);
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
