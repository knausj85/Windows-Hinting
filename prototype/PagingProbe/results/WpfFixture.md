## WpfFixture  — `PagingFixture WPF` [HwndWrapper[PagingFixture;;aaac0a83-3e5e-4aa9-a6e8-9f55040a23b1]], 2026-10-15 12:46

- **#1** List 'WpfNestedListBox' aid=WpfNestedListBox cls=ListBox 461x23958@2116,178 | SP=anc1:Pane TP=- Grid=- RV=y Items=y hwnd=0xF0AB2[HwndWrapper[PagingFixture;;aaac0…] SB=- SBCTL=- NAT=-
- **#2** List 'WpfListBox' aid=WpfListBox cls=ListBox 496x1329@133,178 | SP=self TP=- Grid=- RV=- Items=y hwnd=0xF0AB2[HwndWrapper[PagingFixture;;aaac0…] SB=- SBCTL=- NAT=-
- **#3** DataGrid 'WpfDataGrid' aid=WpfDataGrid cls=DataGrid 496x1329@1124,178 | SP=self TP=- Grid=y RV=- Items=y hwnd=0xF0AB2[HwndWrapper[PagingFixture;;aaac0…] SB=- SBCTL=- NAT=-
- **#4** Edit 'WpfTextBox' aid=WpfTextBox cls=TextBox 496x1329@1620,178 | SP=self TP=y Grid=- RV=y Items=- hwnd=0xF0AB2[HwndWrapper[PagingFixture;;aaac0…] SB=- SBCTL=- NAT=-
- **#5** Document 'WpfRichTextBox' aid=WpfRichTextBox cls=RichTextBox 496x1329@2611,178 | SP=self TP=y Grid=- RV=- Items=- hwnd=0xF0AB2[HwndWrapper[PagingFixture;;aaac0…] SB=- SBCTL=- NAT=-
- **#6** Tree 'WpfTreeView' aid=WpfTreeView cls=TreeView 495x1329@629,178 | SP=self TP=- Grid=- RV=- Items=y hwnd=0xF0AB2[HwndWrapper[PagingFixture;;aaac0…] SB=- SBCTL=- NAT=-
- **#7** Pane 'WpfOuterScrollViewer' aid=WpfOuterScrollViewer cls=ScrollViewer 495x1329@2116,178 | SP=self TP=- Grid=- RV=y Items=y hwnd=0xF0AB2[HwndWrapper[PagingFixture;;aaac0…] SB=- SBCTL=- NAT=-

