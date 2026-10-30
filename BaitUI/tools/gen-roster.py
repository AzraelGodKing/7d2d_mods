# Writes BaitUI/mod/Config/XUi_InGame/windows.xml: every Bait UI window. The rows and buttons are repetitive,
# so they are generated here rather than kept by hand.
# Run from anywhere:  python BaitUI/tools/gen-roster.py
#
# Bait UI 1.9.0 layout: the roster window has tabs (Thralls, Groups, Display, Keys, Tools) instead of seven rows of
# buttons under the list, and a command bar at the bottom. Per-thrall buttons that were on every row (rename, release,
# auto boxes, move up/down) live on the thrall page now. The admin window has tabs too (settings, feed rates,
# exclude list, players).
import os

W = 1150   # roster window width


def btn(id, x, y, w, h, text, fs=22, extra=""):
    return (f'\t\t\t<rect pos="{x},{y}" width="{w}" height="{h}" depth="3">\n'
            f'\t\t\t\t<sprite depth="2" sprite="menu_empty" color="[darkGrey]" type="sliced" />\n'
            f'\t\t\t\t<button name="{id}" depth="3" width="{w}" height="{h}" sprite="menu_empty" defaultcolor="[mediumGrey]" hoversprite="ui_game_select_row" hovercolor="[white]" type="sliced" hoverscale="1.0"{extra} />\n'
            f'\t\t\t\t<label depth="4" text="{text}" width="{w}" height="{h - 8}" pos="0,-{max(2, (h - fs) // 2 - 2)}" justify="center" font_size="{fs}" />\n'
            f'\t\t\t</rect>\n')


def lbl(x, y, w, h, text, fs, extra=""):
    return f'\t\t\t<label depth="3" pos="{x},{y}" width="{w}" height="{h}" font_size="{fs}" text="{text}" color="{{textcol}}"{extra} />\n'


def field(name, x, y, w, h, limit):
    return (f'\t\t\t<rect pos="{x},{y}" width="{w}" height="{h}" depth="3">\n'
            '\t\t\t\t<sprite depth="2" sprite="menu_empty" color="[darkGrey]" type="sliced" />\n'
            f'\t\t\t\t<textfield name="{name}" depth="3" pos="6,-4" width="{w - 12}" height="{h - 6}" character_limit="{limit}" clear_on_open="false" open_vk_on_open="false" focus_on_open="false" />\n'
            '\t\t\t</rect>\n')


def head(name, ctrl, w, h, depth=15):
    return (f'\t\t<window name="{name}" anchor="Center" pos="-{w // 2}, {h // 2}" width="{w}" height="{h}" depth="{depth}" controller="{ctrl}, AzraelBaitUI" cursor_area="true">\n'
            f'\t\t\t<sprite depth="1" name="background" sprite="menu_empty" color="{{bgcol}}" type="sliced" />\n'
            f'\t\t\t<sprite depth="8" name="border" sprite="menu_empty3px" color="[black]" type="sliced" fillcenter="false" />\n')


def tab(i, vis_h):
    return f'\t\t\t<rect name="tab{i}area" pos="0,0" width="{W}" height="{vis_h}" depth="2" visible="{{tab{i}vis}}">\n'


def buttons_row(y, items, x0=12, gap=8, h=38, fs=21):
    # items: (id, width, text)
    s, x = "", x0
    for bid, w, text in items:
        s += btn(bid, x, y, w, h, text, fs)
        x += w + gap
    return s


# per-row buttons on the Thralls tab: id, x, width
ROW_BTNS = [("call", 530, 74), ("stay", 608, 74), ("mode", 686, 62), ("teth", 752, 96), ("heal", 852, 76), ("track", 932, 80), ("more", 1016, 124)]
TABS = ["{tab0txt}", "{tab1txt}", "{tab2txt}", "{tab3txt}", "{tab4txt}"]


