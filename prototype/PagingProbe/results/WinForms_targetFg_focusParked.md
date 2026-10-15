## WinForms_targetFg_focusParked  — `PagingFixture WinForms` [WindowsForms10.Window.8.app.0.2360855_r3_ad1], 2026-10-15 12:51

- **#1** List 'WfListBox' aid=WfListBox cls=WindowsForms10.ListBox.a… 482x1310@102,148 | SP=self TP=- Grid=- RV=y Items=y hwnd=0xE0A52[WindowsForms10.ListBox.app.0.236…] SB=0xE0A52 SBCTL=- NAT=LB

| cand | mech | LineDown | PageDown | PageUp | ToTop | ToBottom | notes |
|---|---|---|---|---|---|---|---|
| #1 | SP | line +0.03pg SB **K** | page +0.97pg SB **K** | page -0.97pg SB **K** | exact -14.63pg SB **K** | exact +14.63pg SB **K** | focus:hwnd 0x80A40->0xE0A52 uia 'WfTextBox'->'ListBox item 000' |
| #1 | WMV | line +0.03pg SB | page +0.97pg SB | page -0.97pg SB | exact -14.63pg SB | exact +14.63pg SB |  |
| #1 | SI | line +0.06pg SB | page +1.06pg SB | page -1.03pg SB | exact -14.63pg SB | exact +14.63pg SB | items=500 vis=0..32 target=33; items=500 vis=0..32 target=65 |
| #1 | RV | line +0.13pg SB **K** | over +1.44pg SB **K** | over -1.59pg SB **K** | exact -14.63pg SB **K** | exact +14.63pg SB **K** | v=0.0->1.0 focus:hwnd 0x80A40->0xE0A52 uia 'WfTextBox'->'ListBox item 000'; v=0.0->10.0 focus:hwnd 0x80A40->0xE0A52 uia 'WfTextBox'->'ListBo… |
