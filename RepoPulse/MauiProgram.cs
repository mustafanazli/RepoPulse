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

            builder.Services.AddSingleton<IRepoPulseAuthApiClient>(_ => new RepoPulseAuthApiClient(new HttpClient
            {
                // DEVELOPMENT-ONLY address — see RepoPulseAuthApiOptions.
                BaseAddress = new Uri(RepoPulseAuthApiOptions.DevelopmentBaseAddress),
                Timeout = TimeSpan.FromSeconds(15)
            }));

            builder.Services.AddSingleton<IGitHubApiClient>(_ => new GitHubApiClient(new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            }));

            builder.Services.AddTransient<MainPage>();

            return builder.Build();
        }
    }
}
