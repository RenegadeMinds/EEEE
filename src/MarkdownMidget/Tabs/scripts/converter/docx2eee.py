"""docx2eee.py - turn one E&E Word master into a folder of Markdown files.

    python docx2eee.py "<book.docx>" FF

1. Pandoc converts the .docx to one GitHub-flavoured Markdown file (renders
   the same in VS Code, GitHub and Pandoc). Hand-set fonts, sizes and
   colours are dropped. tables.lua turns most tables into pipe tables.
2. The file is cut at every heading:
     chapter heading  -> folder   0010-chapter-1-creatures/
     section heading  -> folder   0010-creature-types/
     anything lower   -> file     000100-aberration.md, 000200-... (step 100)
   Text between a chapter/section heading and its first child goes in
   000050-<name>.md inside that folder. A folder that would hold just one
   file becomes that file instead (0010-creature-types.md).
   Every folder gets 000000-index.md: its files and folders in book order,
   one link per line. assemble.py follows the indexes to rebuild the book.
   Alphabetical sections (BOOKS[..]['alpha']: FF monsters, HH feats and
   spells) get a folder per first letter instead of numbers:
   A/aboleth.md, sub-entries as A/angel-astral-deva.md.
3. Every heading gets a permanent anchor as plain HTML, which every
   Markdown viewer understands:
       #### ABOLETH <a id="ff.aboleth"></a>
       ##### COMBAT <a id="ff.aboleth.combat"></a>     (heading inside a file)
       <a id="ff.table-aberrations"></a>**Table: Aberrations**
   Whole-line bold text ("**COMBAT**") becomes a real heading one level
   below the file's own heading. Links point at <file>#ff.aboleth. Word's
   own bookmarks (_Toc123...) are removed.
4. Things to fix by hand later (straight quotes, HTML tables, ...) are
   listed in EEE/_anomalies/<BOOK>.md with links to the exact line.

Output: EEE/<BOOK>/ (emptied and rebuilt on every run).
"""
import os, re, sys, glob, shutil, subprocess, collections
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import html_tables

BOOKS = {
    # chapter = heading level of chapters, section = level of sections.
    # alpha = alphabetical sections whose entries go in A/, B/, ... folders:
    #   (regex for the section heading, heading level of one entry,
    #    folder name if several Word sections merge into one, else None).
    #   Headings below the entry level are sub-entries: A/angel-astral-deva.md.
    'FF': dict(chapter=2, section=3, alpha=[(r'^MONSTERS\b', 4, 'monsters')]),
    # join = section headings that continue the previous section instead of
    #   starting a new folder.
    'HH': dict(chapter=1, section=2, alpha=[(r'^Feat Descriptions$', 6, None),
                                            (r'^List of Spells', 6, None)],
               join=[r'^List of Spells']),
    'GMG': dict(chapter=1, section=2, alpha=[]),
}
PANDOC = shutil.which('pandoc') or os.path.expandvars(r'%LOCALAPPDATA%\Pandoc\pandoc.exe')
ROMAN = {'I': 1, 'II': 2, 'III': 3, 'IV': 4, 'V': 5, 'VI': 6, 'VII': 7, 'VIII': 8, 'IX': 9,
         'X': 10, 'XI': 11, 'XII': 12, 'XIII': 13, 'XIV': 14, 'XV': 15}


BOOKMARK = re.compile(r'<span id="[^"]*" class="anchor"></span>|\[\]\{#[^}]*\}')   # Word bookmarks


def clean_heading(t):
    t = BOOKMARK.sub('', t)
    t = re.sub(r'\{#[^}]*\}\s*$', '', t)            # Pandoc attributes
    t = t.replace('**', '').replace('\\', '')
    t = re.sub(r'(?<!\w)\*|\*(?!\w)', '', t)
    return re.sub(r'\s+', ' ', t).strip()


def slug(t):
    t = re.sub(r'<[^>]+>|@@LK:.*?@@|@@BM:.*?@@', '', t)   # <sup>ST</sup> -> ST; markers out
    t = re.sub(r'\]\([^)]*\)', '', t)                     # [text](link) -> text
    t = re.sub(r'^chapter ([ivxl]+)\b', lambda m: 'chapter ' + str(ROMAN.get(m.group(1).upper(), m.group(1))), t, flags=re.I)
    t = t.lower().replace('&', ' and ')
    t = re.sub(r"['’‘\"“”]", '', t)
    return re.sub(r'[^a-z0-9]+', '-', t).strip('-') or 'untitled'


def pandoc_id(t):
    """The id Pandoc gives a heading (its links in the .md point at these)."""
    t = re.sub(r'<[^>]+>', '', t)
    t = re.sub(r'\]\([^)]*\)', '', t)                # a link counts by its text only
    t = re.sub(r'[^\w\s.-]', '', t.lower())
    t = re.sub(r'\s+', '-', t.strip())
    return re.sub(r'^[^a-z]+', '', t)


def merge_slug(parent, child):
    """angel + angel-astral-deva -> angel-astral-deva; animal-dire + dire-ape -> animal-dire-ape."""
    if child.startswith(parent + '-') or child == parent:
        return child
    p, c = parent.split('-'), child.split('-')
    for k in range(min(len(p), len(c)), 0, -1):
        if p[-k:] == c[:k]:
            return '-'.join(p + c[k:])
    return parent + '-' + child


