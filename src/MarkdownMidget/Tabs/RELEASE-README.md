# MMEEEE — Epics & Epochs Encyclopedia Editor

A Markdown editor for writing the Epics & Epochs rulebooks: tabs, a project pane that
shows a book in reading order, and one-click builds to Word, Open Document, web page
and PDF. Built on [Markdown Midget](https://github.com/FuncularLabs/MarkdownMidget).

## Run it

`MMEEEE.exe` is the whole app: put it anywhere and double-click it. There's no
installer. To delete it, delete the exe (and `%LocalAppData%\EEEE`, where its settings
live).

Windows may warn that it doesn't recognise the app the first time: choose
**More info ▸ Run anyway**.

## What you need

| | |
|---|---|
| **Windows 10 or 11** (64-bit) | |
| **Microsoft Edge WebView2** | Already on Windows 11. On Windows 10, if the app says it's missing: <https://developer.microsoft.com/microsoft-edge/webview2/> |
| **Pandoc** (free), for **Build** | <https://pandoc.org/installing.html>, or in a terminal: `winget install JohnMacFarlane.Pandoc` |
| **Microsoft Word**, optional | Fills in a Word build's contents page, and makes PDFs. Without it, Word / ODT / web page builds still work. |

Nothing else: .NET is built into the exe. The **Build** menus show what's installed,
grey out what can't be made, and link to Pandoc if it's missing. Install it while
the app is open and the next Build uses it.

## First steps

1. **Open Project** on the toolbar, and pick a book folder (one with a
   `000000-index.md`, such as `EEE\FF`). The **Project** pane shows the book in order.
2. Click an entry to open it in a tab. Right-click entries and tabs for more.
3. **Check Links** lists any link that points nowhere; double-click one to go there.
4. **Build ▾** makes the whole book (into a `_build` folder beside it).
   **Build ▾ ▸ Contents depth** sets how many heading levels its contents page lists.

**Help ▸ Help** in a document window has the full guide, and
**Help ▸ About MMEEEE** checks for updates.

Source, issues and new versions: <https://github.com/RenegadeMinds/EEEE>
