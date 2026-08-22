using Microsoft.Extensions.Logging;
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

            builder.Services.AddSingleton<IRepoPulseAuthApiClient>(_ =>
                new RepoPulseAuthApiClient(CreateAuthApiHttpClient()));

            // GitHubApiClient always uses ordinary platform TLS validation —
            // no custom handler, in any build configuration.
            builder.Services.AddSingleton<IGitHubApiClient>(_ => new GitHubApiClient(new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            }));

            builder.Services.AddTransient<MainPage>();

            return builder.Build();
        }

        // DEVELOPMENT-ONLY address resolution. The Android emulator cannot
        // reach the host machine's "localhost" — 10.0.2.2 is its documented
        // alias for that. Every other debug target (Windows, iOS simulator,
        // MacCatalyst) keeps using localhost directly.
        private static string ResolveAuthApiBaseAddress()
        {
#if ANDROID && DEBUG
            return "https://10.0.2.2:7082";
#else
#if !DEBUG
#warning RepoPulseAuthApiClient base address is still the DEBUG-only localhost/10.0.2.2 placeholder. Set a real production hosting URL (see docs/backend-auth.md) before shipping a Release build.
#endif
            return RepoPulseAuthApiOptions.DevelopmentBaseAddress;
#endif
        }

        // The AuthApi HttpClient is the ONLY client that ever gets a custom
        // certificate handler, and only in DEBUG builds — the callback and
        // HttpClientHandler below do not exist at all in Release IL.
        // GitHubApiClient (above) and every Release build use the default
        // handler with ordinary platform TLS validation.
        private static HttpClient CreateAuthApiHttpClient()
        {
            var baseAddress = new Uri(ResolveAuthApiBaseAddress());

#if DEBUG
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (request, certificate, chain, sslPolicyErrors) =>
                    DevelopmentCertificateValidator.ShouldAccept(request, certificate, chain, sslPolicyErrors)
            };

            return new HttpClient(handler) { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(15) };
#else
            return new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(15) };
#endif
        }
    }
}