MARK = re.compile(r'@@BM:(.*?)@@')     # bookmark marker put in by mark_bookmarks()
LINK_MARK = re.compile(r'\[@@LK:(.*?)@@((?:[^\[\]]|\[[^\[\]]*\])*)\]\((#(?:[^()\s]|\([^()\s]*\))*)\)')   # [@@LK:bm@@text](#pandoc-id)
LINK_MARK_OUTSIDE = re.compile(r'@@LK:(.*?)@@\[((?:[^\[\]]|\[[^\[\]]*\])*)\]\((#(?:[^()\s]|\([^()\s]*\))*)\)')  # @@LK:bm@@[text](#id)


def mark_bookmarks(docx, dest):
    """Pandoc drops Word bookmarks. Copy the .docx to dest with a text marker
    @@BM:<name>@@ at the start of the paragraph each bookmark is in (or the
    next paragraph, if it sits between paragraphs). The markers survive
    Pandoc and become <a id="<name>"></a>. Returns [(name, para text)]."""
    import zipfile
    from lxml import etree
    W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
    q = lambda t: '{%s}%s' % (W, t)
    zin = zipfile.ZipFile(docx)
    doc = etree.fromstring(zin.read('word/document.xml'))
    found = []
    for bm in list(doc.iter(q('bookmarkStart'))):
        name = bm.get(q('name'))
        if not name or name == '_GoBack':
            continue
        p = next(bm.iterancestors(q('p')), None)
        if p is None:
            p = next(iter(bm.xpath('following::w:p[1]', namespaces={'w': W})), None)
        if p is None:
            continue
        r = etree.Element(q('r')); t = etree.SubElement(r, q('t'))
        t.text = '@@BM:%s@@' % name
        t.set('{http://www.w3.org/XML/1998/namespace}space', 'preserve')
        ppr = p.find(q('pPr'))
        p.insert(list(p).index(ppr) + 1 if ppr is not None else 0, r)
        found.append((name, MARK.sub('', ''.join(x.text or '' for x in p.iter(q('t')) if x is not t))[:60]))
    # Links to bookmarks: put @@LK:<bookmark>@@ at the start of the link text
    # so the link can be pointed at the right file later (Pandoc rewrites
    # these targets into ids that often don't exist).
    for h in doc.iter(q('hyperlink')):
        target = h.get(q('anchor'))
        if not target:
            continue
        r = etree.Element(q('r')); t = etree.SubElement(r, q('t'))
        t.text = '@@LK:%s@@' % target
        t.set('{http://www.w3.org/XML/1998/namespace}space', 'preserve')
        h.insert(0, r)
    # Tabs typed in the text (not tab-stop settings): Pandoc turns them into
    # spaces, which wrecks the many "tables" laid out with tabs. Mark each
    # one; tab_tables() turns runs of them into real tables.
    for tab in list(doc.iter(q('tab'))):
        parent = tab.getparent()
        if parent is not None and parent.tag == q('r'):
            t = etree.Element(q('t')); t.text = TAB
            t.set('{http://www.w3.org/XML/1998/namespace}space', 'preserve')
            parent.replace(tab, t)
    with zipfile.ZipFile(dest, 'w', zipfile.ZIP_DEFLATED) as zout:
        for item in zin.infolist():
            data = etree.tostring(doc, xml_declaration=True, encoding='UTF-8', standalone=True) \
                if item.filename == 'word/document.xml' else zin.read(item.filename)
            zout.writestr(item, data)
    return found


def bm_name(s):
    return re.sub(r'\\(.)', r'\1', s)     # Pandoc may escape _ ( ) in the name


TAB = '@@TAB@@'          # a Word tab, put in by mark_bookmarks()
TAB_RUN = re.compile(r'(?:[ \t]*@@TAB@@[ \t]*)+')
# Two-cell runs ("Task<tab>DC", "1-5<tab>1d6") are tables too; with
# --no-two-col-tables they stay as lines and are listed in _anomalies.
TWO_COL_TABLES = '--no-two-col-tables' not in sys.argv


