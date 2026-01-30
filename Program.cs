using BetfairReplicator.Options;
using BetfairReplicator.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using Microsoft.AspNetCore.Authorization;


namespace BetfairReplicator
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // ✅ Razor Pages + protezione folder Admin
            builder.Services.AddRazorPages()
    .AddRazorPagesOptions(o =>
    {
        // ✅ Protegge TUTTE le pagine del sito
        o.Conventions.AuthorizeFolder("/");

        // ✅ Lascia pubblica SOLO la login
        o.Conventions.AllowAnonymousToPage("/Admin/Login");
    });


            // ✅ Auth admin (cookie) — UNA SOLA VOLTA
            builder.Services
                .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/Admin/Login";
                    options.AccessDeniedPath = "/Admin/Login";
                    options.Cookie.Name = "BetfairReplicator.Admin";
                    options.Cookie.HttpOnly = true;
                    options.SlidingExpiration = true;
                    options.ExpireTimeSpan = TimeSpan.FromHours(12);
                    options.Events = new CookieAuthenticationEvents
                    {
                        OnRedirectToLogin = ctx =>
                        {
                            // Se è una chiamata AJAX (fetch) meglio 401 che redirect HTML
                            if (ctx.Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                                ctx.Request.Query.ContainsKey("handler"))
                            {
                                ctx.Response.StatusCode = 401;
                                return Task.CompletedTask;
                            }

                            ctx.Response.Redirect(ctx.RedirectUri);
                            return Task.CompletedTask;
                        }
                    };

                });

            builder.Services.AddAuthorization();

            // ✅ DataProtection (persistente su VPS /data)
            var dp = builder.Services.AddDataProtection();
            if (Directory.Exists("/data"))
            {
                dp.PersistKeysToFileSystem(new DirectoryInfo("/data/dpkeys"));
            }

            // ✅ HttpClient factory
            // ✅ HttpClient factory
            builder.Services.AddHttpClient(); // default

            // ✅ Named client usato da CreateClient("Betfair")
            builder.Services.AddHttpClient("Betfair", c =>
            {
                c.Timeout = TimeSpan.FromSeconds(30);
            });


            builder.Services.Configure<BetfairOptions>(
                builder.Configuration.GetSection("Betfair"));

            // ✅ Store sessioni (token Betfair)
            builder.Services.AddSingleton<BetfairSessionStoreFile>();

            // ✅ Store account + per-account cert/client
            builder.Services.AddSingleton<BetfairAccountStoreFile>();
            builder.Services.AddSingleton<BetfairCertificateProvider>();
            builder.Services.AddSingleton<BetfairHttpClientProvider>();

            // ✅ Servizi applicativi
            builder.Services.AddScoped<BetfairSsoService>();
            builder.Services.AddScoped<BetfairAccountApiService>();
            builder.Services.AddScoped<BetfairBettingApiService>();

            var app = builder.Build();

            // ✅ Seed displayName da appsettings (senza secrets)
            using (var scope = app.Services.CreateScope())
            {
                var opts = scope.ServiceProvider
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<BetfairOptions>>().Value;

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

            // ✅ auth prima di authorization
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapRazorPages();

            app.Run();
        }
    }
}
