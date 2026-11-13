using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace LabKiosk;

public partial class EvaluationResultWindow : Window
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    public EvaluationResultWindow(
        Window owner,
        int attackId,
        string status,
        string reason,
        double score,
        double basePoints = 85.0,
        double speedBonus = 0.0,
        double multiplier = 1.0,
        double latencyP95Ms = 0.0,
        double peakMemoryMb = 0.0,
        int promptsUsed = 0,
        string? attackName = null)
    {
        InitializeComponent();

        Owner = owner;
        if (owner is not null)
        {
            Left = owner.Left;
            Top = owner.Top;
            Width = owner.ActualWidth > 0 ? owner.ActualWidth : SystemParameters.PrimaryScreenWidth;
            Height = owner.ActualHeight > 0 ? owner.ActualHeight : SystemParameters.PrimaryScreenHeight;
        }

        var nameStr = !string.IsNullOrWhiteSpace(attackName) ? attackName : $"Intrusion #{attackId}";
        TxtAttackTitle.Text = $"Intrusion #{attackId}: {nameStr}";
        TxtFeedback.Text = string.IsNullOrWhiteSpace(reason) ? "No detailed evaluator notes provided." : reason;

        var isSolved = string.Equals(status, "SOLVED", StringComparison.OrdinalIgnoreCase);
        var isPartial = string.Equals(status, "PARTIAL", StringComparison.OrdinalIgnoreCase);

        if (isSolved)
        {
            if (multiplier >= 0.95)
            {
                TxtStatusBadge.Text = "✔ SOLVED — OPTIMAL ARCHITECTURAL FIX";
                TxtStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x3F, 0xB6, 0x8B));
                StatusBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x33, 0x26));
                StatusBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0xB6, 0x8B));
                CardBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0xB6, 0x8B));
                CardGlow.Color = Color.FromRgb(0x3F, 0xB6, 0x8B);
            }
            else
            {
                TxtStatusBadge.Text = $"⚠ SOLVED — SUBOPTIMAL WORKAROUND ({multiplier:0.##}x)";
                TxtStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xB3, 0x41));
                StatusBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x29, 0x12));
                StatusBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xB3, 0x41));
                CardBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xB3, 0x41));
                CardGlow.Color = Color.FromRgb(0xE0, 0xB3, 0x41);
            }

            TxtScoreHighlight.Text = $"+{score:0.#} PTS";
            TxtScoreHighlight.Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0xD6, 0xE8));
            TxtFunctionalPass.Text = "✔ All Test Assertions Passed (100%)";
            TxtFunctionalPass.Foreground = new SolidColorBrush(Color.FromRgb(0x3F, 0xB6, 0x8B));

            TxtBasePoints.Text = $"+{basePoints:0.#} pts";
            TxtMultiplier.Text = $"x {multiplier:0.##}";
            TxtSpeedBonus.Text = $"+{speedBonus:0.#} pts";
            TxtNetScore.Text = $"+{score:0.#} PTS";
            TxtNetScore.Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0xD6, 0xE8));
        }
        else if (isPartial)
        {
            TxtStatusBadge.Text = "⚠ PARTIAL RESOLUTION — DEFECTS DETECTED";
            TxtStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xB3, 0x41));
            StatusBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x29, 0x12));
            StatusBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xB3, 0x41));
            CardBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xB3, 0x41));
            CardGlow.Color = Color.FromRgb(0xE0, 0xB3, 0x41);

            TxtScoreHighlight.Text = $"{score:0.#} PTS";
            TxtScoreHighlight.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xB3, 0x41));
            TxtFunctionalPass.Text = "⚠ Partial Pass — Edge Case Tests Failed";
            TxtFunctionalPass.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xB3, 0x41));

            TxtBasePoints.Text = "0.0 pts (Unsolved)";
            TxtMultiplier.Text = "0.0x";
            TxtSpeedBonus.Text = "0.0 pts";
            TxtNetScore.Text = $"{score:0.#} PTS (Penalty Applied)";
            TxtNetScore.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x3D));
        }
        else
        {
            TxtStatusBadge.Text = "❌ SUBMISSION REJECTED — BUG STILL ACTIVE";
            TxtStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x3D));
            StatusBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x16, 0x16));
            StatusBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x3D));
            CardBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x3D));
            CardGlow.Color = Color.FromRgb(0xE5, 0x53, 0x3D);

            TxtScoreHighlight.Text = $"{score:0.#} PTS";
            TxtScoreHighlight.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x3D));
            TxtFunctionalPass.Text = "❌ Test Suite Rejected Solution";
            TxtFunctionalPass.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x3D));

            TxtBasePoints.Text = "0.0 pts (Unsolved)";
            TxtMultiplier.Text = "0.0x";
            TxtSpeedBonus.Text = "0.0 pts";
            TxtNetScore.Text = $"{score:0.#} PTS (Penalty Applied)";
            TxtNetScore.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x3D));
        }

        TxtLatencyP95.Text = latencyP95Ms > 0 ? $"{latencyP95Ms:0.#} ms" : "Optimal (<5.0 ms)";
        TxtMemoryUsage.Text = peakMemoryMb > 0 ? $"{peakMemoryMb:0.#} MB" : "Normal (<25 MB)";
        TxtPromptsUsed.Text = promptsUsed > 0 ? $"{promptsUsed} prompts recorded" : "N/A";
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE);
            }
        }
        catch { /* best effort */ }
    }

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

    private void OnDismissClick(object sender, RoutedEventArgs e) => Close();
    private void OnBackdropMouseDown(object sender, MouseButtonEventArgs e) => Close();
    private void OnCardMouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter or Key.Space)
        {
            e.Handled = true;
            Close();
        }
    }
}

