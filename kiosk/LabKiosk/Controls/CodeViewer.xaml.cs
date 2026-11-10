using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;

namespace LabKiosk.Controls;

/// <summary>
/// The middle pane: file tree plus a syntax-highlighted editor over the same workspace the agents edit. Changes made by
/// agy, OpenCode or an attack appear within a moment, keeping the cursor and scroll position; Ctrl+S saves manual edits.
/// </summary>
public partial class CodeViewer : UserControl, IDisposable
{
    private static readonly string[] Hidden = { "node_modules", ".git", ".breach-backup" };
    private string _root = "";
    private string? _current;
    private bool _loading, _dirty, _suppressSelect, _firstBuild = true;
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounce;

    static CodeViewer() => AdaptHighlightingToDarkTheme();

    public CodeViewer()
    {
        InitializeComponent();
        Editor.TextChanged += (_, _) => { if (!_loading) { _dirty = true; UpdateLabel(); } };
        PreviewKeyDown += OnKey;
    }

    public void Initialize(string root)
    {
        _root = root;
        BuildTree();
        _watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
        FileSystemEventHandler h = (_, e) => OnDiskChange(e.FullPath);
        _watcher.Changed += h; _watcher.Created += h; _watcher.Deleted += h;
        _watcher.Renamed += (_, e) => OnDiskChange(e.FullPath);
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Re-reads the tree and the open file right now (used after an attack lands).</summary>
    public void RefreshAll() => Dispatcher.InvokeAsync(() => { BuildTree(); ReloadCurrentFile(); });

    // ---- tree --------------------------------------------------------------------------------------
    private static bool IsLocalOnly(string name) => name.EndsWith(".db") || name.EndsWith(".db-wal") || name.EndsWith(".db-shm") || name == ".gitignore";

    private HashSet<string> CollectExpanded()
    {
        var set = new HashSet<string>();
        void Walk(ItemCollection items) { foreach (TreeViewItem i in items) { if (i.IsExpanded && i.Tag is string p) set.Add(p); Walk(i.Items); } }
        Walk(Tree.Items);
        return set;
    }

    private void BuildTree()
    {
        var expanded = CollectExpanded();
        _suppressSelect = true;
        Tree.Items.Clear();
        AddDirectory(Tree.Items, _root, expanded);
        _firstBuild = false;
        _suppressSelect = false;
    }

    private void AddDirectory(ItemCollection items, string dir, HashSet<string> expanded)
    {
        foreach (var d in Directory.GetDirectories(dir).OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(d);
            if (Hidden.Contains(name)) continue;
            var node = new TreeViewItem { Header = "\uD83D\uDCC1 " + name, Tag = d, Foreground = Brushes.Gainsboro, IsExpanded = _firstBuild || expanded.Contains(d) };
            AddDirectory(node.Items, d, expanded);
            items.Add(node);
        }
        foreach (var f in Directory.GetFiles(dir).OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(f);
            if (IsLocalOnly(name)) continue;
            items.Add(new TreeViewItem { Header = "\uD83D\uDCC4 " + name, Tag = f, Foreground = Brushes.Gainsboro, IsSelected = f == _current });
        }
    }

    private void OnTreeSelect(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressSelect || e.NewValue is not TreeViewItem { Tag: string path } || !File.Exists(path)) return;
        if (_dirty) Save();               // switching files saves manual edits, so nothing is lost silently
        OpenFile(path);
    }

