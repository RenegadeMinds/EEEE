using System.Linq;

namespace MarkdownMidget.Tabs;

/// <summary>Which window a launch gets: the tabbed one unless told otherwise.</summary>
public static class TabsStartup
{
    /// <summary>Flags that mean "the classic one-document window": --notabs, plus the
    /// flags the classic window passes when it starts another copy of itself (File ▸ New,
    /// crash recovery, the help viewer, relaunch after an update, install move), so it
    /// keeps behaving exactly as before.</summary>
    private static readonly string[] ClassicFlags =
    {
        "--notabs", "--new", "--recover", "--finish-move", "--help-window",
        "--source", "/source", "--readonly", "-r", "/readonly",
    };

    public static bool UseClassicWindow(string[] args) => args.Any(a => ClassicFlags.Contains(a));
}
