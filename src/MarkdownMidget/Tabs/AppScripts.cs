using System;
using System.IO;

namespace MarkdownMidget.Tabs;

/// <summary>
/// The scripts the app hands to other programs (today: html_bits.lua, the Pandoc
/// filter Build uses). They live in Tabs\scripts in the source, are built into the
/// exe as resources, and are written out to %LOCALAPPDATA%\EEEE\scripts when used.
/// That way the single-file exe carries them, an update (which replaces just the
/// exe) brings their new versions, and nothing needs writing beside the exe.
/// </summary>
public static class AppScripts
{
    public static string Folder => Path.Combine(AppIdentity.DataFolder, "scripts");

    /// <summary>The script's path on disk, written (or refreshed) from the exe first. Null if it can't be.</summary>
    public static string? Get(string name)
    {
        try
        {
            using var res = typeof(AppScripts).Assembly.GetManifestResourceStream("scripts/" + name);
            if (res is null) return null;
            using var mem = new MemoryStream();
            res.CopyTo(mem);
            var bytes = mem.ToArray();
            var path = Path.Combine(Folder, name);
            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(path, bytes);
            }
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
