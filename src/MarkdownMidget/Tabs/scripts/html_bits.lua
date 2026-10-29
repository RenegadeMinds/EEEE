-- html_bits.lua - Pandoc filter used by MMEEEE's Build (and converter/build_docx.py).
-- The Markdown keeps a few things as HTML (Markdown has no syntax for them).
-- DOCX would drop raw HTML, so turn each into the real thing:
--   <a id="x"></a>          -> a bookmark (empty span with that id), so links work
--   <sup>..</sup> <sub>..</sub> <u>..</u>  -> superscript / subscript / underline
--   <table>...</table>      -> a real table (merged cells kept)

local wrap = { sup = pandoc.Superscript, sub = pandoc.Subscript, u = pandoc.Underline }

function Inlines(inlines)
  local out = pandoc.List()
  local stack = {}            -- open <sup>/<sub>/<u>: {tag, collected inlines}
  local function emit(x)
    if #stack > 0 then stack[#stack].items:insert(x) else out:insert(x) end
  end
  for _, el in ipairs(inlines) do
    local handled = false
    if el.t == 'RawInline' and el.format == 'html' then
      local id = el.text:match('^<a id="([^"]+)"></a>$') or el.text:match('^<a id="([^"]+)">$')
      local open = el.text:match('^<(%a+)>$')
      local close = el.text:match('^</(%a+)>$')
      if id then
        emit(pandoc.Span({}, pandoc.Attr(id))); handled = true
      elseif el.text == '</a>' then
        handled = true
      elseif open and wrap[open] then
        table.insert(stack, { tag = open, items = pandoc.List() }); handled = true
      elseif close and wrap[close] and #stack > 0 and stack[#stack].tag == close then
        local top = table.remove(stack)
        emit(wrap[close](top.items)); handled = true
      end
    end
    if not handled then emit(el) end
  end
  while #stack > 0 do          -- unclosed tag: keep its text
    local top = table.remove(stack)
    for _, x in ipairs(top.items) do emit(x) end
  end
  return out
end

-- Headings that were bold lines in the Word original ("COMBAT" in every
-- monster) carry an anchor like ff.aboleth.combat (two dots). In Word they
-- get the paragraph style "Minor Heading" (bold), so the table of contents
-- skips them, as it did in the original.
function Header(h)
  local minor = false
  h.content:walk({ Span = function(s) if select(2, s.identifier:gsub('%.', '')) >= 2 then minor = true end end })
  if minor then
    return pandoc.Div({ pandoc.Para({ pandoc.Strong(h.content) }) },
                      pandoc.Attr('', {}, { ['custom-style'] = 'Minor Heading' }))
  end
end

-- The book's first line is its title; build_docx.py also passes it as the
-- document title (Title style, above the table of contents), so drop the
-- plain-text copy.
function Pandoc(doc)
  local t = doc.meta.title and pandoc.utils.stringify(doc.meta.title)
  if t and #doc.blocks > 0 and pandoc.utils.stringify(doc.blocks[1]) == t then
    doc.blocks:remove(1)
  end
  return doc
end

function RawBlock(el)
  if el.format == 'html' and el.text:match('<table') then
    local doc = pandoc.read(el.text, 'html')
    return doc.blocks
  end
  local id = el.format == 'html' and el.text:match('^<a id="([^"]+)"></a>%s*$')
  if id then
    return pandoc.Plain({ pandoc.Span({}, pandoc.Attr(id)) })
  end
end
