using System.Windows;
using LabKiosk.Services;

namespace LabKiosk;

public partial class ComplianceLockoutDialog : Window
{
    private readonly HubClient? _hub;
    private readonly string _email;
    private readonly string _tier;
    private readonly string _deskId;
    private bool _resolved;

    public bool AllowedProPlan { get; private set; }

    public ComplianceLockoutDialog(HubClient? hub, string email, string tier, string deskId)
    {
        InitializeComponent();
        _hub = hub;
        _email = email;
        _tier = tier;
        _deskId = deskId;

        TxtDeskTag.Text = string.IsNullOrWhiteSpace(_deskId) ? "ARENA STATION" : _deskId;
        TxtEmail.Text = string.IsNullOrWhiteSpace(_email) ? "(Unset)" : _email;
        TxtTier.Text = string.IsNullOrWhiteSpace(_tier) ? "Google AI Pro" : _tier;

        Loaded += async (_, _) => await ReportProAccountAsync();
    }

    private async Task ReportProAccountAsync()
    {
        if (_hub is null) return;
        _hub.PardonGranted += OnPardon;
        _hub.ProAccountAllowed += OnAllowed;
        _hub.Disqualified += OnDisqualified;
        await _hub.ReportProAccountAsync(_email, _tier);
    }

    private void OnAllowed()
    {
        Dispatcher.Invoke(() =>
        {
            if (_resolved) return;
            _resolved = true;
            AllowedProPlan = true;
            PrgSpin.Visibility = Visibility.Collapsed;
            TxtArbiterStatus.Text = "⚡ PRO PLAN AUTHORIZED: Organizer granted official exemption to use Google AI Pro.";
            TxtArbiterStatus.Foreground = System.Windows.Media.Brushes.DeepSkyBlue;
            DialogResult = true;
            Close();
        });
    }

    private void OnPardon()
    {
        Dispatcher.Invoke(() =>
        {
            if (_resolved) return;
            _resolved = true;
            PrgSpin.Visibility = Visibility.Collapsed;
            TxtArbiterStatus.Text = "🕊️ PARDON GRANTED: Arbiter granted amnesty. Session reset authorized.";
            TxtArbiterStatus.Foreground = System.Windows.Media.Brushes.LimeGreen;
            DialogResult = true;
            Close();
        });
    }

    private void OnDisqualified(string reason)
    {
        Dispatcher.Invoke(() =>
        {
            if (_resolved) return;
            _resolved = true;
            PrgSpin.Visibility = Visibility.Collapsed;
            TxtArbiterStatus.Text = $"⛔ DISQUALIFIED: {reason}";
            TxtArbiterStatus.Foreground = System.Windows.Media.Brushes.Red;
            DialogResult = false;
            Close();
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hub != null)
        {
            _hub.PardonGranted -= OnPardon;
            _hub.ProAccountAllowed -= OnAllowed;
            _hub.Disqualified -= OnDisqualified;
        }
        base.OnClosed(e);
    }
}
