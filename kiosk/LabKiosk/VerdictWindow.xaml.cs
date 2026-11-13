using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace LabKiosk;

public partial class VerdictWindow : Window
{
    public int AttackId { get; }

    public VerdictWindow(
        Window owner,
        int attackId,
        string attackName,
        string status,
        double points,
        string reason)
    {
        InitializeComponent();

        AttackId = attackId;
        Owner = owner;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (owner is not null)
        {
            Left = owner.Left;
            Top = owner.Top;
            Width = owner.ActualWidth > 0 ? owner.ActualWidth : SystemParameters.PrimaryScreenWidth;
            Height = owner.ActualHeight > 0 ? owner.ActualHeight : SystemParameters.PrimaryScreenHeight;
        }

        TxtAttackTitle.Text = $"Attack #{attackId}: {attackName}";

        var isOptimal = string.Equals(status, "OPTIMAL", StringComparison.OrdinalIgnoreCase) ||
                        (string.Equals(status, "SOLVED", StringComparison.OrdinalIgnoreCase) && points >= 80);
        var isBase = string.Equals(status, "BASE", StringComparison.OrdinalIgnoreCase) ||
                     (string.Equals(status, "SOLVED", StringComparison.OrdinalIgnoreCase) && points < 80);
        var isProgressive = string.Equals(status, "PROGRESSIVE", StringComparison.OrdinalIgnoreCase);

        if (isOptimal)
        {
            ApplyTheme(
                primaryColor: (Color)ColorConverter.ConvertFromString("#00E676"),
                icon: "🏆 ",
                heading: "OPTIMAL SOLUTION ACCEPTED",
                scoreText: $"+{points:0.#} PTS (100% QUALITY)",
                tierLabel: "OPTIMAL ARCHITECTURE · 100% MAXIMUM QUALITY SCORE",
                subheadline: "Exemplary defense! Root vulnerability completely neutralized with clean architecture and optimal latency.",
                diagnostics: reason,
                actionText: "✓ Continue Defense"
            );
        }
        else if (isBase)
        {
            ApplyTheme(
                primaryColor: (Color)ColorConverter.ConvertFromString("#37D6E8"),
                icon: "🛡️ ",
                heading: "BASE SOLUTION ACCEPTED",
                scoreText: $"+{points:0.#} PTS (75% BASE)",
                tierLabel: "VULNERABILITY NEUTRALIZED · 75% BASE SCORE",
                subheadline: "Your fix neutralized the breach and passed all functional tests! Note: Solution is functional, but suboptimal (performance or ad-hoc architecture).",
                diagnostics: reason,
                actionText: "✓ Continue Defense"
            );
        }
        else if (isProgressive)
        {
            ApplyTheme(
                primaryColor: (Color)ColorConverter.ConvertFromString("#FFB300"),
                icon: "⚡ ",
                heading: "PROGRESSIVE DEFENSE VERIFIED",
                scoreText: points > 0 ? $"+{points:0.#} PTS AWARDED" : "+0 PTS (TIERS ALREADY CREDITED)",
                tierLabel: "INTERMEDIARY DEFENSE · PROGRESSIVE POINTS AWARDED",
                subheadline: "Progress confirmed! Intermediary security tiers cleared and permanent points credited. Continue hardening to reach full BASE or OPTIMAL resolution.",
                diagnostics: reason,
                actionText: "✓ Keep Hardening"
            );
        }
        else
        {
            ApplyTheme(
                primaryColor: (Color)ColorConverter.ConvertFromString("#E5533D"),
                icon: "❌ ",
                heading: "FIX REJECTED: NOT SOLVED",
                scoreText: "0 PTS (ATTEMPT RECORDED)",
                tierLabel: "SECURITY AUDIT FAILED · INCOMPLETE FIX",
                subheadline: "This is not the correct solution. You have not solved everything for this particular attack.",
                diagnostics: reason,
                actionText: "Review Code & Try Again"
            );
        }
    }

    private void ApplyTheme(
        Color primaryColor,
        string icon,
        string heading,
        string scoreText,
        string tierLabel,
        string subheadline,
        string diagnostics,
        string actionText)
    {
        var brush = new SolidColorBrush(primaryColor);

        CardBorder.BorderBrush = brush;
        CardGlow.Color = primaryColor;
        TxtIcon.Text = icon;
        TxtHeading.Text = heading;
        TxtHeading.Foreground = brush;
        TxtScoreBadge.Text = scoreText;
        TxtScoreBadge.Foreground = brush;
        TierBadgeBorder.BorderBrush = brush;
        TxtTierLabel.Text = tierLabel;
        TxtTierLabel.Foreground = brush;
        TxtSubheadline.Text = subheadline;
        TxtDiagnostics.Text = string.IsNullOrWhiteSpace(diagnostics) ? "No additional details provided." : diagnostics;
        BtnAction.Content = actionText;
    }

    private void OnDismissClick(object sender, RoutedEventArgs e)
    {
        Close();
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
        if (e.Key is Key.Escape or Key.Enter or Key.Space)
        {
            Close();
            e.Handled = true;
        }
    }
}

