using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LabKiosk;

public partial class VerdictWindow : Window
{
    public int AttackId { get; }

    private static readonly Dictionary<int, string[]> AttackDefaultTiers = new()
    {
        [0] = ["tier1_defacement_neutralized", "tier2_template_syntax_valid", "tier3_layout_wrapper_intact", "tier4_header_metadata_pills", "tier5_footer_status_present", "tier6_favicon_link_preserved", "tier7_stylesheet_included", "tier8_operational_health", "tier9_benchmark_sla", "tier10_clean_architecture"],
        [1] = ["tier1_session_purge_functional", "tier2_bounded_cache_limit", "tier3_ttl_expiry_eviction", "tier4_valid_session_preserved", "tier5_unauthenticated_denied", "tier6_login_session_minted", "tier7_cookie_header_format", "tier8_memory_rss_bounded", "tier9_benchmark_sla", "tier10_clean_architecture"],
        [2] = ["tier1_concurrency_isolation", "tier2_overview_functional", "tier3_benchmark_sla", "tier4_req_user_binding", "tier5_viewer_denied", "tier6_operator_permitted", "tier7_clearance_bound", "tier8_audit_logged", "tier9_dashboard_stats", "tier10_clean_architecture"],
        [3] = ["tier1_sqli_quote_handled", "tier2_sqli_tautology_blocked", "tier3_parameterized_telemetry", "tier4_search_functional", "tier5_empty_query_all", "tier6_web_interface_render", "tier7_signal_override_audit", "tier8_benchmark_sla", "tier9_incidents_functional", "tier10_clean_architecture"],
        [4] = ["tier1_power_routes_accessible", "tier2_api_zones_functional", "tier3_diagnostic_telemetry_header", "tier4_no_bypass_header_needed", "tier5_json_accept_functional", "tier6_allocation_endpoint_secured", "tier7_operator_allocation_success", "tier8_non_power_routes_untouched", "tier9_benchmark_sla", "tier10_clean_architecture"],
        [5] = ["tier1_privilege_escalation_blocked", "tier2_vault_clearance_enforced", "tier3_profile_update_functional", "tier4_admin_vault_access_permitted", "tier5_citizen_dossier_integrity", "tier6_unauth_profile_blocked", "tier7_unauth_vault_blocked", "tier8_vault_access_audit_logged", "tier9_benchmark_sla", "tier10_clean_architecture"],
        [6] = ["tier1_double_decode_traversal_blocked", "tier2_single_decode_traversal_blocked", "tier3_windows_backslash_traversal_blocked", "tier4_null_byte_injection_blocked", "tier5_legitimate_report_accessible", "tier6_boundary_telemetry_header", "tier7_extension_whitelist_enforced", "tier8_security_dashboard_rendered", "tier9_benchmark_sla", "tier10_clean_architecture"],
        [7] = ["tier1_listener_leak_blocked", "tier2_bounded_telemetry", "tier3_listener_pool_header", "tier4_max_listeners_configured", "tier5_event_tick_fired", "tier6_traffic_signals_functional", "tier7_traffic_incidents_functional", "tier8_web_traffic_page_renders", "tier9_benchmark_sla", "tier10_clean_architecture"],
        [8] = ["tier1_prototype_pollution_blocked", "tier2_constructor_pollution_blocked", "tier3_unauth_route_still_protected", "tier4_settings_functional", "tier5_config_guard_header", "tier6_nested_merge_supported", "tier7_array_mutation_safe", "tier8_recursion_depth_bounded", "tier9_benchmark_sla", "tier10_clean_architecture"],
        [9] = ["tier1_spoofing_blocked", "tier2_direct_socket_policy", "tier3_ratelimit_headers_present", "tier4_remaining_decrements", "tier5_retry_after_ms_provided", "tier6_health_unblocked", "tier7_legitimate_login_allowed", "tier8_stale_bucket_eviction", "tier9_benchmark_sla", "tier10_clean_architecture"],
        [10] = ["tier1_defacement_cleared", "tier2_auth_session_restored", "tier3_database_stability", "tier4_janus_restored_header", "tier5_overview_api_functional", "tier6_cross_sector_telemetry", "tier7_bearer_token_fallback", "tier8_database_checkpoint_intact", "tier9_benchmark_sla", "tier10_clean_architecture"],
    };