def roster(name, rows, step, fs_text, fs_btn):
    h = step - 2
    bh = min(36, h - 6)
    by = -((h - bh) // 2)
    C0 = -100                              # content top (under the tab bar)
    area = 46 + rows * step + 62           # the Thralls tab: toolbar, rows, details
    area = max(area, 7 * 46 + 10)          # room for the other tabs
    Ycmd = C0 - area - 4
    height = -(Ycmd - 40 - 34) + 8
    s = head(name, "AzraelBaitRoster", W, height)
    # title doubles as the drag handle (drag it to move the window; the spot is remembered)
    s += f'\t\t\t<button name="dragbar" depth="2" pos="0,0" width="{W}" height="46" sprite="menu_empty" defaultcolor="[darkestGrey]" hoversprite="menu_empty" hovercolor="[darkGrey]" type="sliced" hoverscale="1.0" on_drag="true" />\n'
    s += lbl(12, -8, W - 24, 34, "{title}", 24)
    # tab bar
    for i, t in enumerate(TABS):
        s += btn(f"tab{i}", 12 + i * 150, -52, 144, 40, t, 22)
    s += f'\t\t\t<rect name="adminrect" pos="0,0" width="{W}" height="{height}" depth="2" visible="{{adminvis}}">\n'
    s += btn("admin", W - 264, -52, 124, 40, "{admintxt}", 22)
    s += '\t\t\t</rect>\n'
    s += btn("close", W - 132, -52, 120, 40, "{closetxt}", 22)
    s += f'\t\t\t<sprite depth="2" pos="6,-96" width="{W - 12}" height="2" sprite="menu_empty" color="[black]" type="sliced" />\n'

    # ---- Thralls
    s += tab(0, height)
    s += btn("prev", 12, C0 - 4, 80, 36, "&lt;")
    s += lbl(98, C0 - 8, 110, 30, "{pagetext}", 20, ' justify="center"')
    s += btn("next", 214, C0 - 4, 80, 36, "&gt;")
    s += btn("filter", 304, C0 - 4, 160, 36, "{filtertxt}", 20)
    s += btn("sort", 472, C0 - 4, 150, 36, "{sorttxt}", 20)
    s += field("search", 630, C0 - 4, 200, 36, 24)
    s += btn("pagesize", 838, C0 - 4, 130, 36, "{pagesizetxt}", 20)
    s += btn("allhere", 976, C0 - 4, 162, 36, "{allheretxt}", 20)
    R0 = C0 - 46
    for i in range(1, rows + 1):
        y = R0 - (i - 1) * step
        s += f'\t\t\t<rect name="row{i}" pos="0,{y}" width="{W}" height="{step}" depth="2" visible="{{r{i}vis}}">\n'
        s += f'\t\t\t\t<sprite depth="1" sprite="menu_empty" color="{{r{i}col}}" type="sliced" width="{W - 12}" height="{h}" pos="6,0" />\n'
        s += f'\t\t\t\t<button name="r{i}sel" depth="2" pos="6,0" width="516" height="{h}" sprite="menu_empty" defaultcolor="[darkGrey]" hoversprite="ui_game_select_row" hovercolor="[mediumGrey]" type="sliced" hoverscale="1.0" />\n'
        s += f'\t\t\t\t<label depth="3" pos="14,-2" width="504" height="{h - 2}" font_size="{fs_text}" text="{{r{i}text}}" color="{{textcol}}" />\n'
        for bid, x, w in ROW_BTNS:
            s += btn(f"r{i}{bid}", x, by, w, bh, f"{{r{i}{bid}txt}}", fs_btn)
        s += '\t\t\t</rect>\n'
    rows_end = R0 - rows * step
    s += lbl(12, rows_end - 4, W - 24, 54, "{details}", 18)
    s += lbl(12, rows_end - 4, W - 24, 30, "{emptytext}", 20)
    s += '\t\t\t</rect>\n'

    # ---- Groups: whole-roster commands, teams, blood moon, PvP, kennel
    s += tab(1, height)
    y = C0 - 4
    s += lbl(12, y, 600, 28, "{grp1hdr}", 20)
    y -= 30
    s += buttons_row(y, [("stowall", 150, "{stowalltxt}"), ("stowactive", 150, "{stowactivetxt}"), ("stowtethered", 170, "{stowtethtxt}"),
                         ("followall", 150, "{followalltxt}"), ("stayall", 150, "{stayalltxt}"), ("allhere2", 170, "{allheretxt}"), ("deaths", 130, "{deathstxt}")])
    y -= 56
    s += lbl(12, y, 600, 28, "{grp2hdr}", 20)
    y -= 30
    s += buttons_row(y, [("teamcycle", 330, "{teamtxt}"), ("teamcall", 160, "{teamcalltxt}"), ("teamsave", 170, "{teamsavetxt}"), ("teamnew", 160, "{teamnewtxt}")])
    y -= 56
    s += lbl(12, y, 600, 28, "{grp3hdr}", 20)
    y -= 30
    s += buttons_row(y, [("bloodmoon2", 300, "{bmtxt}"), ("pvp", 260, "{pvptxt}"), ("kennelall", 260, "{kennelalltxt}")])
    y -= 50
    s += lbl(12, y, W - 24, 60, "{grouphelp}", 18)
    s += '\t\t\t</rect>\n'

    # ---- Display: tags, HUD, colours, columns, sizes
    s += tab(2, height)
    y = C0 - 4
    rowsd = [
        [("tgltags", 216, "{tagstxt}"), ("tglparty", 216, "{partytxt}"), ("tglfed", 216, "{fedtxt}"), ("tglpbars", 216, "{pbarstxt}"), ("tglkills", 214, "{killstxt}")],
        [("tglglow", 216, "{glowtxt}"), ("tgllowhp", 216, "{lowhptxt}"), ("tgllowsnd", 216, "{lowsndtxt}"), ("tgldowned", 216, "{downedtxt}"), ("tglmap", 214, "{maptxt}")],
        [("tglstrip", 216, "{striptxt}"), ("tglcompact", 216, "{compacttxt}"), ("cycletheme", 216, "{themetxt}"), ("tglcb", 216, "{cbtxt}"), ("tglbanner", 214, "{bannertxt}")],
        [("tgldmg", 216, "{dmgtxt}"), ("tgllines", 216, "{linestxt}"), ("tgldpanel", 216, "{dpaneltxt}"), ("cyclekf", 216, "{kftxt}"), ("tglsetup", 214, "{setuptxt}")],
        [("cyclecolor", 300, "{colortxt}"), ("cyclebar", 300, "{bartxt}"), ("cycletarget", 520, "{targettxt}")],
    ]
    for r in rowsd:
        s += buttons_row(y, r, fs=19)
        y -= 46
    s += lbl(12, y - 4, 170, 30, "{sizetxt}", 19, ' justify="right"')
    s += btn("sizedown", 190, y, 46, 38, "-")
    s += btn("sizeup", 242, y, 46, 38, "+")
    s += lbl(296, y - 4, 230, 30, "{wsizetxt}", 19, ' justify="right"')
    s += btn("wsizedown", 534, y, 46, 38, "-")
    s += btn("wsizeup", 586, y, 46, 38, "+")
    s += btn("resetpos", 642, y, 170, 38, "{recentertxt}", 19)
    y -= 46
    s += lbl(12, y - 4, 170, 30, "{colshdr}", 19, ' justify="right"')
    s += buttons_row(y, [("col0", 150, "{col0txt}"), ("col1", 150, "{col1txt}"), ("col2", 150, "{col2txt}"), ("col3", 150, "{col3txt}"), ("col4", 150, "{col4txt}"), ("col5", 150, "{col5txt}")], x0=190, fs=19)
    s += '\t\t\t</rect>\n'

    # ---- Keys
    s += tab(3, height)
    y = C0 - 4
    s += buttons_row(y, [("pickkey", 360, "{keytxt}"), ("pickwheel", 360, "{wheeltxt}"), ("pickgoto", 400, "{gototxt}")])
    y -= 46
    s += buttons_row(y, [("pickgroup", 740, "{grouptxt}"), ("cyclemod", 388, "{modtxt}")])
    y -= 56
    s += lbl(12, y, W - 24, 200, "{keyhelp}", 19)
    s += '\t\t\t</rect>\n'

    # ---- Tools
    s += tab(4, height)
    y = C0 - 4
    s += buttons_row(y, [("thrallpage", 210, "{pagebtntxt}"), ("bestiary", 210, "{bestiarytxt}"), ("summary", 230, "{summarytxt}"), ("top", 210, "{toptxt}"), ("tglhelp", 234, "{helptxt}")])
    y -= 46
    s += buttons_row(y, [("statsme", 260, "{statsmetxt}"), ("statsserver", 260, "{statsservertxt}"), ("craft", 300, "{crafttxt}")])
    y -= 56
    s += lbl(12, y, W - 24, 200, "{toolhelp}", 19)
    s += '\t\t\t</rect>\n'

    # ---- command bar and status, under every tab
    s += f'\t\t\t<sprite depth="2" pos="6,{Ycmd + 4}" width="{W - 12}" height="2" sprite="menu_empty" color="[black]" type="sliced" />\n'
    s += lbl(12, Ycmd - 6, 150, 30, "{cmdlbl}", 20, ' justify="right"')
    s += field("cmd", 170, Ycmd - 2, 420, 36, 80)
    s += btn("cmdgo", 598, Ycmd - 2, 70, 36, "{cmdgotxt}", 20)
    s += lbl(680, Ycmd - 6, W - 692, 30, "{cmdhint}", 17)
    s += lbl(12, Ycmd - 44, W - 24, 30, "{status}", 20)
    s += '\t\t</window>\n'
    return s


def stats():
    # The thrall page: one thrall's stats and every per-thrall action, in sections.
    w, h = 1000, 760
    s = head("windowAzraelBaitStats", "AzraelBaitStats", w, h, 16)
    s += lbl(12, -8, w - 24, 34, "{stitle}", 26)
    s += lbl(16, -48, w - 32, 250, "{sbody}", 20)
    y = -310

    def sect(title, items):
        nonlocal s, y
        s += lbl(12, y, 600, 26, title, 19)
        y -= 28
        s += buttons_row(y, items, h=36, fs=20)
        y -= 50

    sect("{carehdr}", [("rename", 150, "{renametxt}"), ("autorev", 170, "{autorevtxt}"), ("autoheal", 170, "{autohealtxt}"), ("second", 200, "{secondtxt}"), ("kennel", 130, "{kenneltxt}"), ("track", 110, "{tracktxt}")])
    sect("{perkhdr}", [("ptough", 140, "{ptoughtxt}"), ("phit", 140, "{phittxt}"), ("pregen", 140, "{pregentxt}"), ("ptaunt", 140, "{ptaunttxt}"), ("preset", 200, "{resettxt}")])
    sect("{posthdr}", [("guard", 190, "{guardtxt}"), ("padd", 210, "{paddtxt}"), ("pstart", 180, "{patroltxt}"), ("pclear", 170, "{pcleartxt}"), ("scout", 130, "{scouttxt}")])
    sect("{morehdr}", [("evolve", 220, "{evolvetxt}"), ("bloodmoon", 250, "{bmtxt}"), ("scream", 120, "{screamtxt}"), ("retire", 130, "{retiretxt}"), ("release", 120, "{releasetxt}"), ("up", 46, "^"), ("down", 46, "v")])
    s += lbl(12, y + 6, w - 24, 30, "{status}", 20)
    y -= 34
    s += btn("prevthrall", 12, y, 110, 36, "{prevtxt}")
    s += btn("nextthrall", 130, y, 110, 36, "{nexttxt}")
    s += btn("back", w - 256, y, 120, 36, "{rostertxt}")
    s += btn("close", w - 128, y, 116, 36, "{closetxt}")
    s += '\t\t</window>\n'
    return s


def name_window():
    return '''		<window name="windowAzraelBaitName" anchor="Center" pos="-200, 80" width="400" height="150" depth="15" controller="AzraelBaitName, AzraelBaitUI" cursor_area="true" lock_navigation="true">
			<rect name="header" height="43" depth="1">
				<headerbg />
				<label style="header.name" text="{nametitle}" width="380" />
			</rect>
			<rect name="content" pos="0,-46" height="50" depth="1">
				<sprite depth="8" name="backgroundMain" sprite="menu_empty3px" pos="0,0" color="[black]" type="sliced" fillcenter="false" />
				<sprite color="[darkGrey]" type="sliced" />
				<textfield name="txtName" depth="2" pos="10,-8" width="380" height="32" clear_on_open="true" open_vk_on_open="false" focus_on_open="true" character_limit="40" />
			</rect>
			<simplebutton name="btnCancel" depth="3" pos="0, -104" width="190" height="36" font_size="30" />
			<simplebutton name="btnOk" depth="3" pos="400, -104" pivot="right" width="190" height="36" font_size="30" />
		</window>
'''


def setup():
    # New thrall setup (U42): name, mode, auto-heal, auto-revive in one go.
    w, h = 720, 250
    s = head("windowAzraelBaitSetup", "AzraelBaitSetup", w, h, 17)
    s += lbl(12, -10, w - 24, 34, "{sutitle}", 26)
    s += lbl(12, -52, 140, 30, "{sunamelbl}", 20, ' justify="right"')
    s += field("suname", 160, -48, 360, 38, 40)
    y = -104
    s += buttons_row(y, [("sumode", 220, "{sumodetxt}"), ("suheal", 220, "{suhealtxt}"), ("surev", 240, "{surevtxt}")], fs=20)
    y -= 56
    s += buttons_row(y, [("suskip", 160, "{suskiptxt}"), ("sunever", 240, "{sunevertxt}")], fs=20)
    s += btn("suok", w - 192, y, 180, 38, "{suoktxt}", 20)
    s += '\t\t</window>\n'
    return s


def bestiary():
    w, rows, step = 960, 12, 36
    h = 46 + 46 + rows * step + 100
    s = head("windowAzraelBaitBestiary", "AzraelBaitBestiary", w, h, 16)
    s += lbl(12, -8, w - 24, 34, "{btitle}", 26)
    y = -50
    s += btn("prev", 12, y, 70, 36, "&lt;")
    s += lbl(88, y - 4, 110, 30, "{pagetext}", 20, ' justify="center"')
    s += btn("next", 204, y, 70, 36, "&gt;")
    s += btn("kind", 284, y, 200, 36, "{kindtxt}", 20)
    s += btn("onlytamed", 492, y, 220, 36, "{onlytxt}", 20)
    s += field("bsearch", 720, y, 228, 36, 24)
    R0 = y - 46
    for i in range(1, rows + 1):
        yy = R0 - (i - 1) * step
        s += f'\t\t\t<rect name="brow{i}" pos="0,{yy}" width="{w}" height="{step}" depth="2" visible="{{b{i}vis}}">\n'
        s += f'\t\t\t\t<sprite depth="1" sprite="menu_empty" color="{{b{i}col}}" type="sliced" width="{w - 12}" height="{step - 2}" pos="6,0" />\n'
        s += f'\t\t\t\t<label depth="3" pos="14,-5" width="{w - 28}" height="{step - 6}" font_size="19" text="{{b{i}text}}" color="{{b{i}tcol}}" />\n'
        s += '\t\t\t</rect>\n'
    y = R0 - rows * step - 8
    s += lbl(12, y, w - 24, 30, "{bfoot}", 19)
    y -= 42
    s += btn("back", w - 256, y, 120, 36, "{rostertxt}")
    s += btn("close", w - 128, y, 116, 36, "{closetxt}")
    s += '\t\t</window>\n'
    return s


def summary():
    w, h = 760, 470
    s = head("windowAzraelBaitSummary", "AzraelBaitSummary", w, h, 16)
    s += lbl(12, -8, w - 24, 34, "{mtitle}", 26)
    s += lbl(16, -50, w - 32, 340, "{mbody}", 21)
    y = -h + 50
    s += btn("mreset", 12, y, 220, 36, "{mresettxt}", 20)
    s += btn("back", w - 256, y, 120, 36, "{rostertxt}")
    s += btn("close", w - 128, y, 116, 36, "{closetxt}")
    s += '\t\t</window>\n'
    return s


def admin():
    # Admin window: Settings, Feed rates, Exclude list, Players. Ten generic rows, each with a label and up to four buttons.
    w, rows, step = 960, 10, 38
    h = 46 + 46 + 46 + rows * step + 46 + 40 + 50
    s = head("windowAzraelBaitAdmin", "AzraelBaitAdmin", w, h, 15)
    s += lbl(12, -8, w - 24, 34, "{title}", 26)
    for i, t in enumerate(["{atab0}", "{atab1}", "{atab2}", "{atab3}"]):
        s += btn(f"atab{i}", 12 + i * 170, -50, 164, 38, t, 21)
    s += btn("back", w - 256, -50, 120, 38, "{rostertxt}")
    s += btn("close", w - 128, -50, 116, 38, "{closetxt}")
    y = -96
    s += btn("prev", 12, y, 70, 36, "&lt;")
    s += lbl(88, y - 4, 200, 30, "{pagetext}", 20, ' justify="center"')
    s += btn("next", 294, y, 70, 36, "&gt;")
    s += field("asearch", 374, y, 260, 36, 32)
    s += lbl(644, y - 4, w - 656, 30, "{ahint}", 18)
    R0 = y - 46
    for i in range(1, rows + 1):
        yy = R0 - (i - 1) * step
        s += f'\t\t\t<rect name="arow{i}" pos="0,{yy}" width="{w}" height="{step}" depth="2" visible="{{a{i}vis}}">\n'
        s += f'\t\t\t\t<sprite depth="1" sprite="menu_empty" color="[darkGrey]" type="sliced" width="{w - 12}" height="{step - 2}" pos="6,0" />\n'
        s += f'\t\t\t\t<label depth="3" pos="14,-5" width="460" height="{step - 6}" font_size="19" text="{{a{i}text}}" />\n'
        for j in range(1, 5):
            s += btn(f"a{i}b{j}", 480 + (j - 1) * 118, -2, 112, step - 6, f"{{a{i}b{j}txt}}", 18)
        s += '\t\t\t</rect>\n'
    y = R0 - rows * step - 6
    s += buttons_row(y, [(f"f{j}", 180, f"{{f{j}txt}}") for j in range(1, 6)], fs=19)
    s += lbl(12, y - 46, w - 24, 30, "{status}", 20)
    s += '\t\t</window>\n'
    return s


here = os.path.dirname(os.path.abspath(__file__))
path = os.path.join(here, "..", "mod", "Config", "XUi_InGame", "windows.xml")
gen = ("<configs>\n\t<append xpath=\"/windows\">\n"
       + roster("windowAzraelBaitRoster", 8, 46, 19, 21)
       + roster("windowAzraelBaitRoster12", 12, 40, 17, 19)
       + roster("windowAzraelBaitRoster16", 16, 34, 15, 17)
       + stats() + name_window() + admin() + setup() + bestiary() + summary()
       + "\t</append>\n</configs>\n")
with open(path, "w", encoding="utf-8", newline="\n") as f:
    f.write(gen)
print("wrote", os.path.normpath(path))
