using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace MarkdownMidget.Tabs;

/// <summary>
/// One folder or Markdown file in the Files tree. Folders load their children the
/// first time they are expanded (a placeholder child makes the expander show), so
/// opening a big folder costs nothing until you look inside it.
/// </summary>
public sealed class FileTreeItem : NotifyBase
{
    private static readonly FileTreeItem Placeholder = new("", isFolder: false);
    private bool _isExpanded, _loaded;

    public FileTreeItem(string path, bool isFolder)
    {
        FullPath = path;
        IsFolder = isFolder;
        Name = path.Length == 0 ? "" : Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : path;
        if (isFolder) Children.Add(Placeholder);
    }

    public string FullPath { get; }
    public string Name { get; }
    public bool IsFolder { get; }
    /// <summary>Segoe Fluent Icons glyph: folder or document.</summary>
    public string Glyph => IsFolder ? (IsExpanded ? "" : "") : "";
    public ObservableCollection<FileTreeItem> Children { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!Set(ref _isExpanded, value)) return;
            OnPropertyChanged(nameof(Glyph));
            if (value && !_loaded) Load();
        }
    }

    /// <summary>Read the folder again (after a file was added): now if it's open, else when next opened.</summary>
    public void Reload()
    {
        _loaded = false;
        if (IsExpanded) Load();
        else { Children.Clear(); Children.Add(Placeholder); }
    }

    /// <summary>Folders first, then .md / .markdown files, each in Explorer's order. Hidden
    /// and system entries, and folders we can't read, are left out.</summary>
    private void Load()
    {
        _loaded = true;
        Children.Clear();
        try
        {
            var dir = new DirectoryInfo(FullPath);
            const FileAttributes skip = FileAttributes.Hidden | FileAttributes.System;
            foreach (var d in dir.EnumerateDirectories().Where(d => (d.Attributes & skip) == 0)
                                 .OrderBy(d => d.Name, ExplorerOrder.Instance))
                Children.Add(new FileTreeItem(d.FullName, isFolder: true));
            foreach (var f in dir.EnumerateFiles().Where(f => (f.Attributes & skip) == 0 && IsMarkdown(f.Name))
                                 .OrderBy(f => f.Name, ExplorerOrder.Instance))
                Children.Add(new FileTreeItem(f.FullName, isFolder: false));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // An unreadable folder just shows as empty.
        }
    }

    public static bool IsMarkdown(string name) =>
        name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);
}
