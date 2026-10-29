using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MarkdownMidget.Tabs;

/// <summary>
/// One entry in the Project tree. A project is a folder with a 000000-index.md
/// (the EEE book folders): each index lists that folder's files and sub-folders
/// in book order, one "- [name](target)" line each, a sub-folder's target being its
/// own index. The tree follows the indexes, so it shows the book in reading order.
///
/// Entries are labelled with their document's first heading ("Chapter I: Creatures",
/// "ABOLETH"); a folder with that of its intro file (000050-...md). Folders load
/// when first expanded.
/// </summary>
public sealed class ProjectItem : NotifyBase
{
    public const string IndexName = "000000-index.md";
    private static readonly ProjectItem Placeholder = new("", "", isFolder: false);
    internal static readonly Regex Entry =new(@"^- \[[^\]]*\]\(([^)]+)\)\s*$", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^#{1,6}\s+(.*)$", RegexOptions.Compiled);
    private bool _isExpanded, _loaded;

    private ProjectItem(string path, string label, bool isFolder, string? intro = null)
    {
        FullPath = path;
        Intro = intro;
        Label = label;
        IsFolder = isFolder;
        if (isFolder) Children.Add(Placeholder);
    }

    /// <summary>The root of a project, or null when the folder has no index.</summary>
    public static ProjectItem? OpenProject(string folder)
    {
        if (!File.Exists(Path.Combine(folder, IndexName))) return null;
        var title = FirstLine(Directory.EnumerateFiles(folder, "*.md").Where(f => !f.EndsWith(IndexName))
                                              .OrderBy(f => f, ExplorerOrder.Instance).FirstOrDefault());
        var name = Path.GetFileName(folder.TrimEnd('\\', '/'));
        return new ProjectItem(folder, title is null ? name : $"{name}  ·  {title}", isFolder: true) { IsExpanded = true };
    }

    public static bool IsProject(string folder) => File.Exists(Path.Combine(folder, IndexName));

    /// <summary>
    /// Every Markdown file under a folder, in book order: following its 000000-index.md
    /// (and its sub-folders' indexes) when it has one, as assemble.py does; otherwise
    /// sub-folders then files, in Explorer's order. Index files themselves are left out.
    /// </summary>
    /// <param name="subfolders">false: only the files directly in the folder.</param>
    public static IEnumerable<string> FilesInOrder(string folder, bool subfolders = true)
    {
        var index = Path.Combine(folder, IndexName);
        if (File.Exists(index))
        {
            foreach (var line in File.ReadLines(index))
            {
                var m = Entry.Match(line);
                if (!m.Success) continue;
                var target = Path.GetFullPath(Path.Combine(folder, m.Groups[1].Value));
                if (m.Groups[1].Value.EndsWith("/" + IndexName, StringComparison.OrdinalIgnoreCase))
                {
                    if (subfolders)
                        foreach (var f in FilesInOrder(Path.GetDirectoryName(target)!)) yield return f;
                }
                else if (File.Exists(target))
                    yield return target;
            }
            yield break;
        }
        const FileAttributes skip = FileAttributes.Hidden | FileAttributes.System;
        var dir = new DirectoryInfo(folder);
        if (subfolders)
            foreach (var d in dir.EnumerateDirectories().Where(d => (d.Attributes & skip) == 0).OrderBy(d => d.Name, ExplorerOrder.Instance))
                foreach (var f in FilesInOrder(d.FullName)) yield return f;
        foreach (var f in dir.EnumerateFiles().Where(f => (f.Attributes & skip) == 0 && FileTreeItem.IsMarkdown(f.Name)
                                                          && !f.Name.Equals(IndexName, StringComparison.OrdinalIgnoreCase))
                             .OrderBy(f => f.Name, ExplorerOrder.Instance))
            yield return f.FullName;
    }

    public string FullPath { get; }
    /// <summary>A folder's intro file (000050-...md): opened by clicking the folder, so it
    /// isn't listed again inside it.</summary>
    public string? Intro { get; }
    /// <summary>What clicking this entry opens: the file, or a folder's intro.</summary>
    public string? OpenPath => IsFolder ? Intro : FullPath;
    public string Label { get; }
    public bool IsFolder { get; }
    public string Glyph => IsFolder ? (IsExpanded ? "" : "") : "";
    public string ToolTip => IsFolder && Intro is not null ? FullPath + Environment.NewLine + "(double-click to open its intro)" : FullPath;
    public ObservableCollection<ProjectItem> Children { get; } = new();

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

    /// <summary>This folder's entries, in the order its index lists them.</summary>
    private void Load()
    {
        _loaded = true;
        Children.Clear();
        try
        {
            foreach (var line in File.ReadLines(Path.Combine(FullPath, IndexName)))
            {
                var m = Entry.Match(line);
                if (!m.Success) continue;
                var target = Path.GetFullPath(Path.Combine(FullPath, m.Groups[1].Value));
                if (m.Groups[1].Value.EndsWith("/" + IndexName, StringComparison.OrdinalIgnoreCase))
                {
                    var dir = Path.GetDirectoryName(target)!;
                    var intro = Directory.EnumerateFiles(dir, "000050-*.md").FirstOrDefault();
                    Children.Add(new ProjectItem(dir, FolderLabel(dir, intro), isFolder: true, intro));
                }
                else if (File.Exists(target) && !string.Equals(target, Intro, StringComparison.OrdinalIgnoreCase))
                {
                    Children.Add(new ProjectItem(target, FirstLine(target) ?? Path.GetFileName(target), isFolder: false));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable index just shows as empty.
        }
    }

    /// <summary>A folder's label: its intro file's heading, else its name without the number.</summary>
    private static string FolderLabel(string dir, string? intro)
    {
        if (intro is not null && FirstLine(intro) is { } heading) return heading;
        // no intro: "0050-monsters" -> "Monsters"
        var words = Regex.Replace(Path.GetFileName(dir), @"^\d{4}-", "").Replace('-', ' ');
        return words.Length > 0 ? char.ToUpper(words[0]) + words[1..] : words;
    }

    /// <summary>The first heading (or first non-blank line) of a file, without markup or anchors.</summary>
    private static string? FirstLine(string? file)
    {
        if (file is null) return null;
        try
        {
            foreach (var raw in File.ReadLines(file).Take(20))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var m = Heading.Match(raw);
                var text = m.Success ? m.Groups[1].Value : raw;
                text = Regex.Replace(text, @"<[^>]+>", "");                 // anchors, <sup>
                text = Regex.Replace(text, @"\[([^\]]*)\]\([^)]*\)", "$1");  // links -> their text
                text = text.Replace("**", "").Replace("\\", "").Trim();
                if (text.Length > 0) return text;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }
}
