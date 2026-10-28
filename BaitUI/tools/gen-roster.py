# Regenerates the three roster windows (8, 12 and 16 rows per page) in BaitUI/mod/Config/XUi_InGame/windows.xml.
# The rows and buttons are repetitive, so they are written here. The rename and admin windows in that file are kept as they are.
# Run from the repo root:  python BaitUI/tools/gen-roster.py
import os, re

W = 1150   # window width

def btn(id, x, y, w, h, text, fs=22, extra=""):
    return (f'\t\t\t<rect pos="{x},{y}" width="{w}" height="{h}" depth="3">\n'
            f'\t\t\t\t<sprite depth="2" sprite="menu_empty" color="[darkGrey]" type="sliced" />\n'
            f'\t\t\t\t<button name="{id}" depth="3" width="{w}" height="{h}" sprite="menu_empty" defaultcolor="[mediumGrey]" hoversprite="ui_game_select_row" hovercolor="[white]" type="sliced" hoverscale="1.0"{extra} />\n'
            f'\t\t\t\t<label depth="4" text="{text}" width="{w}" height="{h - 8}" pos="0,-{max(2, (h - fs) // 2 - 2)}" justify="center" font_size="{fs}" />\n'
            f'\t\t\t</rect>\n')

def lbl(x, y, w, h, text, fs, extra=""):
    return f'\t\t\t<label depth="3" pos="{x},{y}" width="{w}" height="{h}" font_size="{fs}" text="{text}"{extra} />\n'

# per-row buttons: id, x, width
ROW_BTNS = [("call", 362, 60), ("stay", 426, 62), ("mode", 492, 56), ("teth", 552, 78), ("heal", 634, 62), ("name", 700, 66),
            ("rel", 770, 70), ("auto", 846, 100), ("hbox", 950, 100), ("up", 1054, 40), ("down", 1098, 40)]

