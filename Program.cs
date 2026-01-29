using BetfairReplicator.Options;
using BetfairReplicator.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using System.IO;

namespace BetfairReplicator
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // ✅ Razor Pages + protezione folder Admin
            builder.Services.AddRazorPages(o =>
            {
                o.Conventions.AuthorizeFolder("/Admin");
                o.Conventions.AllowAnonymousToPage("/Admin/Login");
            });

            // ✅ DataProtection (persistente su VPS /data)
            var dp = builder.Services.AddDataProtection();
            if (Directory.Exists("/data"))
            {
                dp.PersistKeysToFileSystem(new DirectoryInfo("/data/dpkeys"));
            }
            builder.Services.AddHttpClient();
            builder.Services.Configure<BetfairOptions>(
                builder.Configuration.GetSection("Betfair"));

            // ✅ Store sessioni (già ok)
            builder.Services.AddSingleton<BetfairSessionStoreFile>();

            // ✅ Store account + per-account cert/client
            builder.Services.AddSingleton<BetfairAccountStoreFile>();
            builder.Services.AddSingleton<BetfairCertificateProvider>();
            builder.Services.AddSingleton<BetfairHttpClientProvider>();

            // ✅ Auth admin (cookie)
            builder.Services
                .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(o =>
                {
                    o.LoginPath = "/Admin/Login";
                    o.LogoutPath = "/Admin/Logout";
                    o.AccessDeniedPath = "/Admin/Login";
                    o.Cookie.Name = "BetfairReplicator.Admin";
                    o.Cookie.HttpOnly = true;
                    o.SlidingExpiration = true;
                    o.ExpireTimeSpan = TimeSpan.FromHours(12);
                });

            builder.Services.AddAuthorization();

            // Servizi applicativi
            builder.Services.AddScoped<BetfairSsoService>();
            builder.Services.AddScoped<BetfairAccountApiService>();
            builder.Services.AddScoped<BetfairBettingApiService>();

            var app = builder.Build();

            // Seed displayName da appsettings (senza secrets)
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

            // Se fai reverse proxy dopo, puoi tenere. Per ora ok.
            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            // ✅ auth prima di authorization
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapRazorPages();

            app.Run();
        }
    }
}
