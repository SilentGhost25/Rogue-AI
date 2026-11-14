using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LabKiosk.Services;

namespace LabKiosk.Controls;

/// <summary>A sleek, safe profile review modal that lets participants inspect identity and update callsign.</summary>
public static class ProfilePrompt
{
    public static bool Show(Window owner, KioskSettings settings)
    {
        var panel = new StackPanel { Margin = new Thickness(20), MinWidth = 340 };

        var title = new TextBlock
        {
            Text = "PARTICIPANT PROFILE",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0xD6, 0xE8)),
            Margin = new Thickness(0, 0, 0, 14)
        };
        panel.Children.Add(title);

        void AddField(string label, string value)
        {
            var l = new TextBlock { Text = label, FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0xA0, 0xAC)), Margin = new Thickness(0, 4, 0, 2) };
            var v = new TextBlock { Text = value, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEE, 0xF2)), Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(l);
            panel.Children.Add(v);
        }

        AddField("DESK IDENTIFIER", string.IsNullOrWhiteSpace(settings.DeskId) ? "Auto-assigned (Desk Active)" : settings.DeskId);
        AddField("STATION HOSTNAME", settings.KioskId);
        AddField("CONTROL PLANE URL", settings.ServerUrl);

        var lblName = new TextBlock
        {
            Text = "CALLSIGN / DISPLAY NAME",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0xD6, 0xE8)),
            Margin = new Thickness(0, 4, 0, 4)
        };
        panel.Children.Add(lblName);

        var txtName = new TextBox
        {
            Text = settings.DisplayName,
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x26, 0x33)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x4D, 0x61)),
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(txtName);

        var btnReqAccount = new Button
        {
            Content = "🔄 Request Account Switch / Proctor Help",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 0, 16),
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x02, 0x84, 0xC7)),
            FontSize = 11,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        panel.Children.Add(btnReqAccount);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        var btnCancel = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x38, 0x48)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xC2, 0xD8, 0xE2)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x4D, 0x61)),
            Cursor = System.Windows.Input.Cursors.Hand
        };

        var btnSave = new Button
        {
            Content = "Save Callsign",
            IsDefault = true,
            Padding = new Thickness(16, 6, 16, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x32, 0x42)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x3F, 0xB6, 0x8B)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0xB6, 0x8B)),
            FontWeight = FontWeights.Bold,
            Cursor = System.Windows.Input.Cursors.Hand
        };

        btnRow.Children.Add(btnCancel);
        btnRow.Children.Add(btnSave);
        panel.Children.Add(btnRow);

        var win = new Window
        {
            Title = "Participant Identity",
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x1E, 0x27)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEE, 0xF2)),
            WindowStyle = WindowStyle.ToolWindow
        };

        bool saved = false;
        btnCancel.Click += (_, _) => win.Close();
        btnSave.Click += (_, _) =>
        {
            var updated = txtName.Text.Trim();
            if (!string.IsNullOrWhiteSpace(updated))
            {
                settings.DisplayName = updated;
                settings.Save();
                saved = true;
            }
            win.Close();
        };

        btnReqAccount.Click += async (_, _) =>
        {
            btnReqAccount.IsEnabled = false;
            btnReqAccount.Content = "⏳ Sending request to Organizer...";
            try
            {
                var api = new ApiClient(settings.ServerUrl);
                var desk = !string.IsNullOrWhiteSpace(settings.DeskId) ? settings.DeskId : settings.KioskId;
                var participantId = !string.IsNullOrWhiteSpace(settings.KioskId) ? settings.KioskId : Environment.MachineName;
                var reqId = await api.RequestAccountChangeAsync(participantId, "proctor_assistance", $"Participant at {desk} ({settings.DisplayName}) requests Account Switch / Proctor Assistance.");
                
                if (string.IsNullOrEmpty(reqId))
                {
                    MessageBox.Show("Could not contact Command Center. Please signal your desk proctor in person.", "Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                btnReqAccount.Content = "⏳ Awaiting Organizer Approval...";
                var approved = false;
                for (var i = 0; i < 45; i++)
                {
                    await Task.Delay(2000);
                    var status = await api.GetRequestStatusAsync(participantId, reqId);
                    if (status == "approved") { approved = true; break; }
                    if (status == "denied") { break; }
                }

                if (approved)
                {
                    CredentialManager.ClearAntigravityGoogleCredential();
                    MessageBox.Show("Organizer has APPROVED your account switch request!\n\nYour session credential has been reset so you can sign in with your new account.", "Request Approved", MessageBoxButton.OK, MessageBoxImage.Information);
                    win.Close();
                }
                else
                {
                    MessageBox.Show("The Organizer has denied or dismissed the request.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                btnReqAccount.IsEnabled = true;
                btnReqAccount.Content = "🔄 Request Account Switch / Proctor Help";
            }
        };

        win.Loaded += (_, _) => { txtName.Focus(); txtName.SelectAll(); };
        win.ShowDialog();
        return saved;
    }
}