    // ---- file ---------------------------------------------------------------------------------------
    public void OpenRelative(string relPath)
    {
        var full = Path.Combine(_root, relPath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(full))
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_dirty) Save();
                OpenFile(full);
                SelectTreeNode(full);
            });
        }
    }

    private void SelectTreeNode(string fullPath)
    {
        bool FindAndSelect(ItemCollection items)
        {
            foreach (TreeViewItem item in items)
            {
                if (item.Tag is string p && string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    item.IsSelected = true;
                    item.BringIntoView();
                    return true;
                }
                if (item.Items.Count > 0 && FindAndSelect(item.Items))
                {
                    item.IsExpanded = true;
                    return true;
                }
            }
            return false;
        }
        FindAndSelect(Tree.Items);
    }

    public void OpenFile(string path)
    {
        string text;
        try { text = File.ReadAllText(path); } catch (IOException) { return; }
        _current = path;
        _loading = true;
        Editor.Text = text;
        Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(path));
        Editor.IsReadOnly = false;
        _loading = false; _dirty = false;
        SaveButton.IsEnabled = true;
        UpdateLabel();
    }

    private void ReloadCurrentFile()
    {
        if (_current is null) return;
        if (!File.Exists(_current)) { FileLabel.Text = Rel() + "  (deleted on disk)"; return; }
        string disk;
        try { disk = File.ReadAllText(_current); } catch (IOException) { return; }
        if (disk == Editor.Text) return;                  // our own save, or nothing changed
        if (_dirty) { FileLabel.Text = Rel() + "  (changed on disk; your unsaved edits are kept)"; return; }

        var caret = Editor.CaretOffset;                   // keep the cursor and scroll position
        var scroll = Editor.VerticalOffset;
        _loading = true;
        Editor.Text = disk;
        Editor.CaretOffset = Math.Min(caret, disk.Length);
        Editor.ScrollToVerticalOffset(scroll);
        _loading = false; _dirty = false;
        FileLabel.Text = Rel() + "  (updated from disk)";
    }

    private void Save()
    {
        if (_current is null) return;
        try { File.WriteAllText(_current, Editor.Text, new UTF8Encoding(false)); _dirty = false; UpdateLabel(); }
        catch (IOException ex) { FileLabel.Text = "Could not save: " + ex.Message; }
    }

    private void OnSave(object sender, RoutedEventArgs e) => Save();

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { Save(); e.Handled = true; }
    }

    private string Rel() => _current is null ? "" : Path.GetRelativePath(_root, _current).Replace('\\', '/');
    private void UpdateLabel() => FileLabel.Text = Rel() + (_dirty ? "  \u25CF unsaved" : "");

    private void OnDiskChange(string path)
    {
        if (Hidden.Any(h => path.Contains(Path.DirectorySeparatorChar + h + Path.DirectorySeparatorChar)) || IsLocalOnly(Path.GetFileName(path))) return;
        _debounce?.Dispose();
        _debounce = new System.Threading.Timer(_ => RefreshAll(), null, 400, Timeout.Infinite);
    }

    // ---- theme --------------------------------------------------------------------------------------
    /// <summary>AvalonEdit's built-in colours are made for a white background; lighten them for the dark kiosk theme.</summary>
    private static void AdaptHighlightingToDarkTheme()
    {
        foreach (var ext in new[] { ".js", ".json", ".html", ".css", ".md", ".xml" })
        {
            var def = HighlightingManager.Instance.GetDefinitionByExtension(ext);
            if (def is null) continue;
            foreach (var color in def.NamedHighlightingColors)
            {
                var n = color.Name ?? "";
                Color? c =
                    n.Contains("Comment", StringComparison.OrdinalIgnoreCase) ? Color.FromRgb(0x6A, 0x99, 0x55) :
                    n.Contains("String", StringComparison.OrdinalIgnoreCase) ? Color.FromRgb(0xCE, 0x91, 0x78) :
                    n.Contains("Digit", StringComparison.OrdinalIgnoreCase) || n.Contains("Number", StringComparison.OrdinalIgnoreCase) ? Color.FromRgb(0xB5, 0xCE, 0xA8) :
                    n.Contains("Key", StringComparison.OrdinalIgnoreCase) || n.Contains("Statement", StringComparison.OrdinalIgnoreCase) || n.Contains("Reserved", StringComparison.OrdinalIgnoreCase) ? Color.FromRgb(0x56, 0x9C, 0xD6) :
                    n.Contains("Method", StringComparison.OrdinalIgnoreCase) || n.Contains("Function", StringComparison.OrdinalIgnoreCase) ? Color.FromRgb(0xDC, 0xDC, 0xAA) :
                    n.Contains("Tag", StringComparison.OrdinalIgnoreCase) || n.Contains("Element", StringComparison.OrdinalIgnoreCase) ? Color.FromRgb(0x4E, 0xC9, 0xB0) :
                    n.Contains("Attribute", StringComparison.OrdinalIgnoreCase) || n.Contains("Property", StringComparison.OrdinalIgnoreCase) ? Color.FromRgb(0x9C, 0xDC, 0xFE) :
                    null;
                if (c is { } value) color.Foreground = new SimpleHighlightingBrush(value);
            }
        }
    }

    public void Dispose() { _watcher?.Dispose(); _debounce?.Dispose(); }
}
