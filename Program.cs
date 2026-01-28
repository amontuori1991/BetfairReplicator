using BetfairReplicator.Services;
using System.IO;
using Microsoft.AspNetCore.DataProtection;
namespace BetfairReplicator
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorPages();

            if (Directory.Exists("/data"))
            {
                builder.Services
                    .AddDataProtection()
                    .PersistKeysToFileSystem(new DirectoryInfo("/data/dpkeys"));
            }

            builder.Services.Configure<BetfairReplicator.Options.BetfairOptions>(
                builder.Configuration.GetSection("Betfair"));

            // ✅ Cifratura token (DataProtection)
            builder.Services.AddDataProtection();

            // ✅ Store token su file cifrato (App_Data/betfair-sessions.json)
            builder.Services.AddSingleton<BetfairSessionStoreFile>();

            // ❌ (per ora lasciamo questo commentato finché non lo sostituiamo ovunque)
            // builder.Services.AddSingleton<BetfairSessionStore>();

            builder.Services.AddHttpClient<BetfairSsoService>();
            builder.Services.AddHttpClient<BetfairAccountApiService>();
            builder.Services.AddHttpClient<BetfairBettingApiService>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
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
