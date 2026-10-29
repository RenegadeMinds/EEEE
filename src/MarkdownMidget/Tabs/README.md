# Tabs — the MMEEEE additions

Everything MMEEEE adds to Markdown Midget lives in this folder, so upstream changes
merge with as little friction as possible. Outside it, the fork changes only:

- `AppIdentity.cs` (new): the app's name (**MMEEEE**), data folder (**EEEE**) and
  update repository (**RenegadeMinds/EEEE**). Every place upstream hard-coded
  "Markdown Midget" / `MarkdownMidget` for a folder, a registry key, a window title or
  a dialog caption now uses it.
- `App.xaml.cs`: starts the tabbed window (`MainTabs`) unless `--notabs` (or one of the
  classic window's own switches) is given.
- `MarkdownMidget.csproj`: exe name `MMEEEE`, version, and `scripts\html_bits.lua` built in.
- `RegistrationService.cs`, `Updates\*`, `AboutDialog.*`: MMEEEE's own "Open with"
  entries and update feed; never touches an installed Markdown Midget's.

## Files

| File | What it is |
|---|---|
| `MainTabs.xaml` / `.xaml.cs` | The tabbed window: toolbar, Files / Project panes, tab strip, Output panel. Every right-click menu is a keyed `ContextMenu` in `Window.Resources`. |
| `MainTabs.Menus.cs` | The tab right-click menu (close others / all / to the right, copy path …). |
| `MainTabs.TreeMenus.cs` | The Files / Project right-click menus, Concatenate, Open All, and **Build** (Pandoc, then Word for DOCX contents and PDF). |
| `MainTabs.NewEntry.cs` | **New Entry…**: numbered name halfway to the next entry, added to the folder's index. |
| `MainTabs.Tools.cs` | **Check Links**, the Build drop-down, and the Output panel (runs a program and streams what it prints). |
| `LinkChecker.cs` | Check Links in C# (same rules as `scripts\converter\check_links.py`). |
| `BuildTools.cs` | Finds Pandoc and Word for the Build menus; **Contents depth** per project. |
| `AppScripts.cs` | Writes the built-in scripts (`html_bits.lua`) out to `%LocalAppData%\EEEE\scripts`. |
| `ProjectItem.cs` | Project pane entries, read from each folder's `000000-index.md` (book order); `FilesInOrder` for Open All / Concatenate / Build. |
| `FileTreeItem.cs` | Files pane entries (folders first, Explorer order). |
| `ExplorerOrder.cs` | Sorts names exactly as Explorer does (Windows' `StrCmpLogicalW`). |
| `DocumentTab.cs` | One open document: its editor and tab title. |
| `InputDialog.cs` | A small "type a name" box (New Entry). |
| `TabsStartup.cs` | Which command lines start the classic window instead of tabs. |
| `EditorView.*` | **Generated — don't edit.** The classic window's editor as a control, one per tab. Made from `MainWindow.*` by `tools\make_editorview.py`. |
| `EditorView.Host.cs` | Hand-written: what `EditorView` needs that a `Window` had (title, close, owner …). |
| `scripts\` | `html_bits.lua` (used by Build) and the Word → Markdown converter. See `scripts\README.md`. |
| `RELEASE-README.md` | The README that goes in the release zip. |
| `publish.ps1` | Makes the release zip locally (the release workflow does the same on GitHub). |

## Data the app keeps (in `%LocalAppData%\EEEE`)

Besides upstream's settings, recent files, backups, themes and dictionary:
`tabs-folder.txt` (last Files folder), `tabs-project.txt` (last project),
`contents-depth.json` (Build ▸ Contents depth, per project), `scripts\`.

## A book folder ("project")

The app works with any folder of Markdown files; a **project** is one with a
`000000-index.md` in it, as the converter makes them:

- every folder has `000000-index.md`, one `- [name](target)` line per entry in reading
  order; a sub-folder's target is its own index (`0010-x/000000-index.md`);
- a folder's intro is `000050-….md` (clicking the folder opens it);
- numbered names: 4 digits for folders, 6 for files, in steps of 10 / 100 so there is
  room to insert (New Entry); A–Z folders (`A\`, `B\` …) hold unnumbered entries.

Builds of a project go to `_build\` beside the project folder (`EEE\_build\FF.docx`).

## Building

```bat
dotnet build src\MarkdownMidget
dotnet run --project src\MarkdownMidget            REM tabs
dotnet run --project src\MarkdownMidget -- --notabs REM the classic window
dotnet test tests\MarkdownMidget.Tests
```

Needs the **.NET 10 SDK**. The editor bundle (`wwwroot\`) is checked in; Node is needed
only to change `editor-src\`.

## Releasing

`publish.ps1` makes `dist\MMEEEE-vX.Y.Z-win-x64.exe` (self-contained, single file: no
.NET needed to run it) and `dist\MMEEEE-vX.Y.Z-win-x64.zip` (the exe, this folder's
`RELEASE-README.md` as `README.md`, and `LICENSE`):

```powershell
.\src\MarkdownMidget\Tabs\publish.ps1            # version from the csproj
.\src\MarkdownMidget\Tabs\publish.ps1 -Version 1.1.1
```

Or push a tag `v1.1.0`: `.github\workflows\release.yml` builds, tests and attaches
both to a GitHub release, with the notes taken from that version's CHANGELOG section.
The app's updater looks for an asset ending `-win-x64.exe` on
`github.com/RenegadeMinds/EEEE/releases`.

## Pulling in Markdown Midget updates

```bat
git fetch upstream
git merge upstream/master
python tools\make_editorview.py
```

`make_editorview.py` regenerates `EditorView.*` from the new `MainWindow.*`. If a
replacement it relies on no longer matches, it stops and says which; fix the rule
in the script. Pushes to `upstream` are blocked on purpose.
