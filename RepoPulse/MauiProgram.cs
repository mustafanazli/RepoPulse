using Microsoft.Extensions.Logging;
using RepoPulse.Authentication;
using RepoPulse.Core.Authentication;

namespace RepoPulse
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            // Two distinct HttpClient instances (one per remote, one with the
            // AuthApi base address) wired via factory lambdas — avoids pulling
            // in the Microsoft.Extensions.Http package just for named/typed
            // clients from IHttpClientFactory.
            builder.Services.AddSingleton<AuthorizationSessionStore>();
            builder.Services.AddSingleton<UserSessionStore>();
            builder.Services.AddSingleton<ISecureSessionStorage, MauiSecureSessionStorage>();
            builder.Services.AddSingleton<ISessionInvalidationMarker, MauiSessionInvalidationMarker>();
            builder.Services.AddSingleton<SessionPersistenceStore>();

            builder.Services.AddSingleton<IRepoPulseAuthApiClient>(_ =>
                new RepoPulseAuthApiClient(CreateAuthApiHttpClient()));

            // GitHubApiClient always uses ordinary platform TLS validation —
            // no custom handler, in any build configuration.
            builder.Services.AddSingleton<IGitHubApiClient>(_ => new GitHubApiClient(new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            }));

            builder.Services.AddTransient<AppShell>();
            builder.Services.AddTransient<BootstrapPage>();
            builder.Services.AddTransient<LoginPage>();
            builder.Services.AddTransient<RepositoryListPage>();
            builder.Services.AddTransient<RepositoryDetailPage>();
            builder.Services.AddTransient<SettingsPage>();

            return builder.Build();
        }

        // Flip to true LOCALLY (never commit as true) to point the Android
        // DEBUG build at a local `dotnet run` AuthApi instance (10.0.2.2)
        // instead of the live staging backend — useful only when actively
        // developing/debugging the backend itself. Must stay false in
        // committed code: day-to-day mobile development runs against the
        // live staging backend by default, without requiring a local
        // dotnet process.
        private const bool UseLocalDevelopmentAuthApi = false;

        // DEVELOPMENT-ONLY address resolution. Android DEBUG defaults to the
        // live Azure staging backend (RepoPulseAuthApiOptions.StagingBaseAddress)
        // — NOT production, see that constant's remarks — so day-to-day
        // development doesn't require a local backend process. Flipping
        // UseLocalDevelopmentAuthApi above falls back to 10.0.2.2, the
        // Android emulator's documented alias for the host machine's
        // localhost, for local AuthApi testing. Every other debug target
        // (Windows, iOS simulator, MacCatalyst) keeps using localhost
        // directly — unaffected by either the staging address or the
        // emulator-specific alias.
        private static string ResolveAuthApiBaseAddress()
        {
#if ANDROID && DEBUG
            return UseLocalDevelopmentAuthApi
                ? "https://10.0.2.2:7082"
                : RepoPulseAuthApiOptions.StagingBaseAddress;
#else
#if !DEBUG
#warning RepoPulseAuthApiClient base address is still the DEBUG-only localhost/10.0.2.2 placeholder. Set a real production hosting URL (see docs/backend-auth.md) before shipping a Release build.
#endif
            return RepoPulseAuthApiOptions.DevelopmentBaseAddress;
#endif
        }

        // The custom development-certificate handler is wired ONLY when the
        // resolved base address is actually a local-development host
        // (10.0.2.2/localhost/127.0.0.1) — never for the live Azure staging
        // address, even in DEBUG builds, and never in Release. Staging (and
        // Release) always get a plain HttpClient with the default handler,
        // i.e. ordinary platform TLS validation against staging's real,
        // publicly-trusted certificate — no custom callback is ever
        // attached for it. GitHubApiClient (above) never gets a custom
        // handler in any build configuration.
        private static HttpClient CreateAuthApiHttpClient()
        {
            var baseAddress = new Uri(ResolveAuthApiBaseAddress());

#if DEBUG
            if (DevelopmentCertificateValidator.IsLocalDevelopmentHost(baseAddress.Host))
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (request, certificate, chain, sslPolicyErrors) =>
                        DevelopmentCertificateValidator.ShouldAccept(request, certificate, chain, sslPolicyErrors)
                };

                return new HttpClient(handler) { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(15) };
            }
#endif

            return new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(15) };
        }
    }
}