def tab_tables(md, two_col_runs):
    """Word "tables" made of tab-separated paragraphs -> Markdown pipe tables.
    Two or more paragraphs in a row with tabs in them make a table; each tab
    (or run of tabs) separates two cells; the first row is the header. Every
    other tab becomes a space, as Pandoc would have made it."""
    lines = md.split('\n')
    is_row = lambda l: TAB in l and not l.lstrip().startswith(('|', '#', '<'))

    def fix_emphasis(c):
        # **bold** split across cells: close or open it in this cell
        if c.count('**') % 2:
            c = c + '**' if c.startswith('**') else '**' + c
        if c.replace('**', '').count('*') % 2:
            c = c + '*' if c.replace('**', '').startswith('*') else '*' + c
        return c

    def cells(l):
        l = l.rstrip()
        if l.endswith('\\'):                  # line break inside one paragraph
            l = l[:-1].rstrip()
        marks = ''.join('@@BM:%s@@' % m for m in MARK.findall(l))
        l = MARK.sub('', l)
        cs = [c.strip() for c in TAB_RUN.split(l)]
        while cs and not cs[-1]:
            cs.pop()
        cs = ['' if re.fullmatch(r'\**', c) else c for c in cs]   # "****": bold around just a tab
        while cs and not cs[-1]:
            cs.pop()
        cs = [fix_emphasis(c).replace('|', '\\|') for c in cs] or ['']
        if marks:                             # bookmarks go in the first cell with text
            k = next((i for i, c in enumerate(cs) if c), 0)
            cs[k] = marks + cs[k]
        return cs

    out, i, n, made = [], 0, len(lines), [0]

    def make_table(group):
        """Lines of a pipe table for these tab-separated lines, or None if they aren't one."""
        rows = [r for r in (cells(l) for l in group) if any(r)]    # a line of nothing but tabs: no row
        if not rows:
            return None
        width = max(len(r) for r in rows)
        # Not a table: one line; indented paragraphs (every line starts with a
        # tab); Word's own table of contents.
        if len(rows) < 2 or width < 2 or all(not r[0] for r in rows) or re.search(r'@@LK:\\?_Toc', group[0]):
            return None
        if width == 2 and not TWO_COL_TABLES:
            two_col_runs.append(' | '.join(next(r for r in rows if r[0])))
            return None
        rows = [r + [''] * (width - len(r)) for r in rows]
        # A line wrapped by hand inside a cell ("Nature's" / <tab>"Secrets +1"):
        # a row starting empty with only a bit of text joins the row above, in
        # its longest cell (the description column, almost always).
        joined = [rows[0]]
        for r in rows[1:]:
            text = [c for c in r if c]
            if not r[0] and 0 < len(text) <= 2 and len(joined) > 1:
                prev = joined[-1]
                k = max(range(width), key=lambda x: len(prev[x]))
                prev[k] = (prev[k] + ' ' + ' '.join(text)).strip()
            else:
                joined.append(r)
        rows = joined
        keep = [c for c in range(width) if any(r[c] for r in rows)]     # columns empty all the way down go
        rows = [[r[c] for c in keep] for r in rows]
        if len(keep) < 2 or len(rows) < 2:
            return None
        width = len(keep)
        # The first row is the header. In a two-column table only when it's in
        # bold ("**Task** | **DC**"); otherwise ("1-5 | 1d6", "Chameleon: | Gain
        # +10 ...") every line is data and the header row is left empty.
        bold = lambda c: not c or re.fullmatch(r'\*\*.*\*\*', MARK.sub('', c)) is not None
        if width == 2 and not all(bold(c) for c in rows[0]):
            rows.insert(0, [''] * width)
        head = [re.sub(r'^((?:@@BM:.*?@@)*)\*\*(.*)\*\*$', r'\1\2', c) for c in rows[0]]   # header cells are bold anyway
        table = [''] if out and out[-1].strip() else []
        table.append('| ' + ' | '.join(head) + ' |')
        table.append('|' + '---|' * width)
        table.extend('| ' + ' | '.join(r) + ' |' for r in rows[1:])
        table.append('')
        made[0] += 1
        return table

    while i < n:
        if not is_row(lines[i]):
            out.append(lines[i]); i += 1
            continue
        j, group = i, []
        while j < n:
            if is_row(lines[j]):
                group.append(lines[j]); j += 1
            elif not lines[j].strip() and j + 1 < n and is_row(lines[j + 1]):
                j += 1
            else:
                break
        # Indented paragraphs (a tab, then one text) just before a table, or
        # long ones just after it, aren't part of it: they stay paragraphs.
        # (Short ones after it are a cell's wrapped last line.)
        lone = lambda r: not r[0] and sum(1 for c in r if c) <= 1
        rows = [cells(l) for l in group]
        a, b = 0, len(rows)
        while a < b and lone(rows[a]):
            a += 1
        while b > a and lone(rows[b - 1]) and (len(''.join(rows[b - 1])) > 80 or not any(rows[b - 1])):
            b -= 1
        if 0 < b - a < len(rows):
            for g in group[:a]:
                out.extend([g, ''])
            table = make_table(group[a:b])
            if table:
                out.extend(table)
            else:
                for g in group[a:b]:
                    out.extend([g, ''])
            for g in group[b:]:
                out.extend([g, ''])
        else:
            table = make_table(group)
            if not table:
                out.extend(lines[i:j]); i = j
                continue
            out.extend(table)
        i = j
        while i < n and not lines[i].strip():   # the blank line after is already there
            i += 1
    md = '\n'.join(out)
    return finish_tabs(md), made[0]

def finish_tabs(md):
    # Any other tab: a space, trimmed the way Pandoc trims spaces (none at the
    # start or end of a line, none just inside ** or *).
    # A line that starts with "- " or "1. " after its tab is text, not a list (Pandoc escapes it).
    md = re.sub(r'^(?:@@TAB@@[ \t]*)+([-+]|\d+\.)(?= )', r'@@TAB@@\\\1', md, flags=re.M)
    md = re.sub(r'^([-+]|\d+\.)[ \t]*@@TAB@@', r'\\\1@@TAB@@', md, flags=re.M)   # "-<tab>text"
    md = re.sub(r'^((?:\*\*|\*)*)(?:@@TAB@@[ \t]*)+', r'\1', md, flags=re.M)
    md = re.sub(r'(?:[ \t]*@@TAB@@[ \t]*)+((?:\*\*|\*)*)[ \t]*$', r'\1', md, flags=re.M)
    md = re.sub(r'(?<=\s)(\*\*|\*)(?:@@TAB@@[ \t]*)+', r'\1', md)

    def before_closer(m):
        # tab then * or **: if that closes emphasis, the space goes after it ("lesser* •")
        line_start = md.rfind('\n', 0, m.start()) + 1
        before = md[line_start:m.start()]
        d = m.group(1)
        opened = before.count('**') % 2 if d == '**' else before.replace('**', '').count('*') % 2
        return d + ' ' if opened else m.group(0)
    md = re.sub(r'(?:[ \t]*@@TAB@@[ \t]*)+(\*\*|\*)(?!\*)', before_closer, md)
    md = re.sub(r'^\*\*\*\*|(?<!\*)\*\*\*\*(?!\*)', '', md, flags=re.M)      # bold around nothing but a tab
    md = TAB_RUN.sub(' ', md)
    return md


