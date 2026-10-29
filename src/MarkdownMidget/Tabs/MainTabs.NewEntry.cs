using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;

namespace MarkdownMidget.Tabs;

/// <summary>
/// New Entry… on the tree menus. On a file: a new file right after it. On a folder:
/// a new file at the end of it. In numbered folders the name starts with a number
/// halfway between this entry and the next (001500 and 002000 give 001750). When
/// the folder has a 000000-index.md the new file is added to it in that place, so
/// the Project tree and the book build include it.
/// </summary>
public partial class MainTabs
{
    private static readonly Regex NumberPrefix = new(@"^(\d{3,})-", RegexOptions.Compiled);

    private void TreeMenu_NewEntry(object sender, RoutedEventArgs e)
    {
        if (TreeTarget(sender) is not { } t) return;
        var folder = t.IsFolder ? t.Path : Path.GetDirectoryName(t.Path)!;
        var after = t.IsFolder ? null : Path.GetFileName(t.Path);     // null: at the end
        var index = Path.Combine(folder, ProjectItem.IndexName);
        var entries = EntriesOf(folder);

        var (prefix, hint) = SuggestPrefix(entries, after);
        var name = InputDialog.Ask(this, "New Entry",
            after is null ? $"New file at the end of {Path.GetFileName(folder)}:" : $"New file after {after}:",
            prefix, hint);
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (!FileTreeItem.IsMarkdown(name)) name += ".md";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show(this, $"\"{name}\" can't be a file name.", "New Entry", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var path = Path.Combine(folder, name);
        if (File.Exists(path))
        {
            MessageBox.Show(this, $"{name} is already there.", "New Entry", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // "001750-ogre-shaman.md" -> "# Ogre shaman"
        var words = NumberPrefix.Replace(Path.GetFileNameWithoutExtension(name), "").Replace('-', ' ').Trim();
        var heading = words.Length > 0 ? char.ToUpper(words[0]) + words[1..] : "New entry";
        File.WriteAllText(path, $"# {heading}\n", new System.Text.UTF8Encoding(false));
        if (File.Exists(index)) AddToIndex(index, name, after);

        ReloadTreesAt(folder);
        OpenDocument(path);
        StatusText.Text = "Created " + path + (File.Exists(index) ? " (added to its index)" : "");
    }

    /// <summary>A folder's entry names in order: from its index when it has one, else as Explorer lists them.</summary>
    private static List<string> EntriesOf(string folder)
    {
        var index = Path.Combine(folder, ProjectItem.IndexName);
        if (File.Exists(index))
            return File.ReadLines(index).Select(l => ProjectItem.Entry.Match(l)).Where(m => m.Success)
                       .Select(m => EntryName(m.Groups[1].Value)).ToList();
        var dir = new DirectoryInfo(folder);
        return dir.EnumerateDirectories().Select(d => d.Name).OrderBy(n => n, ExplorerOrder.Instance)
                  .Concat(dir.EnumerateFiles().Select(f => f.Name).Where(FileTreeItem.IsMarkdown).OrderBy(n => n, ExplorerOrder.Instance))
                  .ToList();
    }

    /// <summary>"0010-creatures/000000-index.md" -> "0010-creatures"; "ogre.md" -> "ogre.md".</summary>
    private static string EntryName(string target) =>
        target.EndsWith("/" + ProjectItem.IndexName, StringComparison.OrdinalIgnoreCase)
            ? target[..^(ProjectItem.IndexName.Length + 1)] : target;

    /// <summary>
    /// The number to start the new name with, and a note for the dialog. After a
    /// numbered entry: halfway to the next entry numbered with as many digits (files
    /// are 6 digits, folders 4, and they don't compare). At the end: the last such
    /// number plus 100 (6 digits) or 10 (4 digits).
    /// </summary>
    private static (string prefix, string? hint) SuggestPrefix(List<string> entries, string? after)
    {
        Match? from;
        int pos;
        if (after is null)
        {
            pos = entries.Count - 1;
            // the last numbered file, else the last numbered anything
            from = entries.Where(n => Path.HasExtension(n)).Select(n => NumberPrefix.Match(n)).LastOrDefault(m => m.Success)
                   ?? entries.Select(n => NumberPrefix.Match(n)).LastOrDefault(m => m.Success);
            if (from is not null) pos = entries.FindLastIndex(n => n.StartsWith(from.Value, StringComparison.Ordinal));
        }
        else
        {
            pos = entries.FindIndex(n => n.Equals(after, StringComparison.OrdinalIgnoreCase));
            from = NumberPrefix.Match(after) is { Success: true } m ? m : null;
        }
        if (from is null) return ("", "This folder's names aren't numbered.");

        var digits = from.Groups[1].Value.Length;
        var n = long.Parse(from.Groups[1].Value);
        var step = digits >= 6 ? 100 : 10;
        long? next = entries.Skip(pos + 1).Select(x => NumberPrefix.Match(x))
                            .Where(m => m.Success && m.Groups[1].Value.Length == digits)
                            .Select(m => (long?)long.Parse(m.Groups[1].Value)).FirstOrDefault(v => v > n);
        var mid = next is { } nx ? n + (nx - n) / 2 : n + step;
        var fmt = new string('0', digits);
        if (mid <= n)
            return (n.ToString(fmt) + "-", $"No free number between {n.ToString(fmt)} and {next!.Value.ToString(fmt)}; renumber, or pick a name yourself.");
        var between = next is { } nn ? $"Between {n.ToString(fmt)} and {nn.ToString(fmt)}." : $"After {n.ToString(fmt)}, the last one.";
        return (mid.ToString(fmt) + "-", between);
    }

    /// <summary>Add "- [name](name)" to the index after <paramref name="after"/>'s line, or at the end.</summary>
    private static void AddToIndex(string index, string name, string? after)
    {
        var lines = File.ReadAllLines(index).ToList();
        var at = after is null ? -1 : lines.FindIndex(l => ProjectItem.Entry.Match(l) is { Success: true } m &&
                                                          EntryName(m.Groups[1].Value).Equals(after, StringComparison.OrdinalIgnoreCase));
        if (at < 0) at = lines.FindLastIndex(l => ProjectItem.Entry.IsMatch(l));
        lines.Insert(at + 1, $"- [{name}]({name})");
        File.WriteAllText(index, string.Join("\n", lines) + "\n", new System.Text.UTF8Encoding(false));
    }

    /// <summary>Re-read every loaded tree entry for this folder, in both trees.</summary>
    private void ReloadTreesAt(string folder)
    {
        static bool Same(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        void WalkFiles(IEnumerable<FileTreeItem> items)
        {
            foreach (var i in items.Where(i => i.IsFolder).ToList())
                if (Same(i.FullPath, folder)) i.Reload(); else WalkFiles(i.Children);
        }
        void WalkProject(IEnumerable<ProjectItem> items)
        {
            foreach (var i in items.Where(i => i.IsFolder).ToList())
                if (Same(i.FullPath, folder)) i.Reload(); else WalkProject(i.Children);
        }
        if (FileTree.ItemsSource is IEnumerable<FileTreeItem> files) WalkFiles(files);
        if (ProjectTree.ItemsSource is IEnumerable<ProjectItem> project) WalkProject(project);
    }
}
