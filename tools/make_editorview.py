"""make_editorview.py - create Tabs/EditorView.* from MainWindow.* (the classic
one-document window) so the tabbed window can host the same editor, one per tab.

    python tools/make_editorview.py

MainWindow is not changed. The copy differs only by mechanical renames (listed
in RENAMES) and a few hand-made replacements (listed in REPLACE); everything a
Window had that a control doesn't - Title, Close(), Activate(), the Closing /
Closed / Deactivated events, a dialog owner - comes from EditorView.Host.cs.
Re-run after pulling a new MainWindow from upstream; it refuses to run if a
replacement no longer matches, so nothing changes silently.
"""
import os, re, sys

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'MarkdownMidget')
DST = os.path.join(SRC, 'Tabs')
FILES = ['MainWindow.xaml', 'MainWindow.xaml.cs', 'MainWindow.AccessKeys.cs',
         'MainWindow.Mode.cs', 'MainWindow.Spell.cs', 'MainWindow.Theme.cs']

# Applied to every copied file (regex -> replacement).
RENAMES = [
    (r'partial class MainWindow : Window', 'partial class EditorView : UserControl'),
    (r'partial class MainWindow\b', 'partial class EditorView'),
    (r'x:Class="MarkdownMidget\.MainWindow"', 'x:Class="MarkdownMidget.EditorView"'),
    (r'MessageBox\.Show\(this,', 'MessageBox.Show(HostWindow,'),
    (r'Owner = this\b', 'Owner = HostWindow'),
    (r'new WindowInteropHelper\(this\)', 'new WindowInteropHelper(HostWindow)'),
    (r'<Window\.Resources>', '<UserControl.Resources>'),
    (r'</Window\.Resources>', '</UserControl.Resources>'),
    (r'^</Window>', '</UserControl>'),
    # helpers that take the owner window
    (r'\b(OpenLinkDialog\.Confirm|Picker\.FilePickerService\.Show|PasswordDialog\.\w+|DefaultApp\.\w+|InputDialog\.Single)\(this\b',
     r'\1(HostWindow'),
    # one settings type for both windows (same settings.json): the copy's own is renamed out of the way
    (r'internal sealed class AppSettings\b', 'internal sealed class AppSettingsUnused'),
    (r'internal sealed class PrintPrefs\b', 'internal sealed class PrintPrefsUnused'),
]

# Exact one-off replacements: (file, old, new). Each old must occur exactly once.
REPLACE = [
    # XAML root: a control, not a window (size, title, icon belong to MainTabs).
    ('MainWindow.xaml',
     '<Window x:Class="MarkdownMidget.EditorView"',
     '<UserControl x:Class="MarkdownMidget.EditorView"'),
    ('MainWindow.xaml',
     '        Title="MMEEEE" Height="720" Width="1120" MinHeight="360" MinWidth="520"\n'
     '        Icon="/Assets/midget-256.png"\n'
     '        WindowStartupLocation="CenterScreen"\n',
     ''),
    # The document comes from MainTabs, not the command line; no path = a new blank document.
    ('MainWindow.xaml.cs', 'public MainWindow()', 'public EditorView(string? path)'),
    ('MainWindow.xaml.cs',
     'var args = Environment.GetCommandLineArgs().Skip(1).ToArray();',
     'var args = Array.Empty<string>();   // EditorView: the document comes from MainTabs'),
    ('MainWindow.xaml.cs',
     '_pendingOpenPath = DocumentArgument(args);',
     '_pendingOpenPath = path;\n        _forceBlankDocument = path is null;   // EditorView: no path = File > New'),
    # Closing is TryCloseAsync (EditorView.Host.cs), called by MainTabs.
    ('MainWindow.xaml.cs', '        Closing += MainWindow_Closing;\n', ''),
    # Window placement is MainTabs' business.
    ('MainWindow.xaml.cs',
     'protected override void OnSourceInitialized(EventArgs e)\n    {\n        base.OnSourceInitialized(e);   // the HWND exists from here on\n',
     'private void OnSourceInitializedUnused()   // EditorView: placement is MainTabs\'\n    {\n        return;\n'),
    ('MainWindow.xaml.cs', 'else if (Restorable.Maximized) WindowState = WindowState.Maximized;', '// (EditorView: no window state)'),
    ('MainWindow.xaml.cs',
     'protected override void OnContentRendered(EventArgs e)\n    {\n        base.OnContentRendered(e);\n',
     'private void OnContentRenderedUnused()   // EditorView: placement is MainTabs\'\n    {\n        return;\n'),
    ('MainWindow.xaml.cs',
     'private void CaptureWindowPlacement()\n    {\n',
     'private void CaptureWindowPlacement()\n    {\n        return;   // EditorView: placement is MainTabs\'\n'),
    # File > New and opening more files: a new tab, not a new process.
    ('MainWindow.xaml.cs',
     '        var exe = Environment.ProcessPath;\n        if (exe is null) return;\n'
     '        try { Process.Start(new ProcessStartInfo(exe, "--new") { UseShellExecute = false }); }',
     '        NewDocumentRequested?.Invoke(this, EventArgs.Empty);   // EditorView: a new tab\n        return;\n'
     '        var exe = Environment.ProcessPath;\n        if (exe is null) return;\n'
     '        try { Process.Start(new ProcessStartInfo(exe, "--new") { UseShellExecute = false }); }'),
    ('MainWindow.xaml.cs',
     'OpenRouting.LaunchAll(route.Launch, OpenRouting.StartInstance)',
     'OpenRouting.LaunchAll(route.Launch, p => OpenDocumentRequested?.Invoke(this, p))   /* EditorView: a new tab */'),
]

HEADER = ('// GENERATED by tools/make_editorview.py from {src} - do not edit by hand;\n'
          '// change the script (or EditorView.Host.cs) and re-run it.\n')


def main():
    os.makedirs(DST, exist_ok=True)
    texts = {f: open(os.path.join(SRC, f), encoding='utf-8-sig').read() for f in FILES}
    for f in texts:
        for pat, rep in RENAMES:
            texts[f] = re.sub(pat, rep, texts[f], flags=re.M)
    for f, old, new in REPLACE:
        n = texts[f].count(old)
        if n != 1:
            sys.exit('%s: expected 1 match, found %d, for:\n%s' % (f, n, old))
        texts[f] = texts[f].replace(old, new)
    for f, t in texts.items():
        out = os.path.join(DST, f.replace('MainWindow', 'EditorView'))
        head = HEADER.format(src=f)
        if f.endswith('.xaml'):
            # XAML: the note goes in a comment just inside the root element
            t = t.replace('<UserControl.Resources>', '<!-- ' + head.replace('// ', '').replace('\n', ' ').strip()
                          + ' -->\n    <UserControl.Resources>', 1)
            open(out, 'w', encoding='utf-8', newline='').write(t)
        else:
            alias = ''.join('using %s = MarkdownMidget.MainWindow.%s;\n' % (n, n)
                            for n in ('AppSettings', 'PrintPrefs') if n in t)
            pragma = '#pragma warning disable CS0162   // the early returns above cut code on purpose\n' if 'EditorView:' in t else ''
            open(out, 'w', encoding='utf-8', newline='').write(head + pragma + alias + t)
        print('wrote', os.path.relpath(out, SRC))


if __name__ == '__main__':
    main()
