using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace MarkdownMidget.Tabs;

/// <summary>Minimal INotifyPropertyChanged base for the tab models.</summary>
public abstract class NotifyBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value; OnPropertyChanged(name); return true;
    }
}

/// <summary>
/// One open document: the editor that shows it (EditorView - the classic window's
/// editor as a control) and the name on its tab. MainTabs keeps every editor in one
/// Grid and only hides the inactive ones, so an editor (and its WebView2) is never
/// unloaded while its tab is open.
/// </summary>
public sealed class DocumentTab : NotifyBase
{
    public DocumentTab(string? path)
    {
        _openedPath = path;
        Editor = new EditorView(path) { Visibility = Visibility.Collapsed };
        Editor.TitleChanged += (_, _) => { OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(ToolTip)); };
    }

    public EditorView Editor { get; }

    /// <summary>The file this tab was opened on. A tab's editor only reads its file the
    /// first time the tab is shown, so until then this stands in for its path and name.</summary>
    private readonly string? _openedPath;

    /// <summary>The document's path once it has one (null for a new, unsaved document).</summary>
    public string? Path => Editor.CurrentPath ?? _openedPath;

    /// <summary>The tab text: the editor's own title ("*name.md (read-only)  |  Markdown
    /// Midget") without the product part; the file name until the editor has loaded it.</summary>
    public string Title
    {
        get
        {
            if (Editor.CurrentPath is null && _openedPath is not null) return System.IO.Path.GetFileName(_openedPath);
            var t = Editor.Title;
            var bar = t.IndexOf("  |  ", StringComparison.Ordinal);
            return bar > 0 ? t[..bar] : t;
        }
    }

    public string ToolTip => Path ?? Title;
}