def roster(name, rows, step, fs_text, fs_btn):
    h = step - 2
    bh = min(36, h - 6)
    by = -((h - bh) // 2)
    rows_end = -52 - rows * step
    B = rows_end - 6          # details / empty text
    B2 = B - 58               # status
    B3 = B2 - 38              # first button row
    rowsA, rowsB, rowsC, rowsD, rowsE, rowsF = B3, B3 - 46, B3 - 92, B3 - 138, B3 - 184, B3 - 230
    height = -(rowsF - 36) + 12
    s = (f'\t\t<window name="{name}" anchor="Center" pos="-{W // 2}, {height // 2}" width="{W}" height="{height}" depth="15" controller="AzraelBaitRoster, AzraelBaitUI" cursor_area="true">\n'
         f'\t\t\t<sprite depth="1" name="background" sprite="menu_empty" color="[darkestGrey]" type="sliced" />\n'
         f'\t\t\t<sprite depth="8" name="border" sprite="menu_empty3px" color="[black]" type="sliced" fillcenter="false" />\n')
    # title doubles as the drag handle (drag it to move the window; the spot is remembered)
    s += f'\t\t\t<button name="dragbar" depth="2" pos="0,0" width="{W}" height="46" sprite="menu_empty" defaultcolor="[darkestGrey]" hoversprite="menu_empty" hovercolor="[darkGrey]" type="sliced" hoverscale="1.0" on_drag="true" />\n'
    s += lbl(12, -8, W - 24, 34, "{title}", 26)
    for i in range(1, rows + 1):
        y = -52 - (i - 1) * step
        s += f'\t\t\t<rect name="row{i}" pos="0,{y}" width="{W}" height="{step}" depth="2" visible="{{r{i}vis}}">\n'
        s += f'\t\t\t\t<sprite depth="1" sprite="menu_empty" color="[darkGrey]" type="sliced" width="{W - 12}" height="{h}" pos="6,0" />\n'
        s += f'\t\t\t\t<button name="r{i}sel" depth="2" pos="6,0" width="350" height="{h}" sprite="menu_empty" defaultcolor="[darkGrey]" hoversprite="ui_game_select_row" hovercolor="[mediumGrey]" type="sliced" hoverscale="1.0" />\n'
        s += f'\t\t\t\t<label depth="3" pos="14,-2" width="340" height="{h - 2}" font_size="{fs_text}" text="{{r{i}text}}" />\n'
        for bid, x, w in ROW_BTNS:
            s += btn(f"r{i}{bid}", x, by, w, bh, f"{{r{i}{bid}txt}}", fs_btn)
        s += '\t\t\t</rect>\n'
    s += lbl(12, B, W - 24, 54, "{details}", 18)
    s += lbl(12, B, W - 24, 30, "{emptytext}", 20)
    s += lbl(12, B2, W - 24, 30, "{status}", 20)
    # paging, view and window
    s += btn("prev", 12, rowsA, 80, 36, "Prev")
    s += lbl(100, rowsA - 4, 110, 30, "{pagetext}", 20)
    s += btn("next", 214, rowsA, 80, 36, "Next")
    s += btn("filter", 310, rowsA, 170, 36, "{filtertxt}")
    s += btn("sort", 488, rowsA, 150, 36, "{sorttxt}")
    s += btn("pagesize", 646, rowsA, 140, 36, "{pagesizetxt}")
    s += f'\t\t\t<rect name="adminrect" pos="0,0" width="{W}" height="{height}" depth="2" visible="{{adminvis}}">\n'
    s += btn("admin", W - 176, rowsA, 80, 36, "Admin")
    s += '\t\t\t</rect>\n'
    s += btn("close", W - 88, rowsA, 80, 36, "Close")
    # group commands
    s += btn("stowall", 12, rowsB, 110, 36, "Stow all")
    s += btn("stowactive", 130, rowsB, 150, 36, "Stow active")
    s += btn("stowtethered", 288, rowsB, 170, 36, "Stow tethered")
    s += btn("followall", 466, rowsB, 130, 36, "Follow all")
    s += btn("stayall", 604, rowsB, 110, 36, "Stay all")
    s += btn("allhere", 722, rowsB, 170, 36, "{allheretxt}")
    s += btn("deaths", 900, rowsB, 120, 36, "{deathstxt}")
    s += btn("resetpos", 1028, rowsB, 110, 36, "Re-center")
    # tags and HUD
    s += btn("tgltags", 12, rowsC, 190, 36, "{tagstxt}")
    s += btn("tglparty", 210, rowsC, 190, 36, "{partytxt}")
    s += btn("tglfed", 408, rowsC, 170, 36, "{fedtxt}")
    s += lbl(586, rowsC - 4, 110, 30, "{sizetxt}", 20, ' justify="right"')
    s += btn("sizedown", 704, rowsC, 50, 36, "-")
    s += btn("sizeup", 760, rowsC, 50, 36, "+")
    s += btn("tglkills", 818, rowsC, 160, 36, "{killstxt}")
    s += btn("tglglow", 986, rowsC, 152, 36, "{glowtxt}")
    s += btn("tglpbars", 12, rowsD, 230, 36, "{pbarstxt}")
    s += btn("cyclecolor", 250, rowsD, 250, 36, "{colortxt}")
    s += btn("cyclebar", 508, rowsD, 250, 36, "{bartxt}")
    s += btn("tgllowhp", 766, rowsD, 190, 36, "{lowhptxt}")
    s += btn("tgllowsnd", 964, rowsD, 174, 36, "{lowsndtxt}")
    s += btn("cycletarget", 12, rowsE, 480, 36, "{targettxt}")
    s += btn("pickkey", 500, rowsE, 258, 36, "{keytxt}")
    s += btn("pickgroup", 766, rowsE, 372, 36, "{grouptxt}")
    # teams, downed tags, wheel key
    s += btn("teamcycle", 12, rowsF, 280, 36, "{teamtxt}")
    s += btn("teamcall", 300, rowsF, 130, 36, "{teamcalltxt}")
    s += btn("teamsave", 438, rowsF, 150, 36, "{teamsavetxt}")
    s += btn("teamnew", 596, rowsF, 130, 36, "{teamnewtxt}")
    s += btn("tgldowned", 734, rowsF, 200, 36, "{downedtxt}")
    s += btn("pickwheel", 942, rowsF, 196, 36, "{wheeltxt}")
    s += '\t\t</window>\n'
    return s

here = os.path.dirname(os.path.abspath(__file__))
path = os.path.join(here, "..", "mod", "Config", "XUi_InGame", "windows.xml")
with open(path, encoding="utf-8") as f:
    text = f.read().replace("\r\n", "\n")
start = text.index('\t\t<window name="windowAzraelBaitRoster"')
end = text.index('\t\t<window name="windowAzraelBaitName"')
gen = (roster("windowAzraelBaitRoster", 8, 46, 19, 22)
       + roster("windowAzraelBaitRoster12", 12, 40, 17, 20)
       + roster("windowAzraelBaitRoster16", 16, 34, 15, 18))
text = text[:start] + gen + text[end:]
with open(path, "w", encoding="utf-8", newline="\n") as f:
    f.write(text)
print("wrote", os.path.normpath(path))
