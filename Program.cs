using BetfairReplicator.Services;
using BetfairReplicator.Options;
using Microsoft.AspNetCore.DataProtection;
using System.IO;

namespace BetfairReplicator
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorPages();

            // ✅ DataProtection: 1 sola volta
            // Su Fly persistiamo le chiavi nel volume /data (così i token restano decifrabili dopo restart/deploy)
            var dp = builder.Services.AddDataProtection();
            if (Directory.Exists("/data"))
            {
                dp.PersistKeysToFileSystem(new DirectoryInfo("/data/dpkeys"));
            }

            builder.Services.Configure<BetfairOptions>(
                builder.Configuration.GetSection("Betfair"));

            // ✅ Store token su file cifrato
            builder.Services.AddSingleton<BetfairSessionStoreFile>();

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
