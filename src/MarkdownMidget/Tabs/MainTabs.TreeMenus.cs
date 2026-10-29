using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MarkdownMidget.Tabs;

/// <summary>
/// Right-click menus on the Files and Project trees (FolderMenu / FileMenu in
/// MainTabs.xaml), and Build, which those menus, the tab menu and the toolbar's
/// Build drop-down share. A MenuItem's DataContext is the FileTreeItem, ProjectItem
/// or DocumentTab that was right-clicked.
/// </summary>
public partial class MainTabs
{
    private const int OpenAllWarnAt = 20;

    /// <summary>The right-clicked tree entry: its path, whether it's a folder, and a display name.</summary>
    private static (string Path, bool IsFolder, string Label)? TreeTarget(object sender) =>
        (sender as FrameworkElement)?.DataContext switch
        {
            FileTreeItem f => (f.FullPath, f.IsFolder, Path.GetFileNameWithoutExtension(f.Name)),
            ProjectItem p => (p.FullPath, p.IsFolder, p.Label),
            _ => null,
        };

    private static string NameOf(string path) => Path.GetFileNameWithoutExtension(path.TrimEnd('\\', '/'));

    // ---- Both -----------------------------------------------------------------

    private void TreeMenu_CopyPath(object sender, RoutedEventArgs e)
    {
        if (TreeTarget(sender) is { } t) Clipboard.SetText(t.Path);
    }

    private void TreeMenu_ShowInExplorer(object sender, RoutedEventArgs e)
    {
        if (TreeTarget(sender) is not { } t) return;
        // A folder opens in Explorer; a file opens its folder with the file selected.
        Process.Start("explorer.exe", t.IsFolder ? $"\"{t.Path}\"" : $"/select,\"{t.Path}\"");
    }

    // ---- Files ----------------------------------------------------------------

    private void TreeMenu_Open(object sender, RoutedEventArgs e)
    {
        if (TreeTarget(sender) is { IsFolder: false } t) OpenDocument(t.Path);
    }

    /// <summary>[ABOLETH](../A/aboleth.md): a link to the file, relative to the document in the current tab.</summary>
    private void TreeMenu_CopyLink(object sender, RoutedEventArgs e)
    {
        if (TreeTarget(sender) is not { } t) return;
        var from = CurrentDocument?.Path is { } cur ? Path.GetDirectoryName(cur)! : Path.GetDirectoryName(t.Path)!;
        var rel = Path.GetRelativePath(from, t.Path).Replace('\\', '/').Replace(" ", "%20");
        Clipboard.SetText($"[{t.Label}]({rel})");
        StatusText.Text = "Copied: " + $"[{t.Label}]({rel})";
    }

    // ---- Folders --------------------------------------------------------------