def main(docx, book):
    cfg = BOOKS[book]
    here = os.path.dirname(os.path.abspath(__file__))
    # The books folder: EEE_ROOT if set, else the current folder (run it from EEE).
    # EEE_OUT_ROOT: write the book (and its _anomalies) somewhere else, to compare first.
    base = os.path.abspath(os.environ.get('EEE_ROOT') or os.getcwd())
    root = os.environ.get('EEE_OUT_ROOT') or base
    out = os.path.join(root, book)
    work = os.path.join(base, '_work'); os.makedirs(work, exist_ok=True)
    md_path = os.path.join(work, book + '.md')
    # GitHub-flavoured Markdown: renders the same in VS Code, GitHub and
    # Pandoc. Tables are pipe tables (| a | b |); a table with merged cells
    # inside it can't be one and stays as HTML. Superscript -> <sup>.
    marked = os.path.join(work, book + '_marked.docx')
    try:
        open(marked, 'ab').close()
    except PermissionError:      # the old copy is locked (preview pane, virus scan ...)
        marked = os.path.join(work, '%s_marked_%d.docx' % (book, os.getpid()))
    bookmarks = mark_bookmarks(docx, marked)
    subprocess.run([PANDOC, marked, '-t', 'gfm', '--wrap=none',
                    '--lua-filter=' + os.path.join(here, 'tables.lua'),
                    '--extract-media=' + os.path.join(work, book + '_media'), '-o', md_path], check=True)
    # The marked copy is only for Pandoc; delete it so nobody opens it by
    # mistake (it's full of @@BM: / @@LK: markers).
    try:
        os.remove(marked)
    except OSError:
        pass
    md = open(md_path, encoding='utf-8').read()
    # A heading that starts with a line break comes out as "#### \" with its
    # text on the next line: join them.
    md = re.sub(r'^(#{1,6}) ((?:@@BM:.*?@@)*)\\\n', r'\1 \2', md, flags=re.M)
    two_col_runs = []
    md, n_tab_tables = tab_tables(md, two_col_runs)
    lines = md.split('\n')

    # ---- pass 1: cut into pieces, each with a target path ----
    pieces = []          # dict(path, heading_lines, body)
    used = collections.Counter()
    old2new = {}         # Pandoc's auto ids -> our anchors (for links)
    ch_n = sec_n = 0
    chapter_dir = section_dir = None
    file_n = 0
    in_monsters = False
    monsters_dir = None
    parent_monster = None   # (slug, letter) of the current top-level entry in an A-Z section
    entry_level = 0
    cur = dict(path=os.path.join('0000-front-matter', '000000-front-matter.md'), body=[])
    pieces.append(cur)

    def anchor(s, parent=None):
        a = s if not used[s] else (merge_slug(parent, s) if parent else s)
        while used[a]:
            a = '%s-%d' % (s, used[s] + 1)
        used[s] += 1; used[a] += 1
        return book.lower() + '.' + a

    def sub_anchor(fstem, s):
        """Anchor for a heading inside a file: ff.aboleth.combat."""
        base = '%s.%s.%s' % (book.lower(), fstem, s)
        a, n = base, 1
        while used[a]:
            n += 1; a = '%s-%d' % (base, n)
        used[a] += 1
        return a

    HEAD = re.compile(r'^(#{1,6}) (.*)$')
    TABLE_TITLE = re.compile(r'^\*\*((?:0B)*)(Table: .+?)\*\*\s*$')   # 0B0B... = stray junk in one FF title
    # A whole line in bold = a heading Word didn't mark as one ("**COMBAT**").
    BOLD_LINE = re.compile(r'^\*\*([^*]{2,80}?)\*\*\s*$')
    NOT_HEADING = re.compile(r'=|^(Fine|Diminutive|Tiny|Small|Medium|Large|Huge|Gargantuan|Colossal|Dragon)\b.*\)$')
    cur['level'] = 0
    stripped_junk = []
    unresolved_links = []
    entry_levels = {}       # A-Z section folder -> heading level of its entries
    guessed_links = []
    def bm_anchor(m):
        return '<a id="%s"></a>' % bm_name(m.group(1))

    for ln in lines:
        if cur.get('toc') and not HEAD.match(MARK.sub('', ln)):
            continue        # inside Word's table of contents
        # Word bookmarks: on a heading / table title they go after the
        # heading's own anchor; anywhere else they stay where they were.
        raw = ln
        bare = MARK.sub('', ln)
        bms = ''.join('<a id="%s"></a>' % bm_name(x) for x in MARK.findall(ln))
        m = HEAD.match(bare)
        if not m:
            fstem = re.sub(r'^\d{6}-', '', os.path.splitext(os.path.basename(cur['path']))[0])
            t = TABLE_TITLE.match(bare)
            b = BOLD_LINE.match(bare)
            if t:   # table titles get anchors: <a id="ff.table-x"></a>**Table: X**
                if t.group(1):
                    stripped_junk.append((cur['path'], t.group(1) + t.group(2)))
                title = clean_heading(t.group(2))
                a = anchor(slug(title), fstem)
                old2new.setdefault('#' + pandoc_id(bare.strip('*')), (cur['path'], a))
                old2new.setdefault('#' + pandoc_id(bm_name(raw).strip('*')), (cur['path'], a))
                ln = '<a id="%s"></a>%s**%s**' % (a, bms, title)
            elif b and not NOT_HEADING.search(b.group(1)) and \
                    not (b.group(1).endswith(':') and b.group(1) != b.group(1).upper()):
                text = clean_heading(b.group(1)).rstrip(':').strip()
                lvl = min(cur['level'] + 1, 6) if cur['level'] else 2
                a = sub_anchor(fstem, slug(text))
                old2new.setdefault('#' + pandoc_id(bm_name(raw).strip('*')), (cur['path'], a))
                ln = '%s %s <a id="%s"></a>%s' % ('#' * lvl, text, a, bms)
            else:
                ln = MARK.sub(bm_anchor, ln)
            cur['body'].append(ln)
            continue
        level, text = len(m.group(1)), clean_heading(m.group(2))
        jm = re.match(r'^((?:0B)+)(Table: .*)$', text)      # stray "0B0B..." in one FF title
        if jm:
            stripped_junk.append((cur['path'], text))
            text = jm.group(2)
        if not text:
            cur['body'].append(MARK.sub(bm_anchor, ln)); continue
        s = slug(text)
        group_base = entry_at = None
        if level <= cfg['chapter'] and s in ('contents', 'table-of-contents'):
            # Word's table of contents: dropped (the folders are the contents).
            cur = dict(path=os.path.join('0000-front-matter', '000000-front-matter.md'), level=0, body=[], toc=True)
            pieces.append(cur)
            continue
        if level <= cfg['chapter']:
            # Chapter N -> N*10; anything before chapter 1 (Introduction) -> 0005;
            # anything after (Appendix) -> next free number.
            cm = re.match(r'chapter-(\d+)', s)
            ch_n = int(cm.group(1)) * 10 if cm else (5 if ch_n < 10 else ch_n + 10)
            chapter_dir = '%04d-%s' % (ch_n, s)
            section_dir = None; sec_n = 0; file_n = 0
            in_monsters = False; monsters_dir = None
            path = os.path.join(chapter_dir, '000000-%s.md' % s)
            parent = None
        elif level == cfg['section']:
            al = next((x for x in cfg['alpha'] if re.search(x[0], text)), None)
            if section_dir and any(re.search(j, text) for j in cfg.get('join', [])):
                # a section heading that continues the previous section
                # (HH "List of Spells / Powers..." under "Spell and Power Descriptions")
                path = os.path.join(section_dir, '000000-%s.md' % os.path.basename(section_dir)[5:])
                if al:
                    in_monsters, entry_level = True, al[1]
            elif al:        # alphabetical section: entries go in A/, B/, ... folders
                _, entry_level, merge = al
                if merge:   # several Word sections -> one folder (FF "MONSTERS (A)", "(B)", ...)
                    if not monsters_dir:
                        sec_n += 10
                        monsters_dir = os.path.join(chapter_dir, '%04d-%s' % (sec_n, merge))
                    section_dir = monsters_dir
                    group_base = section_dir      # "MONSTERS (B)" is a divider, placed below
                else:
                    sec_n += 10
                    section_dir = os.path.join(chapter_dir, '%04d-%s' % (sec_n, s))
                in_monsters = True
                path = os.path.join(section_dir, '000000-%s.md' % os.path.basename(section_dir)[5:])
            else:
                in_monsters = False
                sec_n += 10
                section_dir = os.path.join(chapter_dir, '%04d-%s' % (sec_n, s))
                path = os.path.join(section_dir, '000000-%s.md' % s)
            file_n = 0
            parent = None
        else:
            base = section_dir or chapter_dir or '0000-front-matter'
            if in_monsters and level < entry_level:
                # divider above the entries ("Feats: B", "Spells & Powers: C"):
                # placed below, in the letter folder of the entry that follows it
                path = os.path.join(base, '000000-%s.md' % os.path.basename(base)[5:])
                parent = os.path.basename(base)[5:]
                group_base = base
            elif in_monsters:
                if level == entry_level:
                    letter = (re.search(r'[a-z]', s) or re.search('.', s)).group(0).upper()
                    parent_monster = (s, letter)
                    name = s
                else:
                    ps, letter = parent_monster or (s, s[0].upper())
                    name = merge_slug(ps, s)
                path = os.path.join(base, letter, name + '.md')
                parent = parent_monster[0] if parent_monster else None
                entry_at = (base, letter)
                entry_levels[base] = entry_level
            else:
                file_n += 100
                path = os.path.join(base, '%06d-%s.md' % (file_n, s))
                parent = os.path.basename(base)[5:] if base else None
        a = anchor(s, parent)
        old2new.setdefault('#' + pandoc_id(text), (path, a))
        old2new.setdefault('#' + pandoc_id(bm_name(HEAD.match(raw).group(2))), (path, a))   # Pandoc's id includes the marker text
        cur = dict(path=path, level=level, body=['%s %s <a id="%s"></a>%s' % ('#' * level, text, a, bms)],
                   group=group_base, entry=entry_at, slug=s)
        pieces.append(cur)

    # Dividers in A-Z sections ("Feats: B", "MONSTERS (E-F)") go at the top of
    # the letter folder of the entry that follows: B/000000-feats-b.md (renamed
    # 000050 below), so the book reassembles in its original order. A divider
    # with its own text before the section's first entry stays in the intro.
    out_of_order, top = [], {}
    for pc in pieces:       # entries the Word file has out of A-Z order (the letter folders fix it)
        if pc.get('entry') and pc['level'] == entry_levels.get(pc['entry'][0]):
            base, letter = pc['entry']
            if letter < top.get(base, ''):
                out_of_order.append((pc['path'], pc['body'][0].split(' <a ')[0].lstrip('# ')))
            top[base] = max(top.get(base, ''), letter)
    # ...and move each one (with its sub-entries) to its alphabetical place,
    # just before the first entry of its section that sorts after it.
    is_top = lambda q, base: q.get('entry') and q['entry'][0] == base and q['level'] == entry_levels[base]
    for path, _ in out_of_order:
        i = next(k for k, q in enumerate(pieces) if q['path'] == path)
        base, s = pieces[i]['entry'][0], pieces[i]['slug']
        j = i + 1
        while j < len(pieces) and pieces[j].get('entry') and pieces[j]['entry'][0] == base and not is_top(pieces[j], base):
            j += 1
        block = pieces[i:j]; del pieces[i:j]
        k = next((k for k, q in enumerate(pieces) if is_top(q, base) and q['slug'] > s), len(pieces))
        pieces[k:k] = block
    moved = {}
    for i, pc in enumerate(pieces):
        if not pc.get('group'):
            continue
        base = pc['group']
        seen = any(q.get('entry') and q['entry'][0] == base for q in pieces[:i])
        has_text = any(l.strip() for l in pc['body'][1:])
        nxt = next((q for q in pieces[i + 1:] if q.get('entry') and q['entry'][0] == base), None)
        if nxt is None or (not seen and has_text):
            continue
        key = id(nxt)
        moved.setdefault(key, os.path.join(base, nxt['entry'][1], '000000-%s.md' % pc['slug']))
        old = pc['path']; pc['path'] = moved[key]
        for k, v in old2new.items():
            if v[0] == old and v[1] in ''.join(pc['body'][:1]):
                old2new[k] = (pc['path'], v[1])

    # ---- pass 2: tidy and write ----
    # Empty the book folder (not the folder itself: an open Explorer window
    # or shell inside it would lock it).
    os.makedirs(out, exist_ok=True)
    for d, subs, files in os.walk(out, topdown=False):
        for f in files:
            os.remove(os.path.join(d, f))
        if d != out:
            try:
                os.rmdir(d)
            except OSError:
                pass        # folder open somewhere; left empty, refilled below
    toc_line = re.compile(r'^\[.*\]\(#[^)]*\)\s*$')
    link = re.compile(r'\]\((#(?:[^()\s]|\([^()\s]*\))+)\)')       # allows #_Wisdom_(Wis)
    merged = collections.OrderedDict()
    for p in pieces:
        merged.setdefault(p['path'], []).extend(p['body'] + [''])
    nfiles = 0
    found = collections.defaultdict(list)
    img_dir = os.path.join(out, 'img')
    media_src = re.compile(r'\]\([^)\n]*?_media[\\/]media[\\/]([^)\s]+)\)')
    # 1. text of every file (empty ones dropped)
    raw = collections.OrderedDict()
    for path, body in merged.items():
        txt = '\n'.join(l for l in body if not (path.startswith('0000-front') and toc_line.match(l)))
        txt = BOOKMARK.sub('', txt)
        txt = re.sub(r'\n{3,}', '\n\n', txt).strip() + '\n'
        if MARK.sub('', LINK_MARK.sub('', txt)).strip():
            raw[path] = txt
    # 2. final paths: intros 000000-x.md -> 000050-x.md (000000 is the index),
    #    then a folder holding just one file becomes that file, one level up
    #    (0010-creature-types/000050-creature-types.md -> 0010-creature-types.md).
    #    A-Z letter folders always stay.
    pathmap = {p: os.path.join(os.path.dirname(p), re.sub(r'^000000-', '000050-', os.path.basename(p)))
               for p in raw}
    while True:
        kids = collections.defaultdict(set)
        for p in pathmap.values():
            parts = p.split(os.sep)
            for i in range(1, len(parts)):
                kids[os.sep.join(parts[:i])].add(parts[i])
        single = {d for d, k in kids.items()
                  if len(k) == 1 and next(iter(k)).endswith('.md') and not re.fullmatch(r'[A-Z]', os.path.basename(d))}
        if not single:
            break
        for p, q in pathmap.items():
            if os.path.dirname(q) in single:
                pathmap[p] = os.path.dirname(q) + '.md'
    texts = collections.OrderedDict()
    where = {}          # anchor/bookmark id -> (file, the book anchor on the same line or None)
    for p0, txt in raw.items():
        path = pathmap[p0]
        rel_img = os.path.relpath(img_dir, os.path.join(out, os.path.dirname(path) or '.')).replace('\\', '/')
        txt = media_src.sub(lambda m: '](%s/%s)' % (rel_img, m.group(1)), txt)
        texts[path] = txt
        for line in txt.split('\n'):
            ids = re.findall(r'<a id="([^"]+)"></a>', line)
            own = next((i for i in ids if i.startswith(book.lower() + '.')), None)
            for i in ids:
                where.setdefault(i, (path, own))

    def target(path, frag):
        """Link to <file>#<anchor> relative to path; prefer the book's own
        anchor on the same line as a Word bookmark."""
        f, own = where[frag]
        rel = os.path.relpath(f, os.path.dirname(path) or '.').replace('\\', '/')
        return '%s#%s' % ('' if f == path else rel, own or frag)

    for path, txt in texts.items():
        def relink_marked(m):       # [@@LK:bookmark@@text](#x): link to the bookmark
            bm, label = bm_name(m.group(1)), m.group(2)
            if bm in where:
                return '[%s](%s)' % (label, target(path, bm))
            # The bookmark isn't in the Word file (a broken link there). Try the
            # heading with the same name: _Danger_Sense -> hh.danger-sense.
            g = slug(bm.strip('_').replace('_', ' '))
            hits = [i for i in where if i.startswith(book.lower() + '.') and
                    (i.split('.')[-1] == g or (len(g) > 8 and i.split('.')[-1].startswith(g)))]
            exact = [i for i in hits if i.split('.')[-1] == g]
            pick = exact[0] if len(exact) == 1 else hits[0] if len(hits) == 1 else None
            if pick:
                guessed_links.append((path, '%s -> %s' % (bm, pick)))
                return '[%s](%s)' % (label, target(path, pick))
            unresolved_links.append((path, bm))
            return '[%s](%s)' % (label, m.group(3))
        txt = LINK_MARK.sub(relink_marked, txt)
        txt = LINK_MARK_OUTSIDE.sub(relink_marked, txt)   # marker just before the link
        txt = re.sub(r'@@LK:.*?@@', '', txt)               # any other stray marker
        def relink(m):
            frag = bm_name(m.group(1)[1:])
            if frag in where:                    # link straight to a bookmark (Word field links)
                return '](%s)' % target(path, frag)
            tgt = old2new.get(m.group(1))
            if not tgt or tgt[0] not in pathmap: return m.group(0)
            tp = pathmap[tgt[0]]
            rel = os.path.relpath(tp, os.path.dirname(path) or '.').replace('\\', '/')
            return '](%s#%s)' % ('' if tp == path else rel, tgt[1])
        txt = link.sub(relink, txt)
        txt = html_tables.convert(txt)          # HTML tables (merged cells ...) -> pipe tables
        full = os.path.join(out, path)
        os.makedirs(os.path.dirname(full), exist_ok=True)
        open(full, 'w', encoding='utf-8', newline='\n').write(txt)
        nfiles += 1
        find_anomalies(path, txt, found)
    write_indexes(out, list(texts))
    stripped_junk[:] = [(pathmap.get(p, p), j) for p, j in stripped_junk]
    media = os.path.join(work, book + '_media', 'media')
    if os.path.isdir(media):
        shutil.copytree(media, os.path.join(out, 'img'))
    for p, junk in stripped_junk:
        found['Junk text removed from a table title (still in the Word file)'].append((p, None, junk))
    for p, name in out_of_order:
        found['Entries out of A-Z order in the Word file (moved to their A-Z place here; fix in Word)'].append((pathmap.get(p, p), None, name))
    for p, bm in guessed_links:
        found['Broken Word links pointed at the heading with the same name (check)'].append((p, None, bm))
    for p, bm in unresolved_links:
        found['Broken Word links (bookmark not in the Word file, no matching heading)'].append((p, None, bm))
    write_anomalies(root, book, found)
    write_bookmarks(root, out, book, bookmarks)
    print('%s: %d tab-separated tables made into tables; %d two-column runs left as lines' %
          (book, n_tab_tables, len(two_col_runs)))
    open(os.path.join(root, '_anomalies', book + '-two-col.txt'), 'w', encoding='utf-8', newline='\n') \
        .write('\n'.join(two_col_runs) + '\n')
    print('%s: %d files in %s; anomalies: %s' % (book, nfiles, out,
          ', '.join('%d %s' % (len(v), k.split(' (')[0].lower()) for k, v in found.items())))