    public VerdictWindow(
        Window owner,
        int attackId,
        string attackName,
        string status,
        double points,
        string reason,
        Dictionary<string, bool>? tierResults = null)
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

        PopulateTierMatrix(tierResults, attackId, status, reason);
    }

    private void PopulateTierMatrix(Dictionary<string, bool>? tierResults, int attackId, string status, string reason)
    {
        LedGrid.Children.Clear();
        TierListPanel.Children.Clear();

        var isOptimal = string.Equals(status, "OPTIMAL", StringComparison.OrdinalIgnoreCase) ||
                        (string.Equals(status, "SOLVED", StringComparison.OrdinalIgnoreCase));
        var isProgressive = string.Equals(status, "PROGRESSIVE", StringComparison.OrdinalIgnoreCase);

        // Derive resolved tier list
        var orderedTiers = new List<(string Key, bool Passed)>();
        if (tierResults != null && tierResults.Count > 0)
        {
            foreach (var kvp in tierResults)
            {
                orderedTiers.Add((kvp.Key, kvp.Value));
            }
        }
        else if (AttackDefaultTiers.TryGetValue(attackId, out var defaultKeys))
        {
            foreach (var key in defaultKeys)
            {
                var passed = isOptimal;
                if (!isOptimal && !string.IsNullOrWhiteSpace(reason))
                {
                    // If reason explicitly lists failure, mark false
                    if (reason.Contains(key, StringComparison.OrdinalIgnoreCase))
                    {
                        passed = false;
                    }
                    else if (isProgressive && reason.Contains("passed", StringComparison.OrdinalIgnoreCase))
                    {
                        passed = true;
                    }
                }
                orderedTiers.Add((key, passed));
            }
        }
        else
        {
            for (int i = 1; i <= 10; i++)
            {
                orderedTiers.Add(($"tier{i}_verification", isOptimal));
            }
        }

        int passedCount = orderedTiers.Count(t => t.Passed);
        int totalCount = orderedTiers.Count;
        double pct = totalCount > 0 ? (double)passedCount / totalCount * 100.0 : 0.0;

        TxtTierSummary.Text = $"{passedCount} / {totalCount} TIERS PASSED ({pct:0}%)";
        TxtTierSummary.Foreground = passedCount == totalCount
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"))
            : passedCount > 0
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB300"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5533D"));

        // Render 10-segment LED bar
        var brushGreen = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));
        var brushRed = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5533D"));
        var brushDark = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D3544"));

        foreach (var tier in orderedTiers)
        {
            var led = new Border
            {
                Background = tier.Passed ? brushGreen : brushRed,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(1.5, 0, 1.5, 0),
                ToolTip = $"{FormatTierName(tier.Key)}: {(tier.Passed ? "PASSED" : "FAILED")}"
            };
            LedGrid.Children.Add(led);
        }

        // Render Checklist Rows
        int tierIdx = 1;
        foreach (var tier in orderedTiers)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };

            var icon = new TextBlock
            {
                Text = tier.Passed ? "✔" : "✖",
                Foreground = tier.Passed ? brushGreen : brushRed,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Width = 22,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(icon);

            var statusText = new TextBlock
            {
                Text = tier.Passed ? "+10% (PASSED)" : "FAILED / BLOCKED",
                Foreground = tier.Passed ? brushGreen : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8AA0AC")),
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(statusText, Dock.Right);
            row.Children.Add(statusText);

            var title = new TextBlock
            {
                Text = $"Tier {tierIdx}: {FormatTierName(tier.Key)}",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6EEF2")),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(title);

            TierListPanel.Children.Add(row);
            tierIdx++;
        }
    }

    private static string FormatTierName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Property Check";
        var parts = raw.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var filtered = parts.Where(p => !p.StartsWith("tier", StringComparison.OrdinalIgnoreCase)).ToList();
        if (filtered.Count == 0) return raw;
        return string.Join(" ", filtered.Select(w => char.ToUpper(w[0]) + (w.Length > 1 ? w.Substring(1) : "")));
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
