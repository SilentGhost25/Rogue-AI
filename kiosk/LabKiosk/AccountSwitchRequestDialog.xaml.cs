using System.Windows;
using LabKiosk.Services;

namespace LabKiosk;

public partial class AccountSwitchRequestDialog : Window
{
    private readonly HubClient? _hub;
    private readonly string _currentEmail;
    private readonly string _deskId;
    private string _requestId = "";
    private bool _resolved;

    public AccountSwitchRequestDialog(HubClient? hub, string currentEmail, string deskId)
    {
        InitializeComponent();
        _hub = hub;
        _currentEmail = currentEmail;
        _deskId = deskId;

        TxtDeskTag.Text = string.IsNullOrWhiteSpace(_deskId) ? "ARENA STATION" : _deskId;
        TxtCurrentEmail.Text = string.IsNullOrWhiteSpace(_currentEmail) ? "(Unset)" : _currentEmail;

        Loaded += async (_, _) => await SubmitRequestAsync();
    }

    private async Task SubmitRequestAsync()
    {
        if (_hub is null)
        {
            TxtStatus.Text = "Hub not connected. Cannot contact organizer.";
            TxtStatus.Foreground = System.Windows.Media.Brushes.Red;
            PrgSpin.Visibility = Visibility.Collapsed;
            return;
        }

        _hub.AccountSwitchApproved += OnApproved;
        _hub.AccountSwitchDenied += OnDenied;
        _hub.AccountSwitchResult += OnResult;

        TxtStatus.Text = "Transmitting switch request to Organizer Control Plane...";
        _requestId = await _hub.RequestAccountSwitchAsync(_currentEmail);

        if (string.IsNullOrEmpty(_requestId))
        {
            TxtStatus.Text = "Organizer hub received request. Awaiting approval...";
        }
        else
        {
            TxtStatus.Text = $"Request #{_requestId} sent to Arbiter Console. Waiting for approval...";
        }
    }

    private void OnResult(string reqId, bool approved, string? reason)
    {
        Dispatcher.Invoke(() =>
        {
            if (_resolved) return;
            if (approved)
                OnApproved(reqId);
            else
                OnDenied(reason ?? "Arbiter declined switch request.");
        });
    }

    private void OnApproved(string reqId)
    {
        Dispatcher.Invoke(() =>
        {
            if (_resolved) return;
            _resolved = true;
            PrgSpin.Visibility = Visibility.Collapsed;
            TxtStatus.Text = "✓ Switch Request APPROVED by Organizer!";
            TxtStatus.Foreground = System.Windows.Media.Brushes.LimeGreen;
            TxtFeedback.Text = "Clearing session credentials and opening sign-in...";
            DialogResult = true;
            Close();
        });
    }

    private void OnDenied(string reason)
    {
        Dispatcher.Invoke(() =>
        {
            if (_resolved) return;
            _resolved = true;
            PrgSpin.Visibility = Visibility.Collapsed;
            TxtStatus.Text = $"✕ Switch Request DENIED: {reason}";
            TxtStatus.Foreground = System.Windows.Media.Brushes.Red;
            TxtFeedback.Text = "Please contact event organizers if you believe this is in error.";
            BtnCancel.Content = "Close";
        });
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hub != null)
        {
            _hub.AccountSwitchApproved -= OnApproved;
            _hub.AccountSwitchDenied -= OnDenied;
            _hub.AccountSwitchResult -= OnResult;
        }
        base.OnClosed(e);
    }
}
