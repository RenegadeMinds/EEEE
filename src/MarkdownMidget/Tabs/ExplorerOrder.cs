using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MarkdownMidget.Tabs;

/// <summary>
/// Sorts names the way Windows Explorer does ("ogre.md", "ogre-mage.md",
/// "ogre-mage-ogre-mages-as-characters.md"; "file2" before "file10"), using the
/// same Windows function Explorer uses.
/// </summary>
public sealed class ExplorerOrder : IComparer<string>
{
    public static readonly ExplorerOrder Instance = new();

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string a, string b);

    public int Compare(string? x, string? y) =>
        x is null || y is null ? string.CompareOrdinal(x, y) : StrCmpLogicalW(x, y);
}
