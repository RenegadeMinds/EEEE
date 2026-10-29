using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MarkdownMidget.Tabs;

/// <summary>
/// Check Links: every internal link in a book folder (the C# version of
/// scripts\converter\check_links.py, so the app needs no Python).
///
/// A link [text](file.md#anchor) is good when the file exists and holds
/// &lt;a id="anchor"&gt;&lt;/a&gt;; [text](#anchor) looks in the same file. Each index
/// entry ("- [name](file.md)") must point at a file that exists. Web links are
/// skipped.
/// </summary>
public static class LinkChecker
{
    private static readonly Regex Link = new(@"\]\(([^()\s#]*)#((?:[^()\s]|\([^()\s]*\))+)\)", RegexOptions.Compiled);
    private static readonly Regex Anchor = new(@"<a id=""([^""]+)""></a>", RegexOptions.Compiled);
    private static readonly Regex IndexEntry = new(@"^- \[[^\]]*\]\(([^)]+)\)", RegexOptions.Compiled | RegexOptions.Multiline);

    public sealed record Broken(string File, int Line, string Target, bool MissingFile);

    public sealed record Result(int Files, int Good, IReadOnlyList<Broken> Broken);

    public static Result Check(string bookFolder)
    {
        var files = Directory.EnumerateFiles(bookFolder, "*.md", SearchOption.AllDirectories)
                             .OrderBy(f => f, ExplorerOrder.Instance).ToList();
        var texts = files.ToDictionary(f => Key(f), File.ReadAllText, StringComparer.OrdinalIgnoreCase);
        var anchors = texts.ToDictionary(kv => kv.Key, kv => Anchor.Matches(kv.Value).Select(m => m.Groups[1].Value).ToHashSet(),
                                         StringComparer.OrdinalIgnoreCase);
        var good = 0;
        var broken = new List<Broken>();
        foreach (var f in files)
        {
            var dir = Path.GetDirectoryName(f)!;
            var lines = texts[Key(f)].Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in Link.Matches(lines[i]))
                {
                    var path = m.Groups[1].Value;
                    var frag = m.Groups[2].Value;
                    if (path.Contains("://")) continue;                     // a web link
                    var target = path.Length == 0 ? Key(f) : Key(Path.Combine(dir, Uri.UnescapeDataString(path)));
                    if (anchors.TryGetValue(target, out var ids) && ids.Contains(frag)) good++;
                    else broken.Add(new Broken(Rel(bookFolder, f), i + 1, path + "#" + frag, !anchors.ContainsKey(target)));
                }
            }
            if (Path.GetFileName(f).Equals(ProjectItem.IndexName, StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match m in IndexEntry.Matches(texts[Key(f)]))
                {
                    if (File.Exists(Path.Combine(dir, m.Groups[1].Value))) good++;
                    else broken.Add(new Broken(Rel(bookFolder, f), 0, m.Groups[1].Value, true));
                }
            }
        }
        return new Result(files.Count, good, broken);
    }

    /// <summary>The report the Output panel shows. Broken-link lines read
    /// "    folder\file.md:12 -> target.md#anchor", which a double-click opens.</summary>
    public static IEnumerable<string> Report(string book, Result r)
    {
        yield return $"{book}: {r.Files:N0} files, {r.Good:N0} links good, {r.Broken.Count:N0} broken";
        var missingFile = r.Broken.Count(b => b.MissingFile);
        if (missingFile > 0) yield return $"    {missingFile:N0} missing file";
        if (r.Broken.Count - missingFile > 0) yield return $"    {r.Broken.Count - missingFile:N0} missing anchor";
        foreach (var b in r.Broken) yield return $"    {b.File}:{b.Line} -> {b.Target}";
    }

    private static string Key(string path) => Path.GetFullPath(path);

    private static string Rel(string root, string path) => Path.GetRelativePath(root, path);
}