    /// <summary>Open All's two choices get their file counts each time the folder menu opens;
    /// a choice with no files is greyed out.</summary>
    private void FolderMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || TreeTarget(menu) is not { IsFolder: true } t) return;
        foreach (var item in menu.Items.OfType<MenuItem>().SelectMany(m => m.Items.OfType<MenuItem>()))
        {
            if (item.Tag is not ("here" or "all")) continue;
            var here = (string)item.Tag == "here";
            int n;
            try { n = ProjectItem.FilesInOrder(t.Path, subfolders: !here).Count(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { n = 0; }
            item.Header = (here ? "…in this folder" : "…and all subfolders") + $" ({n:N0})";
            item.IsEnabled = n > 0;
        }
    }

    private void TreeMenu_OpenAll(object sender, RoutedEventArgs e)
    {
        if (TreeTarget(sender) is not { IsFolder: true } t) return;
        var files = ProjectItem.FilesInOrder(t.Path, subfolders: (sender as MenuItem)?.Tag as string != "here").ToList();
        if (files.Count == 0) { StatusText.Text = "No Markdown files in " + t.Path; return; }
        if (files.Count > OpenAllWarnAt &&
            MessageBox.Show(this, $"Open {files.Count} files, one tab each?\n\nEvery tab stays loaded, so this many may be slow.",
                            "Open All", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        foreach (var f in files) OpenDocument(f);
    }

    /// <summary>Every file in the folder (book order, indexes left out) joined into one new file, then opened.</summary>
    private void TreeMenu_Concat(object sender, RoutedEventArgs e)
    {
        if (TreeTarget(sender) is not { IsFolder: true } t) return;
        var files = ProjectItem.FilesInOrder(t.Path).ToList();
        if (files.Count == 0) { StatusText.Text = "No Markdown files in " + t.Path; return; }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = $"Concatenate {files.Count} files into",
            FileName = NameOf(t.Path) + "-combined.md",
            InitialDirectory = Path.GetDirectoryName(t.Path.TrimEnd('\\', '/')),
            Filter = "Markdown (*.md)|*.md",
        };
        if (dlg.ShowDialog(this) != true) return;
        Concatenate(files, dlg.FileName);
        StatusText.Text = $"Concatenated {files.Count} files into {dlg.FileName}";
        OpenDocument(dlg.FileName);
    }

    /// <summary>
    /// Join files into one, as EEE\_tools\assemble.py does: links into the other files
    /// become links within the one file (file.md#x -> #x), and image paths are made
    /// relative to where the new file is.
    /// </summary>
    private static void Concatenate(IReadOnlyList<string> files, string dest)
    {
        var destDir = Path.GetDirectoryName(Path.GetFullPath(dest))!;
        var parts = new List<string>(files.Count);
        foreach (var f in files)
        {
            var txt = File.ReadAllText(f);
            if (files.Count > 1) txt = Regex.Replace(txt, @"\]\([^)\s#]+\.md#", "](#");
            var dir = Path.GetDirectoryName(f)!;
            txt = Regex.Replace(txt, @"(!\[[^\]]*\]\()([^)\s:]+)\)", m =>
                m.Groups[1].Value + Path.GetRelativePath(destDir, Path.GetFullPath(Path.Combine(dir, m.Groups[2].Value))).Replace('\\', '/') + ")");
            parts.Add(txt.Trim());
        }
        File.WriteAllText(dest, string.Join("\n\n", parts) + "\n", new UTF8Encoding(false));
    }

    // ---- Build ------------------------------------------------------------------

    private static string BuildFilter(string ext) => ext switch
    {
        "odt" => "Open Document (*.odt)|*.odt",
        "html" => "Web page (*.html)|*.html",
        "docx" => "Word (*.docx)|*.docx",
        _ => "PDF (*.pdf)|*.pdf",
    };

    /// <summary>Build from a tree menu (a file, or a folder in book order) or a tab menu (that file as saved).</summary>
    private async void Build_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is not string ext) return;
        string source; bool isFolder;
        if (TreeTarget(sender) is { } t) (source, isFolder) = (t.Path, t.IsFolder);
        else if (TabOf(sender) is { } tab)
        {
            if (tab.Path is null) { StatusText.Text = "Save the document first, then build it."; return; }
            (source, isFolder) = (tab.Path, false);
        }
        else return;

        var files = isFolder ? ProjectItem.FilesInOrder(source).ToList() : new List<string> { source };
        if (files.Count == 0) { StatusText.Text = "No Markdown files in " + source; return; }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = $"Build {(isFolder ? $"{files.Count} files" : Path.GetFileName(source))} as .{ext}",
            FileName = NameOf(source) + "." + ext,
            InitialDirectory = BuildFolderFor(source) ?? Path.GetDirectoryName(source.TrimEnd('\\', '/')),
            Filter = BuildFilter(ext),
        };
        if (dlg.ShowDialog(this) != true) return;
        await BuildAsync(source, files, ext, dlg.FileName, ContentsDepth.For(ProjectOf(source)));
    }

    /// <summary>The toolbar's Build drop-down: the whole project, into _build\BOOK.ext beside the project folder.</summary>
    private async void BuildProject_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is not string ext || _projectFolder is null || BookName is null) return;
        var files = ProjectItem.FilesInOrder(_projectFolder).ToList();
        string output;
        if (BuildFolderFor(_projectFolder, create: true) is { } buildDir) output = Path.Combine(buildDir, BookName + "." + ext);
        else
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = $"Build {BookName} as .{ext}", FileName = BookName + "." + ext, Filter = BuildFilter(ext),
                InitialDirectory = Path.GetDirectoryName(_projectFolder.TrimEnd('\\', '/')),
            };
            if (dlg.ShowDialog(this) != true) return;
            output = dlg.FileName;
        }
        await BuildAsync(_projectFolder, files, ext, output, ContentsDepth.For(_projectFolder));
    }

    /// <summary>The open project, if <paramref name="path"/> is in it; null otherwise.</summary>
    private string? ProjectOf(string path)
    {
        if (_projectFolder is null) return null;
        var project = Path.GetFullPath(_projectFolder).TrimEnd('\\');
        var full = Path.GetFullPath(path).TrimEnd('\\');
        return full.Equals(project, StringComparison.OrdinalIgnoreCase)
               || full.StartsWith(project + "\\", StringComparison.OrdinalIgnoreCase) ? _projectFolder : null;
    }

    /// <summary>Where builds go: a _build folder beside the open project (EEE\_build for EEE\FF),
    /// for anything in the project; null for anything else (the save dialog then starts beside it).</summary>
    private string? BuildFolderFor(string path, bool create = false)
    {
        if (ProjectOf(path) is not { } project || Path.GetDirectoryName(Path.GetFullPath(project).TrimEnd('\\')) is not { } parent) return null;
        var dir = Path.Combine(parent, "_build");
        try
        {
            if (create) Directory.CreateDirectory(dir);
            return Directory.Exists(dir) ? dir : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    // ---- Build menus: what's installed, and the contents depth ---------------------

    /// <summary>
    /// Before a Build menu shows: Pandoc and Word are looked for again (so installing one
    /// needs no restart), the formats that can't be made are greyed out, and a line at the
    /// end says what's missing (clicking it opens how to install it).
    /// </summary>
    private void PrepareBuildMenu(ItemsControl menu)
    {
        var tools = BuildTools.Find();
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            switch (item.Tag as string)
            {
                case "odt" or "html":
                    item.IsEnabled = tools.Pandoc is not null;
                    item.ToolTip = tools.Pandoc is null ? "Needs Pandoc" : null;
                    break;
                case "docx":
                    item.IsEnabled = tools.Pandoc is not null;
                    item.ToolTip = tools.Pandoc is null ? "Needs Pandoc"
                        : tools.Word ? null : "Without Word the contents page is empty until you update it in Word (or LibreOffice)";
                    break;
                case "pdf":
                    item.IsEnabled = tools.Pandoc is not null && tools.Word;
                    item.ToolTip = tools.Pandoc is null ? "Needs Pandoc" : tools.Word ? null : "Needs Microsoft Word";
                    break;
            }
        }
        // the "what's missing" line, refreshed every time
        foreach (var old in menu.Items.OfType<FrameworkElement>().Where(x => x.Tag as string == "tools-note").ToList())
            menu.Items.Remove(old);
        if (tools.Pandoc is null || !tools.Word)
        {
            menu.Items.Add(new Separator { Tag = "tools-note" });
            if (tools.Pandoc is null)
            {
                var get = new MenuItem { Header = "Pandoc isn't installed: how to get it…", Tag = "tools-note",
                                         ToolTip = "Opens pandoc.org/installing. Or in a terminal: winget install JohnMacFarlane.Pandoc" };
                get.Click += (_, _) => OpenUrl(BuildTools.PandocInstallUrl);
                menu.Items.Add(get);
            }
            if (!tools.Word)
                menu.Items.Add(new MenuItem { Header = "Microsoft Word isn't installed: no PDF", Tag = "tools-note", IsEnabled = false });
        }
    }

    /// <summary>A Build submenu (on a folder, file or tab) is opening.</summary>
    private void BuildMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) && sender is MenuItem m) PrepareBuildMenu(m);
    }

    /// <summary>The toolbar Build menu's "Contents depth" submenu: tick the project's depth.</summary>
    private void ContentsDepthMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem m || _projectFolder is null) return;
        var depth = ContentsDepth.For(_projectFolder);
        foreach (var item in m.Items.OfType<MenuItem>())
            item.IsChecked = item.Tag is string s && int.TryParse(s, out var d) && d == depth;
    }

    private void ContentsDepth_Click(object sender, RoutedEventArgs e)
    {
        if (_projectFolder is null || (sender as MenuItem)?.Tag is not string s || !int.TryParse(s, out var d)) return;
        ContentsDepth.Set(_projectFolder, d);
        StatusText.Text = $"Contents depth for {BookName}: headings 1–{d}";
    }

    private void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { StatusText.Text = "Couldn't open " + url + ": " + ex.Message; }
    }

    /// <summary>
    /// Join the files into a work file and run Pandoc on it (with the app's html_bits.lua
    /// filter). Word, when installed, fills in a .docx's contents, and makes the PDF from
    /// a .docx. Then open the result.
    /// </summary>
    private async Task BuildAsync(string source, List<string> files, string ext, string output, int tocDepth)
    {
        if (_toolRunning) { StatusText.Text = "Something is already running; wait for it to finish."; return; }
        var work = Path.Combine(Path.GetTempPath(), AppIdentity.DataFolderName);
        Directory.CreateDirectory(work);
        var md = Path.Combine(work, Path.GetFileNameWithoutExtension(output) + ".md");
        Concatenate(files, md);
        var pandocOut = ext == "pdf" ? Path.ChangeExtension(md, ".docx") : output;
        var title = File.ReadLines(md).Select(l => Regex.Replace(l, @"^#+\s*|<[^>]+>", "").Trim()).FirstOrDefault(l => l.Length > 0) ?? NameOf(source);

        var args = new List<string> { md, "-f", "gfm", "-o", pandocOut, "--resource-path=" + work,
                                      "--toc", "--toc-depth=" + tocDepth, "-M", "title=" + title, "-M", "toc-title=Contents" };
        args.AddRange(ext switch
        {
            "odt" => new[] { "-t", "odt" },
            "html" => new[] { "-t", "html5", "--standalone", "--embed-resources" },   // one file, images inside
            _ => new[] { "-t", "docx" },
        });
        if (AppScripts.Get("html_bits.lua") is { } lua)
            args.Add("--lua-filter=" + lua);
        var tools = BuildTools.Find();
        if (tools.Pandoc is null)
        {
            OutputList.Items.Clear();
            OutputPanel.Visibility = Visibility.Visible;
            OutputTitle.Text = $"Build .{ext}: {NameOf(source)}  (Pandoc isn't installed)";
            AddOutput("Building needs Pandoc (free): " + BuildTools.PandocInstallUrl);
            AddOutput("Or in a terminal: winget install JohnMacFarlane.Pandoc   (then try again; no restart needed)");
            return;
        }
        var (code, _) = await RunProcessAsync($"Build .{ext}: {NameOf(source)} ({files.Count} file{(files.Count == 1 ? "" : "s")})",
                                              tools.Pandoc, args, work);
        if (code != 0) return;

        if (ext is "docx" or "pdf")
        {
            AddOutput(ext == "pdf" ? "Word: filling in the contents and saving as PDF…" : "Word: filling in the contents…");
            var err = await Task.Run(() => WordFinish(pandocOut, ext == "pdf" ? output : null));
            if (err is not null)
            {
                AddOutput("Word couldn't do it: " + err);
                if (ext == "pdf") { AddOutput("No PDF made. (A PDF needs Word.)"); return; }
            }
        }
        AddOutput($"Done: {output}");
        try { Process.Start(new ProcessStartInfo(output) { UseShellExecute = true }); }
        catch (Exception ex) { AddOutput("Couldn't open it: " + ex.Message); }
    }

    /// <summary>Open a .docx in Word (hidden), update its table of contents, and save it, or save it as a PDF. Null when it worked.</summary>
    private static string? WordFinish(string docx, string? pdf)
    {
        var type = Type.GetTypeFromProgID("Word.Application");
        if (type is null) return "Word isn't installed.";
        dynamic? word = null;
        try
        {
            word = Activator.CreateInstance(type)!;
            word.Visible = false;
            word.DisplayAlerts = 0;
            dynamic doc = word.Documents.Open(docx, false, false, false);
            foreach (dynamic toc in doc.TablesOfContents) toc.Update();
            if (pdf is null) doc.Save();
            else doc.SaveAs2(pdf, 17);              // 17 = wdFormatPDF
            doc.Close(0);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
        finally
        {
            if (word is not null)
            {
                try { word.Quit(); } catch (COMException) { }
                Marshal.ReleaseComObject(word);
            }
        }
    }

    /// <summary>The nearest folder called <paramref name="name"/> beside the path or any folder above it.</summary>
    private static string? FindUp(string path, string name)
    {
        for (var dir = Path.GetDirectoryName(path.TrimEnd('\\', '/')); dir is not null; dir = Path.GetDirectoryName(dir))
            if (Directory.Exists(Path.Combine(dir, name))) return Path.Combine(dir, name);
        return null;
    }
}
