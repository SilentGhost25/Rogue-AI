using System.Windows;
using System.Windows.Input;

namespace LabKiosk;

public partial class IncidentWindow : Window
{
    private readonly Action? _onInvestigate;

    public int AttackId { get; }

    public IncidentWindow(
        Window owner,
        int attackId,
        string title,
        string classification,
        string symptoms,
        string telemetry,
        Action? onInvestigate = null)
    {
        InitializeComponent();

        AttackId = attackId;
        _onInvestigate = onInvestigate;

        Owner = owner;
        if (owner is not null)
        {
            Left = owner.Left;
            Top = owner.Top;
            Width = owner.ActualWidth > 0 ? owner.ActualWidth : SystemParameters.PrimaryScreenWidth;
            Height = owner.ActualHeight > 0 ? owner.ActualHeight : SystemParameters.PrimaryScreenHeight;
        }

        TxtTitle.Text = title;
        TxtClassification.Text = classification;
        TxtSymptoms.Text = symptoms;
        TxtTelemetry.Text = telemetry;
    }

    private void OnDismissClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnInvestigateClick(object sender, RoutedEventArgs e)
    {
        Close();
        _onInvestigate?.Invoke();
    }

    private void OnBackdropMouseDown(object sender, MouseButtonEventArgs e)
    {
        Close();
    }

    private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}

