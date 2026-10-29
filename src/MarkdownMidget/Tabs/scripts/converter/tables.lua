-- tables.lua - Pandoc filter used by docx2eee.py.
-- The Word tables put their title ("Table: Aberrations") in a first row that
-- is one cell merged across every column, and their column names in a bold
-- second row. Markdown pipe tables can't merge cells, so Pandoc would fall
-- back to raw HTML. This moves the title out to a bold line above the table
-- and makes the bold row the table's real header row.

-- Pandoc moves Word's "Title" paragraph into metadata, out of the text.
-- Put it back as the first line.
function Pandoc(doc)
  if doc.meta.title then
    doc.blocks:insert(1, pandoc.Para(pandoc.utils.stringify(doc.meta.title)))
  end
  return doc
end

-- Images: drop Word's width/height so Pandoc writes plain ![alt](file)
-- instead of an HTML <img>, and unwrap figures (caption -> italic line).
function Image(img)
  img.attributes = {}
  img.attr = pandoc.Attr()
  return img
end

function Figure(fig)
  local out = pandoc.List(fig.content)
  local cap = pandoc.utils.stringify(fig.caption.long or {})
  if cap ~= '' then out:insert(pandoc.Para({ pandoc.Emph(pandoc.Str(cap)) })) end
  return out
end

local function all_strong(row)
  for _, cell in ipairs(row.cells) do
    local txt = pandoc.utils.stringify(cell.contents)
    if txt ~= '' then
      local strong = false
      pandoc.walk_block(pandoc.Div(cell.contents), { Strong = function() strong = true end })
      if not strong then return false end
    end
  end
  return true
end

function Table(tbl)
  local ncols = #tbl.colspecs
  local body = tbl.bodies[1]
  if not body or #body.body == 0 then return nil end
  local out = {}
  -- 1. merged title row -> paragraph above the table
  local first = body.body[1]
  if #first.cells == 1 and first.cells[1].col_span == ncols and ncols > 1 then
    local blocks = first.cells[1].contents
    local title = pandoc.utils.stringify(blocks)
    if #blocks > 0 and blocks[1].t == 'Header' then
      -- the title is a real Word heading (it's in the table of contents): keep it one
      for _, b in ipairs(blocks) do table.insert(out, b) end
    elseif title ~= '' then
      table.insert(out, pandoc.Para({ pandoc.Strong(pandoc.Str(title)) }))
    end
    body.body:remove(1)
  end
  -- 2. bold first row -> header row (only if the table has no header yet)
  if #tbl.head.rows == 0 and #body.body > 1 then
    local r = body.body[1]
    local span = 0
    for _, c in ipairs(r.cells) do span = span + c.col_span end
    if span == ncols and all_strong(r) then
      tbl.head.rows:insert(r)
      body.body:remove(1)
    end
  end
  if #out == 0 then return nil end
  table.insert(out, tbl)
  return out
end
