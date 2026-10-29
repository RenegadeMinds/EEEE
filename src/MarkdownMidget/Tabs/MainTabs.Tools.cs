using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace MarkdownMidget.Tabs;

/// <summary>
/// The project tools on the toolbar: Check Links (LinkChecker) and the Build
/// drop-down (MainTabs.TreeMenus.cs), with what they report in the Output panel.
/// </summary>
public partial class MainTabs
{
    private string? _projectFolder;
    private bool _toolRunning;

    /// <summary>The book's short name (FF, HH, GMG): the project folder's name.</summary>
    private string? BookName => _projectFolder is null ? null : Path.GetFileName(_projectFolder.TrimEnd('\\', '/'));

    /// <summary>Called when a project opens or a tool starts / stops.</summary>
    private void UpdateToolButtons()
    {
        CheckLinksButton.IsEnabled = BuildButton.IsEnabled = _projectFolder is not null && !_toolRunning;
        CheckLinksButton.ToolTip = _projectFolder is null ? "Open a project first."
            : $"Check every link in {BookName}: each must point at a file and an anchor that exist";
        BuildButton.ToolTip = _projectFolder is null ? "Open a project first."
            : $"Build all of {BookName} and open it (a minute or two)";
    }

    private async void CheckLinks_Click(object sender, RoutedEventArgs e)
    {
        if (_projectFolder is not { } folder || _toolRunning) return;
        _toolRunning = true;
        UpdateToolButtons();
        OutputList.Items.Clear();
        OutputTitle.Text = $"Check links: {BookName}  (running…)";
        OutputPanel.Visibility = Visibility.Visible;
        try
        {
            var result = await Task.Run(() => LinkChecker.Check(folder));
            foreach (var line in LinkChecker.Report(BookName!, result)) OutputList.Items.Add(line);
            OutputTitle.Text = $"Check links: {BookName}  ({(result.Broken.Count == 0 ? "all good" : $"{result.Broken.Count:N0} broken; double-click one to open it")})";
            StatusText.Text = "Links checked: " + BookName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddOutput("Couldn't read the project: " + ex.Message);
            OutputTitle.Text = $"Check links: {BookName}  (failed)";
        }
        finally
        {
            _toolRunning = false;
            UpdateToolButtons();
        }
    }

    /// <summary>The Build button is a drop-down: a click opens its menu under it.</summary>
    private void BuildButton_Click(object sender, RoutedEventArgs e)
    {
        if (BuildButton.ContextMenu is not { } menu) return;
        PrepareBuildMenu(menu);
        menu.PlacementTarget = BuildButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>Run a program, streaming what it prints to the Output panel. One at a time.</summary>
    private async Task<(int code, string output)> RunProcessAsync(string title, string exe, System.Collections.Generic.IEnumerable<string> args,
                                                                 string workDir, string? hintIfMissing = null, bool clear = true)
    {
        if (_toolRunning) return (-1, "");
        _toolRunning = true;
        UpdateToolButtons();
        if (clear) OutputList.Items.Clear();
        OutputTitle.Text = $"{title}  (running…)";
        OutputPanel.Visibility = Visibility.Visible;
        var all = new StringBuilder();
        var code = -1;
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                WorkingDirectory = workDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            void OnLine(string? line)
            {
                if (line is null) return;
                lock (all) all.AppendLine(line);
                Dispatcher.BeginInvoke(() => AddOutput(line));
            }
            p.OutputDataReceived += (_, ev) => OnLine(ev.Data);
            p.ErrorDataReceived += (_, ev) => OnLine(ev.Data);
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            await p.WaitForExitAsync();
            code = p.ExitCode;
        }
        catch (Exception ex)
        {
            AddOutput($"Couldn't run {Path.GetFileName(exe)}: {ex.Message}");
            if (hintIfMissing is not null) AddOutput(hintIfMissing);
        }
        finally
        {
            _toolRunning = false;
            UpdateToolButtons();
        }
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);   // let queued lines land
        OutputTitle.Text = $"{title}  ({(code == 0 ? "done" : "failed, exit code " + code)})";
        return (code, all.ToString());
    }

    private void AddOutput(string line)
    {
        OutputList.Items.Add(line);
        OutputList.ScrollIntoView(line);
    }

    private void CloseOutput_Click(object sender, RoutedEventArgs e) => OutputPanel.Visibility = Visibility.Collapsed;

    /// <summary>check_links.py lines look like "    0110-...\002700-matter-master.md:9 -> #_Compression,_Mass":
    /// double-click opens that file.</summary>
    private void OutputList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (OutputList.SelectedItem is not string line || _projectFolder is null) return;
        var m = Regex.Match(line, @"^\s*(\S.*?\.md)(?::\d+)?\s+->");
        if (!m.Success) return;
        var file = Path.Combine(_projectFolder, m.Groups[1].Value);
        if (File.Exists(file)) OpenDocument(file);
    }
}
