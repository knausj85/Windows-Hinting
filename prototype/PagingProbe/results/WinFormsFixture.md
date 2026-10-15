## WinFormsFixture  — `PagingFixture WinForms` [WindowsForms10.Window.8.app.0.2360855_r3_ad1], 2026-10-15 14:00

- **#1** Document 'RichEdit Control' aid=WfRichTextBox cls=WindowsForms10.RICHEDIT5… 486x1310@2572,148 | SP=self TP=y Grid=- RV=y Items=- hwnd=0x20A42[WindowsForms10.RICHEDIT50W.app.0…] SB=0x20A42 SBCTL=- NAT=EDIT
- **#2** List 'WfListBox' aid=WfListBox cls=WindowsForms10.ListBox.a… 482x1310@102,148 | SP=self TP=- Grid=- RV=y Items=y hwnd=0xE0A52[WindowsForms10.ListBox.app.0.236…] SB=0xE0A52 SBCTL=- NAT=LB
- **#3** Tree 'WfTreeView' aid=WfTreeView cls=WindowsForms10.SysTreeVi… 482x1310@596,148 | SP=self TP=- Grid=- RV=y Items=y hwnd=0x809C8[WindowsForms10.SysTreeView32.app…] SB=0x809C8 SBCTL=- NAT=TV
- **#4** Table 'WfListView' aid=WfListView cls=WindowsForms10.SysListVi… 482x1310@1090,148 | SP=self TP=- Grid=y RV=y Items=y hwnd=0xC0932[WindowsForms10.SysListView32.app…] SB=0xC0932 SBCTL=- NAT=LV
- **#5** DataGrid 'WfDataGridView' aid=WfDataGridView cls=WindowsForms10.Window.8.… 482x1310@1584,148 | SP=- TP=- Grid=y RV=- Items=- hwnd=0x50A16[WindowsForms10.Window.8.app.0.23…] SB=- SBCTL=0x60AA2 NAT=-
- **#6** Edit 'WfTextBox' aid=WfTextBox cls=WindowsForms10.Edit.app.… 482x1310@2078,148 | SP=self TP=y Grid=- RV=y Items=- hwnd=0x80A40[WindowsForms10.Edit.app.0.236085…] SB=0x80A40 SBCTL=- NAT=EDIT

