"""check_links.py - check every internal link in EEE/<BOOK>.

    python check_links.py FF HH GMG

A link is good when its file exists and holds <a id="<anchor>"></a>.
Prints totals per book and the broken links (first 20 per book)."""
import os, re, sys, glob, collections

# The folder that holds the books (FF/, HH/, GMG/ ...): EEE_ROOT if set, else
# the current folder. Run the scripts from there:  cd EEE  then  python ...
ROOT = os.path.abspath(os.environ.get('EEE_ROOT') or os.getcwd())
LINK = re.compile(r'\]\(([^()\s#]*)#((?:[^()\s]|\([^()\s]*\))+)\)')
ANCHOR = re.compile(r'<a id="([^"]+)"></a>')

for book in sys.argv[1:]:
    base = os.path.join(ROOT, book)
    anchors = {}
    files = glob.glob(os.path.join(base, '**', '*.md'), recursive=True)
    for f in files:
        anchors[os.path.normcase(os.path.abspath(f))] = set(ANCHOR.findall(open(f, encoding='utf-8').read()))
    good, bad = 0, []
    for f in files:
        for i, line in enumerate(open(f, encoding='utf-8').read().split('\n'), 1):
            for path, frag in LINK.findall(line):
                if '://' in path:
                    continue        # web link, not ours to check
                tgt = os.path.normcase(os.path.abspath(os.path.join(os.path.dirname(f), path) if path else f))
                if frag in anchors.get(tgt, ()):
                    good += 1
                else:
                    bad.append((os.path.relpath(f, base), i, path + '#' + frag))
    # index entries: [name](file.md) or [name/](name/000000-index.md) must exist
    for f in files:
        if os.path.basename(f) == '000000-index.md':
            for t in re.findall(r'^- \[[^\]]*\]\(([^)]+)\)', open(f, encoding='utf-8').read(), re.M):
                if os.path.exists(os.path.join(os.path.dirname(f), t)):
                    good += 1
                else:
                    bad.append((os.path.relpath(f, base), 0, t))
    print('%s: %d files, %d links good, %d broken' % (book, len(files), good, len(bad)))
    for kind, n in collections.Counter('missing file' if not os.path.exists(os.path.join(base, os.path.dirname(f), p.split('#')[0])) and p.split('#')[0] else 'missing anchor' for f, _, p in bad).items():
        print('   ', n, kind)
    for f, i, p in bad[:20]:
        print('    %s:%d -> %s' % (f, i, p))
