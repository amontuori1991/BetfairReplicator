using System.Text.Json;

namespace BetfairReplicator.Services
{
    // Salva una data "From" per account (formato yyyy-MM-dd)
    public class FromDateStoreFile
    {
        private readonly string _path;

        public FromDateStoreFile(IWebHostEnvironment env)
        {
            var dir = Path.Combine(env.ContentRootPath, "App_Data");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "fromDates.json");
        }

        private async Task<Dictionary<string, string>> ReadAllAsync()
        {
            if (!File.Exists(_path)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var json = await File.ReadAllTextAsync(_path);
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>();

            return new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase);
        }

        private async Task WriteAllAsync(Dictionary<string, string> data)
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_path, json);
        }

        public async Task<string?> GetAsync(string account)
        {
            var all = await ReadAllAsync();
            return all.TryGetValue(account, out var v) ? v : null;
        }

        public async Task SetAsync(string account, string yyyyMmDd)
        {
            var all = await ReadAllAsync();
            all[account] = yyyyMmDd;
            await WriteAllAsync(all);
        }
    }
}
