using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using RepoPulse.Authentication;
using RepoPulse.Core.Authentication;

namespace RepoPulse
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataScheme = OAuthConstants.CallbackScheme,
        DataHost = OAuthConstants.CallbackHost,
        DataPath = OAuthConstants.CallbackPath)]
    public class MainActivity : MauiAppCompatActivity
    {
        private const string LogTag = "RepoPulse.OAuth";

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            HandleOAuthCallbackIntent(Intent);
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            HandleOAuthCallbackIntent(intent);
        }

        private static void HandleOAuthCallbackIntent(Intent? intent)
        {
            var data = intent?.Data;
            if (data is null)
            {
                return;
            }

            // Uri.TryCreate never throws, so malformed/unexpected intent data
            // cannot crash the app; it is simply treated as an invalid callback.
            var rawUri = data.ToString();
            if (string.IsNullOrEmpty(rawUri) || !Uri.TryCreate(rawUri, UriKind.Absolute, out var uri))
            {
                Log.Warn(LogTag, "OAuth callback intent had unparsable data; treating as invalid.");
                OAuthCallbackBroker.Publish(OAuthCallbackResult.Invalid());
                return;
            }

            var result = OAuthCallbackParser.Parse(uri);

            // Never log the actual code/state/error_description values, only
            // the classification and whether each field was present.
            Log.Info(LogTag,
                $"OAuth callback received: outcome={result.Outcome}, hasCode={result.Code is not null}, " +
                $"hasState={result.State is not null}, hasError={result.Error is not null}");

            OAuthCallbackBroker.Publish(result);
        }
    }
}