INDEX = '000000-index.md'


def write_indexes(out, paths):
    """Put 000000-index.md in every folder: its files and sub-folders in
    book order, one link per line. assemble.py follows these to rebuild the
    book; the order is the order of first appearance in `paths`."""
    order = collections.OrderedDict()        # folder -> [entries]
    order[''] = []
    for p in paths:
        parts = p.split(os.sep)
        for i in range(len(parts)):
            d = os.sep.join(parts[:i])
            entry = parts[i] + ('' if i == len(parts) - 1 else '/')
            lst = order.setdefault(d, [])
            if entry not in lst:
                lst.append(entry)
    for d, entries in order.items():
        lines = ['- [%s](%s)' % (e, e + INDEX if e.endswith('/') else e) for e in entries]
        open(os.path.join(out, d, INDEX), 'w', encoding='utf-8', newline='\n').write('\n'.join(lines) + '\n')


# ---- anomalies: things to fix by hand later, listed in EEE/_anomalies/<BOOK>.md ----
ANOMALIES = [
    ('Straight quotes (should be curly)', re.compile(r'"|(?<=\w)\'|\'(?=\w)')),
    ('Underline (<u>)', re.compile(r'<u>')),
    ('Non-breaking spaces (invisible)', re.compile(' ')),
    ('Other inline HTML (<span>, <font> ...)', re.compile(r'<(?!/?(table|tbody|thead|tr|td|th|colgroup|col|strong|em|br|sup|sub|u|a)\b)[a-z]+[\s>]')),
]


