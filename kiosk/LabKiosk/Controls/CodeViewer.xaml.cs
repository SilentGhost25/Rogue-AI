using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Highlighting;

namespace LabKiosk.Controls;

public partial class CodeViewer : UserControl, IDisposable
{
    private string _workspacePath = string.Empty;
    private string? _currentRelativeFile;
    private FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _debounceTimer;
    private string? _pendingChangedFile;
    private bool _isSavingInternal;

    public ObservableCollection<WorkspaceFileItem> Files { get; } = new();

    public CodeViewer()
    {
        InitializeComponent();
        FileList.ItemsSource = Files;

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounceTimer.Tick += OnDebounceTick;

        Editor.TextArea.KeyDown += (s, e) =>
        {
            if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                SaveCurrentFile();
                e.Handled = true;
            }
        };
    }

    public void InitializeWorkspace(string path)
    {
        _workspacePath = Path.GetFullPath(path);
        RefreshFileList();
        SetupWatcher();
        if (Files.Count > 0)
        {
            // Default select search.js or app.js
            var preferred = Files.FirstOrDefault(f => f.DisplayPath == "routes/search.js")
                         ?? Files.FirstOrDefault(f => f.DisplayPath == "app.js")
                         ?? Files[0];
            FileList.SelectedItem = preferred;
        }
    }

    public void RefreshFileList()
    {
        Files.Clear();
        if (!Directory.Exists(_workspacePath)) return;

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "node_modules", ".git", ".breach-backup", "dist", "build" };

        var allFiles = Directory.EnumerateFiles(_workspacePath, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(segment => excluded.Contains(segment)))
            .OrderBy(f => f)
            .ToList();

        foreach (var file in allFiles)
        {
            var rel = Path.GetRelativePath(_workspacePath, file).Replace('\\', '/');
            var ext = Path.GetExtension(file).ToLowerInvariant();
            var icon = ext switch
            {
                ".js" => "JS",
                ".json" => "{ }",
                ".html" => "<>",
                ".css" => "#",
                ".md" => "MD",
                _ => "FILE"
            };
            var color = ext switch
            {
                ".js" => new SolidColorBrush(Color.FromRgb(240, 219, 79)),
                ".json" => new SolidColorBrush(Color.FromRgb(63, 182, 139)),
                ".html" => new SolidColorBrush(Color.FromRgb(229, 83, 61)),
                ".css" => new SolidColorBrush(Color.FromRgb(66, 165, 245)),
                _ => new SolidColorBrush(Color.FromRgb(138, 160, 172))
            };
            Files.Add(new WorkspaceFileItem(rel, file, icon, color));
        }
    }

    private void SetupWatcher()
    {
        _watcher?.Dispose();
        if (!Directory.Exists(_workspacePath)) return;

        _watcher = new FileSystemWatcher(_workspacePath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
        };
        _watcher.Changed += OnFileChangedOnDisk;
        _watcher.Created += OnFileChangedOnDisk;
        _watcher.Deleted += OnFileChangedOnDisk;
        _watcher.Renamed += OnFileChangedOnDisk;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileChangedOnDisk(object sender, FileSystemEventArgs e)
    {
        if (_isSavingInternal) return;
        var rel = Path.GetRelativePath(_workspacePath, e.FullPath).Replace('\\', '/');
        _pendingChangedFile = rel;
        Dispatcher.InvokeAsync(() =>
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        });
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        if (_pendingChangedFile is not null && _pendingChangedFile == _currentRelativeFile)
        {
            ReloadCurrentFile();
            SyncStatus.Text = "Updated from disk";
            SyncStatus.Foreground = (SolidColorBrush)FindResource("Ok");
        }
    }

    private void OnFileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (FileList.SelectedItem is WorkspaceFileItem item)
        {
            LoadFile(item.RelativePath, item.FullPath);
        }
    }

    public void LoadFile(string relPath, string fullPath)
    {
        _currentRelativeFile = relPath;
        CurrentFilePath.Text = relPath;

        if (File.Exists(fullPath))
        {
            try
            {
                Editor.Text = File.ReadAllText(fullPath);
                var ext = Path.GetExtension(fullPath);
                var highlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext);
                if (highlighting is not null)
                {
                    Editor.SyntaxHighlighting = highlighting;
                }
                SyncStatus.Text = "Synced";
                SyncStatus.Foreground = (SolidColorBrush)FindResource("Ok");
            }
            catch (Exception ex)
            {
                Editor.Text = $"// Error loading file: {ex.Message}";
                SyncStatus.Text = "Read error";
                SyncStatus.Foreground = (SolidColorBrush)FindResource("Alert");
            }
        }
    }

    public void ReloadCurrentFile()
    {
        if (_currentRelativeFile is null) return;
        var full = Path.Combine(_workspacePath, _currentRelativeFile);
        if (File.Exists(full))
        {
            var caretOffset = Math.Min(Editor.CaretOffset, (int)new FileInfo(full).Length);
            try
            {
                Editor.Text = File.ReadAllText(full);
                if (caretOffset >= 0 && caretOffset <= Editor.Text.Length)
                {
                    Editor.CaretOffset = caretOffset;
                }
            }
            catch { /* File might be locked during rapid writes */ }
        }
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e) => SaveCurrentFile();

    private void SaveCurrentFile()
    {
        if (_currentRelativeFile is null) return;
        var full = Path.Combine(_workspacePath, _currentRelativeFile);
        try
        {
            _isSavingInternal = true;
            File.WriteAllText(full, Editor.Text);
            SyncStatus.Text = "Saved to disk";
            SyncStatus.Foreground = (SolidColorBrush)FindResource("Ok");
        }
        catch (Exception ex)
        {
            SyncStatus.Text = $"Save failed: {ex.Message}";
            SyncStatus.Foreground = (SolidColorBrush)FindResource("Alert");
        }
        finally
        {
            _isSavingInternal = false;
        }
    }

    private void OnRefreshTree(object sender, RoutedEventArgs e)
    {
        RefreshFileList();
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounceTimer.Stop();
    }
}

public sealed record WorkspaceFileItem(string DisplayPath, string FullPath, string Icon, Brush IconColor)
{
    public string RelativePath => DisplayPath;
}

