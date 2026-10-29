using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;

namespace MarkdownMidget;

/// <summary>
/// What EditorView (the classic window's editor, copied into a control) needs from
/// a window, provided by the tab that hosts it. The copied code calls these by the
/// names it used as a Window - Title, Close(), Activate(), Closed, Deactivated - so
/// the copy stays a plain copy (tools/make_editorview.py).
/// </summary>
public partial class EditorView
{
    /// <summary>The window this editor is in, for dialog owners and window handles.</summary>
    internal Window HostWindow => Window.GetWindow(this) ?? Application.Current.MainWindow;

    private string _title = AppIdentity.Name;
    /// <summary>"*name.md  |  Markdown Midget": the copied UpdateTitle sets it; the tab shows it.</summary>
    public string Title
    {
        get => _title;
        set { if (_title == value) return; _title = value; TitleChanged?.Invoke(this, EventArgs.Empty); }
    }
    public event EventHandler? TitleChanged;

    /// <summary>File ▸ New in this tab: the host opens a new blank tab.</summary>
    public event EventHandler? NewDocumentRequested;
    /// <summary>Opening a file while this tab has a document: the host opens it in a tab.</summary>
    public event EventHandler<string>? OpenDocumentRequested;
    /// <summary>Raised once the tab has closed (after any save prompt); the host removes it.</summary>
    public event EventHandler? Closed;
    /// <summary>The host raises this when the tab stops being the active one.</summary>
    public event EventHandler? Deactivated;
    /// <summary>The copied code calls Activate(); the host brings this tab forward.</summary>
    public event EventHandler? ActivateRequested;

    public bool IsDirty => _dirty;
    public string? CurrentPath => _currentPath;

    public void Activate() => ActivateRequested?.Invoke(this, EventArgs.Empty);
    internal void RaiseDeactivated() => Deactivated?.Invoke(this, EventArgs.Empty);

    /// <summary>What the classic window's Close() did: ask about unsaved changes, then let go.</summary>
    public void Close() => _ = TryCloseAsync();

    private bool _closeDone;

    /// <summary>
    /// Close this document: the classic window's closing steps (MainWindow_Closing),
    /// awaited. False when the user cancelled the save prompt; the tab stays.
    /// </summary>
    public async Task<bool> TryCloseAsync()
    {
        if (_closeDone) return true;
        if (_dirty)
        {
            if (!await ConfirmDiscardAsync()) return false;
            _dirty = false;
        }
        StopWatching();
        EndBackup();             // asked and answered: nothing left for a crash copy to rescue
        ReleaseDocumentClaim();
        _closeDone = true;
        Closed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