def find_anomalies(path, txt, found):
    in_table = False
    for i, line in enumerate(txt.split('\n'), 1):
        if '<table' in line:
            found['HTML tables (merged cells; no pipe table possible)'].append((path, i, None))
            in_table = True
        if in_table:
            in_table = '</table>' not in line
            continue
        text = re.sub(r'<[^>]+>', '', line)          # quotes inside tags don't count
        for name, rx in ANOMALIES:
            src = line if name.startswith(('Underline', 'Other')) else text
            n = len(rx.findall(src))
            if n:
                m = rx.search(src)
                found[name].append((path, i, src[max(0, m.start() - 30):m.end() + 30].replace(' ', '␣')))


def write_bookmarks(root, out, book, bookmarks):
    """EEE/_anomalies/<BOOK>-bookmarks.md: every Word bookmark, where it ended
    up, and which of them are missing from the output."""
    where = collections.defaultdict(list)
    links = collections.Counter()
    for f in sorted(glob.glob(os.path.join(out, '**', '*.md'), recursive=True)):
        rel = os.path.relpath(f, out).replace('\\', '/')
        for i, line in enumerate(open(f, encoding='utf-8').read().split('\n'), 1):
            for a in re.findall(r'<a id="([^"]+)"></a>', line):
                if not a.startswith(book.lower() + '.'):
                    where[a].append((rel, i))
            for a in re.findall(r'\]\([^)#]*#([^)\s]+)\)', line):
                links[a] += 1
    kind = lambda n: ('_Toc: Word table-of-contents target' if n.startswith('_Toc') else
                      '_Hlk: Word automatic link bookmark' if n.startswith('_Hlk') else
                      '__DdeLink: left by LibreOffice' if n.startswith('__DdeLink') else
                      '_bookmark: Word/LibreOffice numbered' if n.startswith('_bookmark') else
                      'Named: cross-reference target')
    groups = collections.defaultdict(list)
    for name, ptxt in bookmarks:
        groups[kind(name)].append((name, ptxt))
    missing = [n for n, _ in bookmarks if n not in where]
    out_md = ['# %s bookmarks' % book, '',
              'Every bookmark in the Word file, kept as `<a id="name"></a>` where it was. '
              'Generated by `docx2eee.py`.', '',
              '- In the Word file: %d' % len(bookmarks),
              '- In the Markdown: %d' % sum(1 for n, _ in bookmarks if n in where),
              '- Missing: %d' % len(missing), '']
    if missing:
        out_md += ['## Missing', ''] + ['- `%s`' % n for n in missing] + ['']
    order = ['Named: cross-reference target', '_Hlk: Word automatic link bookmark',
             '__DdeLink: left by LibreOffice', '_bookmark: Word/LibreOffice numbered',
             '_Toc: Word table-of-contents target']
    for k in order:
        if k not in groups:
            continue
        out_md += ['## %s (%d)' % (k, len(groups[k])), '', '| Bookmark | Where | Links to it | Paragraph starts |',
                   '|---|---|---|---|']
        for name, ptxt in groups[k]:
            locs = ', '.join('[%s:%d](../%s/%s#L%d)' % (r, i, book, r, i) for r, i in where.get(name, [])) or '**missing**'
            out_md.append('| `%s` | %s | %s | %s |' % (name, locs, links[name] or '',
                                                      ptxt.replace('|', '/').strip() or '(empty)'))
        out_md.append('')
    open(os.path.join(root, '_anomalies', book + '-bookmarks.md'), 'w', encoding='utf-8',
         newline='\n').write('\n'.join(out_md))


def write_anomalies(root, book, found):
    d = os.path.join(root, '_anomalies'); os.makedirs(d, exist_ok=True)
    out = ['# %s anomalies' % book, '',
           'Generated by `docx2eee.py` each time the book is converted. '
           'Links open the file at the line.', '']
    for name, rows in found.items():
        out += ['## %s (%d)' % (name, len(rows)), '']
        for path, line, ctx in rows:
            href = '../%s/%s' % (book, path.replace('\\', '/'))
            label = path.replace('\\', '/') + (':%d' % line if line else '')
            out.append('- [%s](%s%s)%s' % (label, href, '#L%d' % line if line else '',
                                            ' — `%s`' % ctx.replace('`', "'") if ctx else ''))
        out.append('')
    open(os.path.join(d, book + '.md'), 'w', encoding='utf-8', newline='\n').write('\n'.join(out))


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2].upper())
