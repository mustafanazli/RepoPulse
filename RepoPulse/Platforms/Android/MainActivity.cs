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

        // RP-014: OnPause/OnResume fire for many reasons unrelated to login
        // (home button, notification shade, recent apps, an unrelated
        // dialog) — this Activity has no idea whether a sign-in attempt is
        // in flight, so it only ever reports the raw pause/resume signal to
        // OAuthLoginAttemptCoordinator, which alone decides whether that
        // means anything. For a real OAuth callback, OnNewIntent (above)
        // always runs before OnResume on Android's standard singleTop
        // activity-resume order, so a genuine callback has already been
        // handed to LoginPage (and the coordinator marked terminal) by the
        // time OnResume's check below runs.
        protected override void OnPause()
        {
            base.OnPause();
            OAuthCallbackBroker.AttemptCoordinator.NotifyPaused();
        }

        protected override void OnResume()
        {
            base.OnResume();
            if (OAuthCallbackBroker.AttemptCoordinator.TryCancelForResumeWithoutCallback())
            {
                OAuthCallbackBroker.PublishAttemptAbandoned();
            }
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
