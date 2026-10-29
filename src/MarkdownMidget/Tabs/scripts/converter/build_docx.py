"""build_docx.py - make a Word file from a book folder.

    python build_docx.py FF [HH GMG ...]

1. assemble.py puts the book's Markdown files together in index order.
2. Pandoc turns that into EEE/_build/<BOOK>.docx, with html_bits.lua so
   anchors become Word bookmarks (links work), and HTML tables, <sup>, <u>
   become real Word tables / superscript / underline. The book's first line
   becomes the Title, followed by a table of contents (depth per book, as in
   the original Word files).
3. If Word is installed, it opens the file once to fill in the table of
   contents (page numbers) and saves it, so it's ready to read.

Styles come from Pandoc's default template (Heading 1-6, Body Text, Table,
...). For a different look, pass a reference Word file whose styles to copy:
    python build_docx.py FF --reference my-styles.docx
"""
import os, sys, subprocess
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import assemble

HERE = os.path.dirname(os.path.abspath(__file__))
# The folder that holds the books (FF/, HH/, GMG/ ...): EEE_ROOT if set, else
# the current folder. Run the scripts from there:  cd EEE  then  python ...
ROOT = os.path.abspath(os.environ.get('EEE_ROOT') or os.getcwd())
PANDOC = os.path.expandvars(r'%LOCALAPPDATA%\Pandoc\pandoc.exe')
TOC_DEPTH = {'FF': 5, 'HH': 3, 'GMG': 3}       # as in the original Word files

UPDATE_TOC = r'''
$w = New-Object -ComObject Word.Application; $w.Visible = $false; $w.DisplayAlerts = 0
try {{
  $d = $w.Documents.Open("{path}", $false, $false, $false)
  foreach ($t in $d.TablesOfContents) {{ $t.Update() }}
  $d.Save(); $d.Close(0); "toc updated"
}} finally {{ $w.Quit(); [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($w) }}
'''


def build(book, reference=None):
    md = os.path.join(ROOT, '_work', book + '_assembled.md')
    assemble.main(book, md)
    title = next(l.strip() for l in open(md, encoding='utf-8') if l.strip())
    out_dir = os.path.join(ROOT, '_build'); os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, book + '.docx')
    cmd = [PANDOC, md, '-f', 'gfm', '-t', 'docx', '-o', out,
           '--lua-filter=' + os.path.join(HERE, '..', 'html_bits.lua'),
           '--resource-path=' + os.path.dirname(md),
           '--toc', '--toc-depth=%d' % TOC_DEPTH.get(book, 3),
           '-M', 'title=' + title, '-M', 'toc-title=Contents']
    if reference:
        cmd.append('--reference-doc=' + reference)
    subprocess.run(cmd, check=True)
    try:
        r = subprocess.run(['powershell', '-NoProfile', '-Command', UPDATE_TOC.format(path=out)],
                           capture_output=True, text=True, timeout=900)
        note = 'contents filled in by Word' if 'toc updated' in r.stdout else 'Word could not fill in the contents: ' + (r.stderr.strip()[:200] or r.stdout.strip()[:200])
    except Exception as e:
        note = 'Word not available (%s); Word will offer to fill in the contents when the file is opened' % e
    print('%s -> %s (%.1f MB); %s' % (book, out, os.path.getsize(out) / 1e6, note))


if __name__ == '__main__':
    args = sys.argv[1:]
    ref = None
    if '--reference' in args:
        i = args.index('--reference'); ref = args[i + 1]; del args[i:i + 2]
    for b in args:
        build(b.upper(), ref)
