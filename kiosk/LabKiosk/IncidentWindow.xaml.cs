using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Interop;

namespace LabKiosk;

public partial class IncidentWindow : Window
{
    private readonly Action? _onInvestigate;

    public int AttackId { get; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                // Disables screen capture, screenshots, and visual scraping tools
                SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE);
            }
        }
        catch { /* best effort */ }
    }

    // Suppress all WPF UI Automation peers to block AI agents and automated inspection tools from reading popup text
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new BlankWindowAutomationPeer(this);
    }

    private sealed class BlankWindowAutomationPeer : WindowAutomationPeer
    {
        public BlankWindowAutomationPeer(Window owner) : base(owner) { }
        protected override List<AutomationPeer>? GetChildrenCore() => null;
        protected override string GetNameCore() => string.Empty;
        protected override string GetItemStatusCore() => string.Empty;
        protected override string GetHelpTextCore() => string.Empty;
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
