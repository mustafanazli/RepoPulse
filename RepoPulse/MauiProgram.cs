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

            // Plain DI-registered HttpClient singleton — avoids pulling in the
            // Microsoft.Extensions.Http package just for IHttpClientFactory.
            builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(15) });
            builder.Services.AddSingleton<AuthorizationSessionStore>();
            builder.Services.AddSingleton<GitHubOAuthClient>();
            builder.Services.AddTransient<MainPage>();

            return builder.Build();
        }
    }
}
