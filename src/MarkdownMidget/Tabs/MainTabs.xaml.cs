using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MarkdownMidget.Tabs;

/// <summary>
/// The tabbed window (the default; <c>--notabs</c> starts the classic one-document
/// window instead). Left: a Files tree of a folder. Right: one tab per open
/// document. Top: a toolbar for workspace-wide actions.
///
/// Documents live in <see cref="Docs"/>; their views all sit in <c>DocHost</c>
/// and only the selected one is visible, so none is ever unloaded while open.
/// </summary>
public partial class MainTabs : Window
{
    private static readonly string StateFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppIdentity.DataFolderName, "tabs-folder.txt");

    public ObservableCollection<DocumentTab> Docs { get; } = new();

    public MainTabs(string? openPath = null)
    {
        // Light / dark from the same setting the editor uses, applied before the window
        // shows (so a dark start has no light flash) and followed while it's open.
        var appearance = WindowsAppearance.Start(Dispatcher, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppIdentity.DataFolderName, "settings.json"));
        appearance.Follow(Chrome.ChromePalette.Apply);
        Closed += (_, _) => appearance.Dispose();

        InitializeComponent();
        DocTabs.ItemsSource = Docs;

        // The Files tree starts at the folder used last time, else the folder of
        // the file we were started with, else Documents.
        var folder = ReadLastFolder()
                     ?? (openPath is not null ? Path.GetDirectoryName(Path.GetFullPath(openPath)) : null)
                     ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        ShowFolder(folder);
        UpdateToolButtons();
        RestoreProject();
        if (openPath is not null) OpenDocument(openPath);
        UpdateTitle();
    }

    // ---- Files tree ---------------------------------------------------------

    private void ShowFolder(string folder)
    {
        if (!Directory.Exists(folder)) return;
        var root = new FileTreeItem(folder, isFolder: true);
        FileTree.ItemsSource = new[] { root };
        root.IsExpanded = true;
        RootLabel.Text = root.Name;
        RootLabel.ToolTip = folder;
        StatusText.Text = folder;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            File.WriteAllText(StateFile, folder);
        }
        catch (IOException) { /* not remembering the folder is fine */ }
        catch (UnauthorizedAccessException) { }
    }

    private static string? ReadLastFolder()
    {
        try
        {
            var f = File.Exists(StateFile) ? File.ReadAllText(StateFile).Trim() : null;
            return f is { Length: > 0 } && Directory.Exists(f) ? f : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>One click on a Markdown file opens it (or switches to its tab).</summary>
    private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FileTreeItem { IsFolder: false } item) OpenDocument(item.FullPath);
    }

    /// <summary>Enter on a folder expands or collapses it.</summary>
    private void FileTree_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && FileTree.SelectedItem is FileTreeItem { IsFolder: true } folder)
        {
            folder.IsExpanded = !folder.IsExpanded;
            e.Handled = true;
        }
    }

    // ---- Toolbar ------------------------------------------------------------

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Show this folder in the Files pane" };
        if (dlg.ShowDialog(this) == true) ShowFolder(dlg.FolderName);
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open Markdown",
            Filter = "Markdown (*.md;*.markdown)|*.md;*.markdown|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        foreach (var f in dlg.FileNames) OpenDocument(f);
    }

    private void NewFile_Click(object sender, RoutedEventArgs e) => NewDocument();

    // ---- Project ------------------------------------------------------------

    private static readonly string ProjectStateFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppIdentity.DataFolderName, "tabs-project.txt");

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Open a book folder as a project (it has a 000000-index.md)" };
        if (dlg.ShowDialog(this) != true) return;
        if (!ShowProject(dlg.FolderName))
            MessageBox.Show(this, $"{dlg.FolderName}\n\nhas no {ProjectItem.IndexName}, so it isn't a project.\n" +
                            "A project is a book folder such as EEE\\FF, made by the converter.",
                            "Open Project", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Show a project in the Project pane and bring the pane forward. False when it isn't one.</summary>
    private bool ShowProject(string folder, bool select = true)
    {
        var root = ProjectItem.OpenProject(folder);
        if (root is null) return false;
        ProjectTree.ItemsSource = new[] { root };
        _projectFolder = folder;
        UpdateToolButtons();
        ProjectHint.Visibility = Visibility.Collapsed;
        if (select) LeftPanes.SelectedItem = ProjectPane;
        StatusText.Text = "Project: " + folder;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProjectStateFile)!);
            File.WriteAllText(ProjectStateFile, folder);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return true;
    }

    /// <summary>The project open last time, shown again at startup (Files stays selected).</summary>
    private void RestoreProject()
    {
        try
        {
            var f = File.Exists(ProjectStateFile) ? File.ReadAllText(ProjectStateFile).Trim() : "";
            if (f.Length > 0 && ProjectItem.IsProject(f)) ShowProject(f, select: false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void ProjectTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // One click opens a file. A folder only gets selected (clicking to look inside
        // shouldn't open tabs); double-click opens its intro.
        if (e.NewValue is ProjectItem { IsFolder: false } item) OpenDocument(item.FullPath);
    }

    private void ProjectTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProjectTree.SelectedItem is ProjectItem { IsFolder: true, Intro: { } intro }) OpenDocument(intro);
    }

    private void ProjectTree_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ProjectTree.SelectedItem is ProjectItem { IsFolder: true } folder)
        {
            folder.IsExpanded = !folder.IsExpanded;
            e.Handled = true;
        }
    }

    // ---- Document tabs ------------------------------------------------------

    /// <summary>Open a file in a new tab, or switch to its tab if it's already open.</summary>
    public void OpenDocument(string path)
    {
        var full = Path.GetFullPath(path);
        var tab = Docs.FirstOrDefault(d => string.Equals(d.Path, full, StringComparison.OrdinalIgnoreCase))
                  ?? AddTab(full);
        DocTabs.SelectedItem = tab;
    }

    /// <summary>A new blank document in its own tab (File ▸ New).</summary>
    public void NewDocument() => DocTabs.SelectedItem = AddTab(null);

    private DocumentTab AddTab(string? path)
    {
        var tab = new DocumentTab(path);
        var ed = tab.Editor;
        ed.NewDocumentRequested += (_, _) => NewDocument();
        ed.OpenDocumentRequested += (_, p) => OpenDocument(p);
        ed.ActivateRequested += (_, _) => { DocTabs.SelectedItem = tab; Activate(); };
        ed.Closed += (_, _) => RemoveTab(tab);
        ed.TitleChanged += (_, _) => { if (ReferenceEquals(tab, CurrentDocument)) UpdateTitle(); };
        Docs.Add(tab);
        DocHost.Children.Add(ed);
        return tab;
    }

    private async void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.Tag is DocumentTab tab)
        {
            DocTabs.SelectedItem = tab;          // so a save prompt is about the tab you can see
            await tab.Editor.TryCloseAsync();    // asks about unsaved changes; Closed removes the tab
        }
    }

    private void RemoveTab(DocumentTab tab)
    {
        var index = Docs.IndexOf(tab);
        if (index < 0) return;
        DocHost.Children.Remove(tab.Editor);
        Docs.RemoveAt(index);
        if (Docs.Count > 0) DocTabs.SelectedIndex = Math.Min(index, Docs.Count - 1);
        UpdateTitle();
    }

    /// <summary>Show the selected tab's editor; hide (not unload) the others.</summary>
    private DocumentTab? _shown;
    private void DocTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, DocTabs)) return;   // ignore bubbling from inside tabs
        var current = CurrentDocument;
        if (_shown is not null && !ReferenceEquals(_shown, current)) _shown.Editor.RaiseDeactivated();
        foreach (var d in Docs) d.Editor.Visibility = ReferenceEquals(d, current) ? Visibility.Visible : Visibility.Collapsed;
        _shown = current;
        UpdateTitle();
    }

    public DocumentTab? CurrentDocument => DocTabs.SelectedItem as DocumentTab;

    private void UpdateTitle()
    {
        Title = CurrentDocument?.Editor.Title ?? AppIdentity.Name;
        if (CurrentDocument is { } doc) StatusText.Text = doc.Path ?? "Untitled";
        EmptyHint.Visibility = Docs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- Closing the window: every tab gets its save prompt ------------------

    private bool _closingChecked;

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closingChecked || Docs.Count == 0) return;
        e.Cancel = true;                                   // ask each tab first, then close for real
        foreach (var tab in Docs.ToList())
        {
            DocTabs.SelectedItem = tab;
            if (!await tab.Editor.TryCloseAsync()) return; // Cancel on a prompt: the window stays
        }
        _closingChecked = true;
        _ = Dispatcher.BeginInvoke(new Action(Close));     // not inside this Closing call
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        CurrentDocument?.Editor.RaiseDeactivated();
    }
}