| cand | mech | LineDown | PageDown | PageUp | ToTop | ToBottom | notes |
|---|---|---|---|---|---|---|---|
| #1 | SP | line +0.03pg SB **FK** | no-move 0.00pg SB **FK** | over -2.94pg SB **FK** | exact -14.80pg SB **FK** | no-move 0.00pg SB **FK** | focus:hwnd 0x0->0x20A42 elKb False->True uia ''->'RichEdit Control' fg->0xF0AA6'PagingFixture WinFor…' |
| #1 | WMV | line +0.03pg SB | page +0.98pg SB | page -0.98pg SB | exact -14.80pg SB | exact +14.80pg SB |  |
| #1 | EMS | line +0.03pg SB | page +0.98pg SB | page -0.98pg SB |  |  | ret=0x10001; ret=0x1001F |
| #1 | TXT | line +0.03pg SB | page +1.01pg SB | page -0.98pg SB | exact -14.80pg SB | exact +14.80pg SB | 2nd line->top (move=1) 'RichTextBox line 001…'; line after last->top (move=1) 'RichTextBox line 032…' |
| #1 | TXTT | line +0.03pg SB | page +1.01pg SB | page -0.98pg SB | exact -14.80pg SB | exact +14.80pg SB | 2nd line->top (move=1) 'RichTextBox line 001…'; line after last->top (move=1) 'RichTextBox line 032…' |
| #1 | RV | line +0.15pg SB **FK** | over +1.48pg SB **FK** | over -2.94pg SB **FK** | exact -14.80pg SB **FK** | exact +14.80pg SB **FK** | v=0.0->1.0 focus:hwnd 0x0->0x20A42 elKb False->True uia ''->'RichEdit Control' fg->0xF0AA6'PagingFixture WinFor…'; v=0.0->10.0 focus:hwnd 0x… |
| #1 | WHEEL | line +0.09pg SB |  |  |  |  |  |
| #1 | WHEELX | line +0.09pg SB |  |  |  |  |  |
| #1 | WHEELS | line +0.09pg SB |  |  |  |  |  |
| #2 | SP | line +0.03pg SB **FK** | page +0.97pg SB **FK** | page -0.97pg SB **FK** | exact -14.63pg SB **FK** | exact +14.63pg SB **FK** | focus:hwnd 0x0->0xE0A52 uia ''->'ListBox item 000' fg->0xF0AA6'PagingFixture WinFor…' |
| #2 | WMV | line +0.03pg SB | page +0.97pg SB | page -0.97pg SB | exact -14.63pg SB | exact +14.63pg SB |  |
| #2 | SI | line +0.06pg SB | page +1.06pg SB | page -1.03pg SB | exact -14.63pg SB | exact +14.63pg SB | items=500 vis=0..32 target=33; items=500 vis=0..32 target=65 |
| #2 | NAT | line +0.03pg SB | page +1.00pg SB | page -1.00pg SB | exact -14.63pg SB | exact +14.63pg SB | LB_SETTOPINDEX(1) per=32 ret=0; LB_SETTOPINDEX(32) per=32 ret=0 |
| #2 | RV | line +0.13pg SB **FK** | over +1.44pg SB **FK** | over -1.59pg SB **FK** | exact -14.63pg SB **FK** | exact +14.63pg SB **FK** | v=0.0->1.0 focus:hwnd 0x0->0xE0A52 uia ''->'ListBox item 000' fg->0xF0AA6'PagingFixture WinFor…'; v=0.0->10.0 focus:hwnd 0x0->0xE0A52 uia ''… |
| #2 | WHEEL | line +0.09pg SB |  |  |  |  |  |
| #2 | WHEELX | line +0.09pg SB |  |  |  |  |  |
| #2 | WHEELS | line +0.09pg SB |  |  |  |  |  |
| #3 | SP | line +0.03pg SB **FK** | page +0.97pg SB **FK** | page -0.97pg SB **FK** | exact -16.24pg SB **FK** | exact +16.24pg SB **FK** | focus:hwnd 0x0->0x809C8 uia ''->'WfTreeView' fg->0xF0AA6'PagingFixture WinFor…' |
| #3 | WMV | line +0.03pg SB | page +0.97pg SB | page -0.97pg SB | exact -16.24pg SB | exact +16.24pg SB |  |
| #3 | SI | no-move 0.00pg SB | over +1.97pg SB | page -1.00pg SB | exact -16.24pg SB | exact +16.24pg SB | items=500 vis=0..28 target=29; items=500 vis=0..28 target=57 |
| #3 | NAT | line +0.03pg SB | page +1.00pg SB | page -1.00pg SB | exact -16.24pg SB | exact +16.24pg SB | TVM_SELECTITEM(FIRSTVISIBLE) visCount=29 ok=1 |
| #3 | RV | no-move 0.00pg SB **FK** | no-move 0.00pg SB **FK** | no-move 0.00pg SB **FK** | no-move 0.00pg SB **FK** | no-move 0.00pg SB **FK** | v=0.0->1.0 focus:hwnd 0x0->0x809C8 uia ''->'WfTreeView' fg->0xF0AA6'PagingFixture WinFor…'; v=0.0->10.0 focus:hwnd 0x0->0x809C8 uia ''->'WfT… |
| #3 | WHEEL | line +0.10pg SB |  |  |  |  |  |
| #3 | WHEELX | line +0.10pg SB |  |  |  |  |  |
| #3 | WHEELS | line +0.10pg SB |  |  |  |  |  |
| #4 | SP | line +0.04pg SB **FK** | page +1.00pg SB **FK** | page -1.00pg SB **FK** | exact -18.23pg SB **FK** | exact +18.23pg SB **FK** | focus:hwnd 0x0->0xC0932 uia ''->'ListView item 00…' fg->0xF0AA6'PagingFixture WinFor…' |
| #4 | WMV | line +0.04pg SB | page +1.00pg SB | page -1.00pg SB | exact -18.23pg SB | exact +18.23pg SB |  |
| #4 | GRIDGET | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB | rows=500 vis=0..25 target=26; rows=500 vis=0..25 target=51 |
| #4 | GRIDSI | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB | rows=500 vis=0..25 target=26 cell=null; rows=500 vis=0..25 target=51 cell=null |
| #4 | SI | line +0.04pg SB | page +1.00pg SB | page -1.08pg SB | exact -18.23pg SB | exact +18.23pg SB | items=500 vis=0..25 target=26; items=500 vis=0..25 target=51 |
| #4 | NAT | line +0.04pg SB | page +1.00pg SB | page -1.00pg SB | exact -18.23pg SB | exact +18.23pg SB | LVM_ENSUREVISIBLE(26) top=0 per=26 ret=1; LVM_ENSUREVISIBLE(51) top=0 per=26 ret=1 |
| #4 | RV | no-move 0.00pg SB **FK** | no-move 0.00pg SB **FK** | no-move 0.00pg SB **FK** | no-move 0.00pg SB **FK** | no-move 0.00pg SB **FK** | v=0.0->1.0 focus:hwnd 0x0->0xC0932 uia ''->'ListView item 00…' fg->0xF0AA6'PagingFixture WinFor…'; v=0.0->10.0 focus:hwnd 0x0->0xC0932 uia '… |
| #4 | WHEEL | line +0.12pg SB |  |  |  |  |  |
| #4 | WHEELX | line +0.12pg SB |  |  |  |  |  |
| #4 | WHEELS | line +0.12pg SB |  |  |  |  |  |
| #5 | WMV | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB |  |
| #5 | WMVCTL | line +0.05pg SB | page +0.99pg SB | page -0.99pg SB | exact -22.66pg SB | exact +22.66pg SB | ->parent 0x50A16[WindowsForms10.Windo…] |
| #5 | GRIDGET | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB | rows=500 vis=0..21 target=22; rows=500 vis=0..21 target=43 |
| #5 | GRIDSI | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB | rows=500 vis=0..21 target=22 no ScrollItem on cell/row; rows=500 vis=0..21 target=43 no ScrollItem on cell/row |
| #5 | WHEEL | line +0.14pg SB |  |  |  |  |  |
| #5 | WHEELX | line +0.14pg SB |  |  |  |  |  |
| #5 | WHEELS | line +0.14pg SB |  |  |  |  |  |
| #6 | SP | line +0.03pg SB **FK** | page +0.97pg SB **FK** | page -0.97pg SB **FK** | exact -15.13pg SB **FK** | exact +15.13pg SB **FK** | focus:hwnd 0x0->0x80A40 elKb False->True uia ''->'WfTextBox' fg->0xF0AA6'PagingFixture WinFor…' |
| #6 | WMV | line +0.03pg SB | page +0.97pg SB | page -0.97pg SB | exact -15.13pg SB | exact +15.13pg SB |  |
| #6 | EMS | line +0.03pg SB | page +0.97pg SB | page -0.97pg SB |  |  | ret=0x10001; ret=0x1001E |
| #6 | TXT | line +0.06pg SB | page +1.10pg SB | page -1.00pg SB | exact -15.13pg SB | exact +15.10pg SB | 2nd line->top (move=1) 'TextBox line 002 - t…'; line after last->top (move=1) 'TextBox line 034 - t…' |
| #6 | TXTT | line +0.06pg SB | page +1.10pg SB | page -1.00pg SB | exact -15.13pg SB | exact +15.13pg SB | 2nd line->top (move=1) 'TextBox line 002 - t…'; line after last->top (move=1) 'TextBox line 034 - t…' |
| #6 | RV | line +0.13pg SB **FK** | over +1.48pg SB **FK** | over -1.55pg SB **FK** | exact -15.13pg SB **FK** | exact +15.13pg SB **FK** | v=0.0->1.0 focus:hwnd 0x0->0x80A40 elKb False->True uia ''->'WfTextBox' fg->0xF0AA6'PagingFixture WinFor…'; v=0.0->10.0 focus:hwnd 0x0->0x80… |
| #6 | WHEEL | line +0.10pg SB |  |  |  |  |  |
| #6 | WHEELX | line +0.10pg SB |  |  |  |  |  |
| #6 | WHEELS | line +0.10pg SB |  |  |  |  |  |
