using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MarkdownMidget.Tabs;

/// <summary>
/// Right-click menus. Each menu is a keyed ContextMenu in MainTabs.xaml, attached
/// to its items through a style. A MenuItem's DataContext is the item that was
/// right-clicked (here, the DocumentTab), so the handlers read it from the sender.
/// </summary>
public partial class MainTabs
{
    /// <summary>The DocumentTab whose menu this click came from.</summary>
    private static DocumentTab? TabOf(object sender) => (sender as FrameworkElement)?.DataContext as DocumentTab;

    /// <summary>Close tabs one by one, each with its save prompt. Stops if one is cancelled.</summary>
    private async Task<bool> CloseTabsAsync(IEnumerable<DocumentTab> tabs)
    {
        foreach (var tab in tabs.ToList())
        {
            DocTabs.SelectedItem = tab;                    // so a save prompt is about the tab you can see
            if (!await tab.Editor.TryCloseAsync()) return false;
        }
        return true;
    }

    private async void TabMenu_Close(object sender, RoutedEventArgs e)
    {
        if (TabOf(sender) is { } tab) await CloseTabsAsync(new[] { tab });
    }

    private async void TabMenu_CloseOthers(object sender, RoutedEventArgs e)
    {
        if (TabOf(sender) is { } keep) await CloseTabsAsync(Docs.Where(d => !ReferenceEquals(d, keep)));
    }

    private async void TabMenu_CloseToRight(object sender, RoutedEventArgs e)
    {
        if (TabOf(sender) is { } tab) await CloseTabsAsync(Docs.Skip(Docs.IndexOf(tab) + 1));
    }

    private async void TabMenu_CloseAll(object sender, RoutedEventArgs e) => await CloseTabsAsync(Docs);

    private void TabMenu_CopyPath(object sender, RoutedEventArgs e)
    {
        if (TabOf(sender)?.Path is { } path) Clipboard.SetText(path);
    }

    private void TabMenu_ShowInExplorer(object sender, RoutedEventArgs e)
    {
        if (TabOf(sender)?.Path is { } path) Process.Start("explorer.exe", $"/select,\"{path}\"");
    }
}
