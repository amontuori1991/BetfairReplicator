using BetfairReplicator.Options;
using BetfairReplicator.Services;
using Microsoft.AspNetCore.DataProtection;
using System.IO;

namespace BetfairReplicator
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddRazorPages();

            // ✅ DataProtection (persistente su VPS /data)
            var dp = builder.Services.AddDataProtection();
            if (Directory.Exists("/data"))
            {
                dp.PersistKeysToFileSystem(new DirectoryInfo("/data/dpkeys"));
            }

            builder.Services.Configure<BetfairOptions>(
                builder.Configuration.GetSection("Betfair"));

            // ✅ Store sessioni (già ok)
            builder.Services.AddSingleton<BetfairSessionStoreFile>();

            // ✅ Store account + per-account cert/client
            builder.Services.AddSingleton<BetfairAccountStoreFile>();
            builder.Services.AddSingleton<BetfairCertificateProvider>();
            builder.Services.AddSingleton<BetfairHttpClientProvider>();

            // Servizi applicativi
            builder.Services.AddScoped<BetfairSsoService>();
            builder.Services.AddScoped<BetfairAccountApiService>();
            builder.Services.AddScoped<BetfairBettingApiService>();

            var app = builder.Build();

            // Se vuoi mantenere la lista displayName in appsettings,
            // la importiamo nel file store una sola volta (senza secrets).
            using (var scope = app.Services.CreateScope())
            {
                var opts = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<BetfairOptions>>().Value;
                var store = scope.ServiceProvider.GetRequiredService<BetfairAccountStoreFile>();

                store.EnsureSeedFromOptionsAsync(
                    opts.Accounts.Select(a => (a.DisplayName, a.AppKeyDelayed))
                ).GetAwaiter().GetResult();
            }

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();
            app.UseAuthorization();
            app.MapRazorPages();

            app.Run();
        }
    }
}
