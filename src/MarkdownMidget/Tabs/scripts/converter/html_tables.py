"""html_tables.py - turn the HTML tables Pandoc leaves in the Markdown into
pipe tables, which the editor (and every Markdown viewer) can handle.

Pandoc writes a table as HTML when a pipe table can't hold it: merged cells,
or cells with several paragraphs or a list. Here:
  - merged cells: the text goes in the first cell, the cells it covered stay empty
  - a first row that is one cell across the whole table (a title) goes above
    the table in bold
  - paragraphs, list items and headings in a cell are joined with <br>
  - bold, italic, <sup>/<sub>/<u> and bookmark anchors (<a id>) are kept

    python html_tables.py FILE.md ...      (rewrites the files in place)
docx2eee.py also calls convert() on every file it writes.
"""
import re, sys
from lxml import html as lhtml

TABLE = re.compile(r'<table\b.*?</table>', re.S)
BREAK = '<br>'
KEEP_TAGS = ('sup', 'sub', 'u')
BLOCKS = ('p', 'div', 'ul', 'ol', 'li', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'blockquote')


def _text(s):
    s = re.sub(r'\s+', ' ', s)
    return s.replace('\\', '\\\\').replace('|', '\\|').replace('*', '\\*').replace('<', '&lt;')


def _wrap(mark, inner):
    """**inner** with any spaces moved outside the marks; nothing for empty."""
    if not inner.strip():
        return inner
    lead = inner[:len(inner) - len(inner.lstrip())]
    trail = inner[len(inner.rstrip()):]
    return '%s%s%s%s%s' % (lead, mark, inner.strip(), mark, trail)


def _inline(el):
    """Markdown for an element's content, without its tail."""
    out = _text(el.text or '')
    for ch in el:
        out += _node(ch) + _text(ch.tail or '')
    return out


def _node(el):
    tag = el.tag if isinstance(el.tag, str) else ''
    inner = _inline(el)
    anchor = '<a id="%s"></a>' % el.get('id') if tag in ('a', 'span') and el.get('id') else ''
    if tag in ('strong', 'b'):
        return _wrap('**', inner)
    if tag in ('em', 'i'):
        return _wrap('*', inner)
    if tag in KEEP_TAGS:
        return '<%s>%s</%s>' % (tag, inner, tag) if inner.strip() else inner
    if tag == 'br':
        return BREAK
    if tag == 'a' and el.get('href'):
        return '[%s](%s)' % (inner, el.get('href'))
    if tag == 'img':
        return '![%s](%s)' % (el.get('alt', ''), el.get('src', ''))
    if tag == 'code':
        return '`%s`' % inner
    return anchor + inner


def _blocks(el):
    """The cell's content as a list of lines (one per paragraph / list item / heading)."""
    lines, cur = [], ''
    def flush():
        nonlocal cur
        if cur.strip():
            lines.append(cur.strip())
        cur = ''
    cur += _text(el.text or '')
    for ch in el:
        tag = ch.tag if isinstance(ch.tag, str) else ''
        if tag in BLOCKS:
            flush()
            if tag in ('ul', 'ol'):
                for k, li in enumerate(x for x in ch if x.tag == 'li'):
                    sub = ' '.join(_blocks(li))
                    if sub:
                        lines.append(('%d. ' % (k + 1) if tag == 'ol' else '• ') + sub)
            elif tag == 'li':
                lines.append('• ' + ' '.join(_blocks(ch)))
            elif tag.startswith('h'):
                h = ' '.join(_blocks(ch))
                # keep anchors outside the bold: <a id="x"></a>**Title**
                m = re.match(r'^((?:<a id="[^"]*"></a>)*)(.*)$', h)
                if m.group(1) or m.group(2):
                    lines.append(m.group(1) + _wrap('**', m.group(2)))
            else:
                lines.extend(_blocks(ch))
        else:
            cur += _node(ch)
        cur += _text(ch.tail or '')
    flush()
    return [l for l in lines if l]


def _cell(td):
    return BREAK.join(_blocks(td))


def table_to_pipe(src):
    t = lhtml.fragment_fromstring(src)
    trs = t.xpath('.//tr')
    head_rows = len(t.xpath('./thead/tr'))
    grid, taken = {}, set()
    for r, tr in enumerate(trs):
        c = 0
        for td in tr:
            if td.tag not in ('td', 'th'):
                continue
            while (r, c) in taken:
                c += 1
            cs = max(1, int(td.get('colspan', 1) or 1))
            rs = max(1, int(td.get('rowspan', 1) or 1))
            for dr in range(rs):
                for dc in range(cs):
                    taken.add((r + dr, c + dc))
            grid[(r, c)] = (_cell(td), cs)
            c += cs
    if not taken:
        return ''
    width = max(c for _, c in taken) + 1
    nrows = max(r for r, _ in taken) + 1
    rows = [[grid.get((r, c), ('', 1))[0] for c in range(width)] for r in range(nrows)]
    spans = [[grid.get((r, c), ('', 1))[1] for c in range(width)] for r in range(nrows)]

    above = []
    # Title rows: one cell across the whole table -> bold line above it
    while len(rows) > 2 and spans[0][0] == width and width > 1:
        if rows[0][0]:
            t0 = rows[0][0]
            above.append(t0 if t0.startswith('**') or re.match(r'^(<a id="[^"]*"></a>)*\*\*', t0) else _wrap('**', t0))
        rows.pop(0); spans.pop(0); head_rows = max(0, head_rows - 1)
    # Footnote rows: one cell across the whole table at the end -> lines below it
    below = []
    while len(rows) > 2 and spans[-1][0] == width and width > 1:
        if rows[-1][0]:
            below.insert(0, rows[-1][0])
        rows.pop(); spans.pop()
    rows = [r for r in rows if any(r)] or rows
    # columns empty all the way down go
    keep = [c for c in range(width) if any(r[c] for r in rows)] or [0]
    rows = [[r[c] for c in keep] for r in rows]
    if len(keep) == 1:
        # one column: not a table, just its lines
        return '\n\n'.join(above + [r[0].replace(BREAK, '\n\n') for r in rows if r[0]] + below)
    head = [c.replace('**', '') for c in rows[0]]       # header cells are bold anyway
    out = above + ([''] if above else [])
    out.append('| ' + ' | '.join(head) + ' |')
    out.append('|' + '---|' * len(keep))
    out.extend('| ' + ' | '.join(r) + ' |' for r in rows[1:])
    for b in below:
        out += ['', b.replace(BREAK, '\n\n')]
    return '\n'.join(out)


def convert(md):
    """Every HTML table in md as a pipe table (with blank lines around it)."""
    def one(m):
        return '\n\n' + table_to_pipe(m.group(0)) + '\n\n'
    out = TABLE.sub(one, md)
    if out != md:
        out = re.sub(r'\n{3,}', '\n\n', out).strip('\n') + '\n'
    return out


if __name__ == '__main__':
    for f in sys.argv[1:]:
        s = open(f, encoding='utf-8').read()
        n = convert(s)
        if n != s:
            open(f, 'w', encoding='utf-8', newline='\n').write(n)
            print('converted', f)
