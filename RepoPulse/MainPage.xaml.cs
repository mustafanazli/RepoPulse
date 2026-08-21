using RepoPulse.Authentication;
using RepoPulse.Core.Authentication;

namespace RepoPulse
{
    public partial class MainPage : ContentPage
    {
        int count = 0;
        bool isSubscribedToOAuthCallbacks;

        public MainPage()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            if (!isSubscribedToOAuthCallbacks)
            {
                OAuthCallbackBroker.CallbackReceived += OnOAuthCallbackReceived;
                isSubscribedToOAuthCallbacks = true;
            }

            // Cold start: the callback may already have been parsed and published
            // by MainActivity before this page existed and could subscribe above.
            var pending = OAuthCallbackBroker.TryConsumePendingResult();
            if (pending is not null)
            {
                OnOAuthCallbackReceived(this, pending);
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            if (isSubscribedToOAuthCallbacks)
            {
                OAuthCallbackBroker.CallbackReceived -= OnOAuthCallbackReceived;
                isSubscribedToOAuthCallbacks = false;
            }
        }

        private void OnCounterClicked(object? sender, EventArgs e)
        {
            count++;

            if (count == 1)
                CounterBtn.Text = $"Clicked {count} time";
            else
                CounterBtn.Text = $"Clicked {count} times";

            SemanticScreenReader.Announce(CounterBtn.Text);
        }

        // Dev-only status text for RP-002 callback verification. Deliberately
        // never displays the actual code/state/error_description values.
        private void OnOAuthCallbackReceived(object? sender, OAuthCallbackResult result)
        {
            var statusText = result.Outcome switch
            {
                OAuthCallbackOutcome.Success => "Callback alındı",
                OAuthCallbackOutcome.Cancelled => "Kullanıcı iptal etti",
                OAuthCallbackOutcome.Invalid => "Geçersiz callback",
                _ => "Bilinmeyen durum"
            };

            MainThread.BeginInvokeOnMainThread(() => OAuthCallbackStatusLabel.Text = statusText);
        }
    }
}
