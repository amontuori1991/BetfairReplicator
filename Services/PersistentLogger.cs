using System.Text;

namespace BetfairReplicator.Services;

public static class PersistentLogger
{
    private static readonly object _lock = new();

    public static void Log(string message)
    {
        try
        {
            var basePath = Directory.Exists("/data")
                ? "/data/logs"
                : Path.Combine(AppContext.BaseDirectory, "App_Data", "logs");

            Directory.CreateDirectory(basePath);

            var filePath = Path.Combine(basePath, "betfair-errors.log");

            var sb = new StringBuilder();
            sb.AppendLine("========================================");
            sb.AppendLine(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"));
            sb.AppendLine(message);
            sb.AppendLine();

            lock (_lock)
            {
                File.AppendAllText(filePath, sb.ToString());
            }
        }
        catch
        {
            // MAI lanciare eccezioni dal logger
        }
    }
}
