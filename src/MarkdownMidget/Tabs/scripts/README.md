# scripts

## `html_bits.lua` — used by the app

The Pandoc filter **Build** runs. The Markdown keeps a few things as HTML because
Markdown has no syntax for them (`<sup>`, `<sub>`, `<u>`, bookmark anchors
`<a id="…"></a>`); Word, Open Document and PDF would drop raw HTML, so the filter turns
each into the real thing.

It is built into `MMEEEE.exe` (see `MarkdownMidget.csproj`) and written to
`%LocalAppData%\EEEE\scripts\` when a build needs it (`Tabs\AppScripts.cs`), so the
single-file exe carries it and an update brings the new version. Edit it here.

## `converter\` — not used by the app

The Python tools that turned the Word rulebooks into the Markdown book folders the app
edits, and that check and rebuild them from the command line. The app does what it
needs of them itself (Check Links and Build are C#), so users don't need Python.

| Script | What it does |
|---|---|
| `docx2eee.py` | A Word rulebook → a book folder of Markdown files (`FF\0010-chapter-1-creatures\…`), with `000000-index.md` in every folder (the reading order), anchors on every heading, Word's bookmarks kept, and anomaly lists in `_anomalies\`. Uses `tables.lua` and `html_tables.py`. |
| `tables.lua` | Pandoc filter for the conversion: table titles and header rows. |
| `html_tables.py` | Tables Pandoc could only write as HTML (merged cells, lists in cells) → Markdown tables. Also runs on its own: `python html_tables.py FILE.md …` |
| `assemble.py` | Joins a book back into one Markdown file, following the indexes. |
| `build_docx.py` | A book → Word file with a contents page (assembles, runs Pandoc with `..\html_bits.lua`, then has Word fill in the contents). |
| `check_links.py` | Checks every internal link (the app's **Check Links** does the same). |

### Running them

Run them from the folder that holds the book folders (or set `EEE_ROOT` to it).
Output goes there too: the book folder, `_anomalies\`, `_build\` and a `_work\` scratch
folder.

```bat
cd C:\path\to\EEE
python C:\path\to\scripts\converter\docx2eee.py "E&E Foe Folio.docx" FF
python C:\path\to\scripts\converter\check_links.py FF HH GMG
python C:\path\to\scripts\converter\build_docx.py FF
```

`docx2eee.py` empties and rebuilds the book folder each time. To compare first, set
`EEE_OUT_ROOT` to another folder and it writes there instead.

### What they need

- **Python 3.12** or later
- **lxml** (`pip install lxml`) for `docx2eee.py` and `html_tables.py`
- **Pandoc 3** for `docx2eee.py` and `build_docx.py`
- **Microsoft Word** for `build_docx.py` to fill in the contents page (optional)
