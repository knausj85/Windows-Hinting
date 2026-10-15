## Notepad  — `notepad_long.txt - Notepad` [Notepad], 2026-10-15 12:56

- **#1** Document 'Text editor' aid= cls=RichEditD2DPT 1856x1176@44,228 | SP=self TP=y Grid=- RV=y Items=- hwnd=0x20ACC[RichEditD2DPT] SB=0x20ACC SBCTL=- NAT=EDIT
- **#2** List '' aid=TabListView cls=ListView 976x64@128,88 | SP=- TP=- Grid=- RV=- Items=- hwnd=0x709CC[InputSiteWindowClass] SB=- SBCTL=- NAT=-
- **#3** 50020 'Plain text' aid=ContentTextBlock cls=TextBlock 148x64@660,1404 | SP=- TP=y Grid=- RV=- Items=- hwnd=0x40A30[InputSiteWindowClass] SB=- SBCTL=- NAT=-

| cand | mech | LineDown | PageDown | PageUp | ToTop | ToBottom | notes |
|---|---|---|---|---|---|---|---|
| #1 | SP | line +0.03pg SB **FK** | page +0.98pg SB **FK** | page -0.98pg SB **FK** | exact -56.88pg SB **FK** | exact +56.88pg SB **FK** | focus:hwnd 0x0->0x20ACC elKb False->True uia ''->'Text editor' fg->0x2707A2'notepad_long.txt - N…' |
| #1 | WMV | line +0.03pg SB | page +0.98pg SB | page -0.98pg SB | exact -56.88pg SB | exact +56.88pg SB |  |
| #1 | EMS | line +0.03pg SB | page +0.98pg SB | page -0.98pg SB |  |  | ret=0x10000; ret=0x10021 |
| #1 | TXT | line +0.06pg SB | page +1.01pg SB | page -0.98pg SB | exact -56.88pg SB | part +56.87pg SB | 2nd line->top (move=1) 'Left line 0002 - lor…'; line after last->top (move=1) 'Left line 0035 - lor…' |
| #1 | TXTT | line +0.06pg SB | page +1.01pg SB | page -0.98pg SB | exact -56.88pg SB | exact +56.88pg SB | 2nd line->top (move=1) 'Left line 0002 - lor…'; line after last->top (move=1) 'Left line 0035 - lor…' |
| #1 | RV | big +0.57pg SB **FK** | over +5.69pg SB **FK** | over -2.95pg SB **FK** | no-move 0.00pg SB **FK** | part +1.15pg SB **FK** | v=0.0->1.0 focus:hwnd 0x0->0x20ACC elKb False->True uia ''->'Text editor' fg->0x2707A2'notepad_long.txt - N…'; v=0.0->10.0 focus:hwnd 0x0->0… |
| #1 | WHEEL | line +0.11pg SB |  |  |  |  |  |
| #2 | WMV | no-move  PIX | no-move  PIX | no-move  PIX | no-move  PIX | no-move  PIX | (prep-top?); (prep-mid?) |
| #2 | WHEEL | no-move  PIX |  |  |  |  | (prep-top?) |
| #3 | WMV | no-move 0.00pg TXT | no-move 0.00pg TXT | no-move 0.00pg TXT | no-move 0.00pg TXT | no-move 0.00pg TXT | (prep-mid FAILED via TXT) |
| #3 | TXT | no-move 0.00pg TXT | no-move 0.00pg TXT | no-move 0.00pg TXT | no-move 0.00pg TXT | no-move 0.00pg TXT | 2nd line->top (move=0) 'Plain text'; line after last->top (move=0) 'Plain text' |
| #3 | TXTT | no-move 0.00pg TXT | no-move 0.00pg TXT | no-move 0.00pg TXT | no-move 0.00pg TXT | no-move 0.00pg TXT | 2nd line->top (move=0) 'Plain text'; line after last->top (move=0) 'Plain text' |
| #3 | WHEEL | no-move 0.00pg TXT |  |  |  |  |  |
