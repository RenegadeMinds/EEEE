"""assemble.py - put a book back together in order.

    python assemble.py FF  [out.md]

Starts at EEE/<BOOK>/000000-index.md and follows it: each file in turn, and
for each folder, that folder's own 000000-index.md. Writes one Markdown file
(default: _work/<BOOK>_assembled.md) that Pandoc can turn into
DOCX, PDF, HTML, ...

Links between files (aboleth.md#ff.aboleth) become #ff.aboleth, since it's
all one file now."""
import os, re, sys

# The folder that holds the books (FF/, HH/, GMG/ ...): EEE_ROOT if set, else
# the current folder. Run the scripts from there:  cd EEE  then  python ...
ROOT = os.path.abspath(os.environ.get('EEE_ROOT') or os.getcwd())
INDEX = '000000-index.md'
ENTRY = re.compile(r'^- \[[^\]]*\]\(([^)]+)\)\s*$')


def files_in_order(folder):
    for line in open(os.path.join(folder, INDEX), encoding='utf-8'):
        m = ENTRY.match(line)
        if not m:
            continue
        target = os.path.join(folder, m.group(1))
        if m.group(1).endswith('/' + INDEX):
            yield from files_in_order(os.path.dirname(target))
        else:
            yield target


def main(book, dest=None):
    base = os.path.join(ROOT, book)
    dest = dest or os.path.join(ROOT, '_work', book + '_assembled.md')
    parts = []
    files = list(files_in_order(base))
    for f in files:
        txt = open(f, encoding='utf-8').read()
        txt = re.sub(r'\]\([^)\s#]+\.md#', '](#', txt)      # cross-file links -> same-file
        # images: path relative to this file -> path relative to the output
        txt = re.sub(r'(!\[[^\]]*\]\()([^)\s:]+)\)',
                     lambda m: m.group(1) + os.path.relpath(os.path.join(os.path.dirname(f), m.group(2)),
                                                            os.path.dirname(dest)).replace('\\', '/') + ')', txt)
        parts.append(txt.strip())
    open(dest, 'w', encoding='utf-8', newline='\n').write('\n\n'.join(parts) + '\n')
    on_disk = [os.path.join(d, f) for d, _, fs in os.walk(base) for f in fs
               if f.endswith('.md') and f != INDEX]
    missing = set(map(os.path.normcase, on_disk)) - set(map(os.path.normcase, files))
    print('%s: %d files assembled -> %s%s' % (book, len(files), dest,
          '' if not missing else '; NOT in any index: %d' % len(missing)))
    for m in sorted(missing)[:10]:
        print('   ', os.path.relpath(m, base))


if __name__ == '__main__':
    main(sys.argv[1].upper(), sys.argv[2] if len(sys.argv) > 2 else None)
