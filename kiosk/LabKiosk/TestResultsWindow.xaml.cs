using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace LabKiosk;

public partial class TestResultsWindow : Window
{
    public TestResultsWindow(bool passed, string output, string workspacePath)
    {
        InitializeComponent();
        OutputBox.Text = string.IsNullOrWhiteSpace(output) ? "(No console output produced by test runner)" : output;
        ExecutionDetailsText.Text = $"Workspace: {workspacePath} • Finished at {DateTime.Now:HH:mm:ss}";

        if (passed)
        {
            StatusBadge.Background = (SolidColorBrush)FindResource("Ok");
            StatusBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(14, 26, 34));
            StatusBadgeText.Text = "✔ ALL LOCAL TESTS PASSED";
        }
        else
        {
            StatusBadge.Background = (SolidColorBrush)FindResource("Alert");
            StatusBadgeText.Foreground = Brushes.White;
            StatusBadgeText.Text = "✖ LOCAL TESTS FAILED";
        }

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
