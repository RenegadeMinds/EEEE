using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MarkdownMidget.Tabs;

/// <summary>
/// The programs Build uses, looked for each time a Build menu opens:
/// Pandoc (every format) and Microsoft Word (fills in a .docx's contents; makes PDFs).
/// </summary>
public static class BuildTools
{
    public const string PandocInstallUrl = "https://pandoc.org/installing.html";

    /// <summary>Pandoc's path (null if not found) and whether Word is installed.</summary>
    public sealed record Found(string? Pandoc, bool Word);

    public static Found Find() => new(FindPandoc(), Type.GetTypeFromProgID("Word.Application") is not null);

    /// <summary>Where the Pandoc installer and winget put it, else anywhere on the PATH.</summary>
    private static string? FindPandoc()
    {
        var candidates = new List<string>
        {
            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Pandoc\pandoc.exe"),
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Pandoc\pandoc.exe"),
        };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Trim().Length == 0) continue;
            try { candidates.Add(Path.Combine(dir.Trim().Trim('"'), "pandoc.exe")); }
            catch (ArgumentException) { /* a malformed PATH entry */ }
        }
        return candidates.FirstOrDefault(File.Exists);
    }
}

/// <summary>
/// How many heading levels a built book's contents page lists, per project
/// (Build ▸ Contents depth on the toolbar). Kept in %LOCALAPPDATA%\EEEE\contents-depth.json.
/// Until one is chosen: FF 5 and everything else 3, as in the original Word books.
/// </summary>
public static class ContentsDepth
{
    private static string File_ => Path.Combine(AppIdentity.DataFolder, "contents-depth.json");

    public static int For(string? project)
    {
        if (project is null) return 3;
        if (Load().TryGetValue(Key(project), out var d)) return d;
        return Path.GetFileName(project.TrimEnd('\\', '/')).Equals("FF", StringComparison.OrdinalIgnoreCase) ? 5 : 3;
    }

    public static void Set(string project, int depth)
    {
        var all = Load();
        all[Key(project)] = Math.Clamp(depth, 1, 6);
        try
        {
            Directory.CreateDirectory(AppIdentity.DataFolder);
            File.WriteAllText(File_, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* not remembered; still used now */ }
    }

    private static Dictionary<string, int> Load()
    {
        try
        {
            if (File.Exists(File_) && JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(File_)) is { } d)
                return new Dictionary<string, int>(d, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    private static string Key(string project) => Path.GetFullPath(project).TrimEnd('\\', '/');
}
