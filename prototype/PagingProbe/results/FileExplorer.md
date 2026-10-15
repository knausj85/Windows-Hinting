## FileExplorer  — `System32 - File Explorer` [CabinetWClass], 2026-10-15 12:58

- **#1** List 'Items View' aid= cls=UIItemsView 3170x1584@571,272 | SP=self TP=- Grid=y RV=- Items=y hwnd=0x809F6[DirectUIHWND] SB=- SBCTL=0x90408 NAT=-
- **#2** Tree 'Navigation Pane' aid=100 cls=SysTreeView32 546x1584@17,272 | SP=- TP=- Grid=- RV=- Items=y hwnd=0x30756[SysTreeView32] SB=- SBCTL=- NAT=TV

| cand | mech | LineDown | PageDown | PageUp | ToTop | ToBottom | notes |
|---|---|---|---|---|---|---|---|
| #1 | SP | line +0.03pg SB **FK** | page +1.00pg SB **FK** | page -1.00pg SB **FK** | exact -195.74pg SB **FK** | exact +195.74pg SB **FK** | focus:hwnd 0x0->0x809F6 uia ''->'0409' fg->0x130A38'System32 - File Expl…' |
| #1 | WMV | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB |  |
| #1 | WMVCTL | line +0.03pg SB | page +0.99pg SB | page -0.99pg SB | exact -195.74pg SB | exact +195.74pg SB | ->parent 0x60454[CtrlNotifySink] |
| #1 | GRIDGET | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB | no-move 0.00pg SB | rows=25 vis=0..23 target=24; rows=27 vis=1..25 target=0 |
| #1 | GRIDSI | line +0.03pg SB | part +0.03pg SB | over -2.97pg SB | exact -195.74pg SB | part +0.03pg SB | rows=25 vis=0..23 target=24 SI(row); rows=27 vis=1..25 target=0 SI(row) |
| #1 | SI | no-move 0.00pg SP% | part +0.03pg SP% | over -3.00pg SP% | exact -195.74pg SP% | part +0.03pg SP% | items=25 vis=0..23 target=24; items=25 vis=0..23 target=24(clamped: not realized?) |
| #1 | WHEEL | line +0.12pg SP% |  |  |  |  |  |
| #2 | WMV | no-move 0.00pg ITEM | no-move 0.00pg ITEM | no-move 0.00pg ITEM | no-move 0.00pg ITEM | no-move 0.00pg ITEM | (prep-top?); (prep-mid?) |
| #2 | SI | pix-only 0.00pg ITEM **FK** | pix-only 0.00pg ITEM **FK** | pix-only 0.00pg ITEM **FK** | pix-only 0.00pg ITEM **FK** | pix-only 0.00pg ITEM **FK** | (prep-top?) items=22 vis=1..21 target=21(clamped: not realized?) focus:hwnd 0x0->0x30756 elKb False->True uia ''->'Local Disk (C:)' fg->0x13… |
| #2 | NAT | no-move 0.00pg ITEM | no-move 0.00pg ITEM | no-move 0.00pg ITEM | no-move 0.00pg ITEM | no-move 0.00pg ITEM | (prep-top?) TVM_SELECTITEM(FIRSTVISIBLE) visCount=23 ok=1; (prep-mid?) TVM_SELECTITEM(FIRSTVISIBLE) visCount=23 ok=1 |
| #2 | WHEEL | no-move 0.00pg ITEM |  |  |  |  | (prep-top?) |