| cand | mech | LineDown | PageDown | PageUp | ToTop | ToBottom | notes |
|---|---|---|---|---|---|---|---|
| #1 | SP | line +0.02pg SP% **FK** | page +1.00pg SP% **FK** | page -1.00pg SP% **FK** | exact -17.03pg SP% **FK** | exact +17.03pg SP% **FK** | focus:hwnd 0x0->0xF0AB2  fg->0xF0AB2'PagingFixture WPF' |
| #1 | WMV | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% |  |
| #1 | SI | line +0.01pg SP% | page +0.95pg SP% | page -0.91pg SP% | part -17.02pg SP% | exact +17.02pg SP% | items=500 vis=0..26 target=27; items=500 vis=0..26 target=53 |
| #1 | RV | line 0.00pg SP% **FK** | part 0.00pg SP% **FK** | part 0.00pg SP% **FK** | exact -17.03pg SP% **FK** | exact +17.03pg SP% **FK** | v=0.0->0.1 focus:hwnd 0x0->0xF0AB2  fg->0xF0AB2'PagingFixture WPF'; v=0.0->1.0 focus:hwnd 0x0->0xF0AB2  fg->0xF0AB2'PagingFixture WPF' |
| #1 | WHEEL | pix-only 0.00pg SP% |  |  |  |  |  |
| #2 | SP | line +0.04pg SP% **FK** | page +1.00pg SP% **FK** | page -1.00pg SP% **FK** | exact -17.52pg SP% **FK** | exact +17.52pg SP% **FK** | focus:hwnd 0x0->0xF0AB2 elKb False->True fg->0xF0AB2'PagingFixture WPF' |
| #2 | WMV | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% |  |
| #2 | SI | line +0.04pg SP% | over +17.37pg SP% | page -1.00pg SP% | part -0.04pg SP% | exact +17.52pg SP% | items=57 vis=0..26 target=27; items=58 vis=0..26 target=53 |
| #2 | WHEEL | line +0.11pg SP% |  |  |  |  |  |
| #3 | SP | line +0.04pg SP% **FK** | page +1.00pg SP% **FK** | page -1.00pg SP% **FK** | exact -17.52pg SP% **FK** | exact +17.52pg SP% **FK** | focus:hwnd 0x0->0xF0AB2 elKb False->True fg->0xF0AB2'PagingFixture WPF' |
| #3 | WMV | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% |  |
| #3 | GRIDGET | line +0.04pg SP% | page +1.00pg SP% | page -1.07pg SP% | exact -17.52pg SP% | exact +17.52pg SP% | rows=500 vis=0..26 target=27; rows=500 vis=0..26 target=53 |
| #3 | GRIDSI | line +0.04pg SP% | page +1.00pg SP% | page -1.07pg SP% | exact -17.52pg SP% | exact +17.52pg SP% | rows=500 vis=0..26 target=27 SI(cell); rows=500 vis=0..26 target=53 SI(cell) |
| #3 | SI | line +0.04pg SP% | part +0.11pg SP% | part -0.04pg SP% | part -0.04pg SP% | part +0.07pg SP% | items=29 vis=0..26 target=27; items=30 vis=0..26 target=29(clamped: not realized?) |
| #3 | WHEEL | line +0.11pg SP% |  |  |  |  |  |
| #4 | SP | line +0.02pg SP% **FK** | page +1.00pg SP% **FK** | page -1.00pg SP% **FK** | exact -14.45pg SP% **FK** | exact +14.45pg SP% **FK** | focus:hwnd 0x0->0xF0AB2 elKb False->True fg->0xF0AB2'PagingFixture WPF' |
| #4 | WMV | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% |  |
| #4 | TXT | line +0.03pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% | 2nd line->top (move=1) 'WPF TextBox line 001…'; line after last->top (move=1) 'WPF TextBox line 033…' |
| #4 | RV | line 0.00pg SP% **FK** | part 0.00pg SP% **FK** | part 0.00pg SP% **FK** | exact -14.45pg SP% **FK** | exact +14.45pg SP% **FK** | v=0.0->0.1 focus:hwnd 0x0->0xF0AB2 elKb False->True fg->0xF0AB2'PagingFixture WPF'; v=0.0->1.0 focus:hwnd 0x0->0xF0AB2 elKb False->True fg->… |
| #4 | WHEEL | line +0.07pg SP% |  |  |  |  |  |
| #5 | SP | line +0.02pg SP% **FK** | page +1.00pg SP% **FK** | page -1.00pg SP% **FK** | exact -14.06pg SP% **FK** | exact +14.06pg SP% **FK** | focus:hwnd 0x0->0xF0AB2 elKb False->True fg->0xF0AB2'PagingFixture WPF' |
| #5 | WMV | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% |  |
| #5 | TXT | line +0.03pg SP% | page +1.02pg SP% | part -0.05pg SP% | exact -14.06pg SP% | exact +14.06pg SP% | 2nd line->top (move=1) 'WPF RichTextBox para…'; line after last->top (move=1) 'WPF RichTextBox para…' |
| #5 | WHEEL | line +0.07pg SP% |  |  |  |  |  |
| #6 | SP | line +0.02pg SP% **FK** | page +1.00pg SP% **FK** | page -1.00pg SP% **FK** | exact -14.10pg SP% **FK** | exact +14.10pg SP% **FK** | focus:hwnd 0x0->0xF0AB2 elKb False->True fg->0xF0AB2'PagingFixture WPF' |
| #6 | WMV | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% |  |
| #6 | SI | line +0.03pg SP% | page +0.96pg SP% | page -0.92pg SP% | exact -14.10pg SP% | exact +14.10pg SP% | items=500 vis=0..32 target=33; items=500 vis=0..32 target=64 |
| #6 | WHEEL | line +0.07pg SP% |  |  |  |  |  |
| #7 | SP | line +0.02pg SP% **FK** | page +1.00pg SP% **FK** | page -1.00pg SP% **FK** | exact -17.03pg SP% **FK** | exact +17.03pg SP% **FK** | focus:hwnd 0x0->0xF0AB2 elKb False->True fg->0xF0AB2'PagingFixture WPF' |
| #7 | WMV | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% | no-move 0.00pg SP% |  |
| #7 | SI | line +0.01pg SP% | page +0.95pg SP% | page -0.91pg SP% | part -17.02pg SP% | exact +17.02pg SP% | items=500 vis=0..26 target=27; items=500 vis=0..26 target=53 |
| #7 | RV | line 0.00pg SP% **FK** | part 0.00pg SP% **FK** | part 0.00pg SP% **FK** | exact -17.03pg SP% **FK** | exact +17.03pg SP% **FK** | v=0.0->0.1 focus:hwnd 0x0->0xF0AB2 elKb False->True fg->0xF0AB2'PagingFixture WPF'; v=0.0->1.0 focus:hwnd 0x0->0xF0AB2 elKb False->True fg->… |
| #7 | WHEEL | pix-only 0.00pg SP% |  |  |  |  |  |
