# Regenerates BaitUI\mod\Config\XUi_InGame\windows.xml (the rows and buttons are repetitive). Run from the repo root:
#   powershell -File BaitUI\tools\gen-windows.ps1
function Btn($id,$x,$y,$w,$h,$bind){
  "`t`t`t<rect pos=`"$x,$y`" width=`"$w`" height=`"$h`" depth=`"3`">`n`t`t`t`t<sprite depth=`"2`" sprite=`"menu_empty`" color=`"[darkGrey]`" type=`"sliced`" />`n`t`t`t`t<button name=`"$id`" depth=`"3`" width=`"$w`" height=`"$h`" sprite=`"menu_empty`" defaultcolor=`"[mediumGrey]`" hoversprite=`"ui_game_select_row`" hovercolor=`"[white]`" type=`"sliced`" hoverscale=`"1.0`" />`n`t`t`t`t<label depth=`"4`" text=`"$bind`" width=`"$w`" height=`"28`" pos=`"0,-5`" justify=`"center`" font_size=`"22`" />`n`t`t`t</rect>"
}
function Lbl($x,$y,$w,$h,$bind,$size){
  "`t`t`t<label depth=`"3`" pos=`"$x,$y`" width=`"$w`" height=`"$h`" font_size=`"$size`" text=`"$bind`" />"
}
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<configs>')
[void]$sb.AppendLine("`t<append xpath=`"/windows`">")
# roster
[void]$sb.AppendLine("`t`t<window name=`"windowAzraelBaitRoster`" anchor=`"Center`" pos=`"-440, 295`" width=`"880`" height=`"590`" depth=`"15`" controller=`"AzraelBaitRoster, AzraelBaitUI`" cursor_area=`"true`">")
[void]$sb.AppendLine("`t`t`t<sprite depth=`"1`" name=`"background`" sprite=`"menu_empty`" color=`"[darkestGrey]`" type=`"sliced`" />")
[void]$sb.AppendLine("`t`t`t<sprite depth=`"8`" name=`"border`" sprite=`"menu_empty3px`" color=`"[black]`" type=`"sliced`" fillcenter=`"false`" />")
[void]$sb.AppendLine((Lbl 12 -8 856 34 '{title}' 28))
for($i=1;$i -le 8;$i++){
  $y = -52 - ($i-1)*46
  [void]$sb.AppendLine("`t`t`t<rect name=`"row$i`" pos=`"0,$y`" width=`"880`" height=`"44`" depth=`"2`" visible=`"{r${i}vis}`">")
  [void]$sb.AppendLine("`t`t`t`t<sprite depth=`"1`" sprite=`"menu_empty`" color=`"[darkGrey]`" type=`"sliced`" width=`"868`" height=`"42`" pos=`"6,0`" />")
  [void]$sb.AppendLine("`t`t`t`t<label depth=`"3`" pos=`"14,-3`" width=`"340`" height=`"40`" font_size=`"19`" text=`"{r${i}text}`" />")
  $bx = @(@('call',362,60),@('stay',426,62),@('mode',492,56),@('teth',552,78),@('heal',634,62),@('name',700,66),@('rel',770,70))
  foreach($b in $bx){ [void]$sb.AppendLine((Btn "r$i$($b[0])" $b[1] -4 $b[2] 36 "{r$i$($b[0])txt}")) }
  [void]$sb.AppendLine("`t`t`t</rect>")
}
[void]$sb.AppendLine((Lbl 12 -424 856 30 '{emptytext}' 20))
[void]$sb.AppendLine((Lbl 12 -452 856 30 '{status}' 20))
[void]$sb.AppendLine((Btn 'prev' 12 -494 80 36 'Prev'))
[void]$sb.AppendLine((Lbl 100 -498 110 30 '{pagetext}' 20))
[void]$sb.AppendLine((Btn 'next' 214 -494 80 36 'Next'))
[void]$sb.AppendLine("`t`t`t<rect name=`"adminrect`" pos=`"0,0`" width=`"880`" height=`"590`" depth=`"2`" visible=`"{adminvis}`">")
[void]$sb.AppendLine((Btn 'admin' 704 -494 80 36 'Admin'))
[void]$sb.AppendLine("`t`t`t</rect>")
[void]$sb.AppendLine((Btn 'close' 792 -494 80 36 'Close'))
[void]$sb.AppendLine((Btn 'stowall' 12 -540 110 36 'Stow all'))
[void]$sb.AppendLine((Btn 'stowactive' 130 -540 150 36 'Stow active'))
[void]$sb.AppendLine((Btn 'stowtethered' 288 -540 170 36 'Stow tethered'))
[void]$sb.AppendLine((Btn 'followall' 466 -540 130 36 'Follow all'))
[void]$sb.AppendLine((Btn 'stayall' 604 -540 110 36 'Stay all'))
[void]$sb.AppendLine("`t`t</window>")
# rename window (modelled on the game's keypad window)
[void]$sb.AppendLine("`t`t<window name=`"windowAzraelBaitName`" anchor=`"Center`" pos=`"-200, 80`" width=`"400`" height=`"150`" depth=`"15`" controller=`"AzraelBaitName, AzraelBaitUI`" cursor_area=`"true`" lock_navigation=`"true`">")
[void]$sb.AppendLine("`t`t`t<rect name=`"header`" height=`"43`" depth=`"1`">`n`t`t`t`t<headerbg />`n`t`t`t`t<label style=`"header.name`" text=`"{nametitle}`" width=`"380`" />`n`t`t`t</rect>")
[void]$sb.AppendLine("`t`t`t<rect name=`"content`" pos=`"0,-46`" height=`"50`" depth=`"1`">`n`t`t`t`t<sprite depth=`"8`" name=`"backgroundMain`" sprite=`"menu_empty3px`" pos=`"0,0`" color=`"[black]`" type=`"sliced`" fillcenter=`"false`" />`n`t`t`t`t<sprite color=`"[darkGrey]`" type=`"sliced`" />`n`t`t`t`t<textfield name=`"txtName`" depth=`"2`" pos=`"10,-8`" width=`"380`" height=`"32`" clear_on_open=`"true`" open_vk_on_open=`"false`" focus_on_open=`"true`" character_limit=`"24`" />`n`t`t`t</rect>")
[void]$sb.AppendLine("`t`t`t<simplebutton name=`"btnCancel`" depth=`"3`" pos=`"0, -104`" width=`"190`" height=`"36`" font_size=`"30`" />")
[void]$sb.AppendLine("`t`t`t<simplebutton name=`"btnOk`" depth=`"3`" pos=`"400, -104`" pivot=`"right`" width=`"190`" height=`"36`" font_size=`"30`" />")
[void]$sb.AppendLine("`t`t</window>")
# admin
[void]$sb.AppendLine("`t`t<window name=`"windowAzraelBaitAdmin`" anchor=`"Center`" pos=`"-415, 330`" width=`"830`" height=`"660`" depth=`"15`" controller=`"AzraelBaitAdmin, AzraelBaitUI`" cursor_area=`"true`">")
[void]$sb.AppendLine("`t`t`t<sprite depth=`"1`" name=`"background`" sprite=`"menu_empty`" color=`"[darkestGrey]`" type=`"sliced`" />")
[void]$sb.AppendLine("`t`t`t<sprite depth=`"8`" name=`"border`" sprite=`"menu_empty3px`" color=`"[black]`" type=`"sliced`" fillcenter=`"false`" />")
[void]$sb.AppendLine((Lbl 12 -8 806 34 '{title}' 28))
for($i=1;$i -le 10;$i++){
  $y = -50 - ($i-1)*36
  [void]$sb.AppendLine("`t`t`t<rect name=`"crow$i`" pos=`"0,$y`" width=`"830`" height=`"34`" depth=`"2`" visible=`"{c${i}vis}`">")
  [void]$sb.AppendLine("`t`t`t`t<sprite depth=`"1`" sprite=`"menu_empty`" color=`"[darkGrey]`" type=`"sliced`" width=`"818`" height=`"32`" pos=`"6,0`" />")
  [void]$sb.AppendLine("`t`t`t`t<label depth=`"3`" pos=`"14,-4`" width=`"560`" height=`"28`" font_size=`"21`" text=`"{c${i}text}`" />")
  [void]$sb.AppendLine((Btn "c${i}minus" 626 -2 80 30 "{c${i}minustxt}"))
  [void]$sb.AppendLine("`t`t`t`t<rect name=`"cplus$i`" pos=`"0,0`" width=`"830`" height=`"34`" depth=`"2`" visible=`"{c${i}plusvis}`">")
  [void]$sb.AppendLine((Btn "c${i}plus" 714 -2 80 30 "+"))
  [void]$sb.AppendLine("`t`t`t`t</rect>")
  [void]$sb.AppendLine("`t`t`t</rect>")
}
[void]$sb.AppendLine((Btn 'prev' 12 -414 80 34 'Prev'))
[void]$sb.AppendLine((Lbl 100 -418 110 30 '{pagetext}' 20))
[void]$sb.AppendLine((Btn 'next' 214 -414 80 34 'Next'))
[void]$sb.AppendLine((Lbl 12 -462 400 30 '{expagetext}' 22))
for($i=1;$i -le 4;$i++){
  $y = -494 - ($i-1)*30
  [void]$sb.AppendLine("`t`t`t<rect name=`"xrow$i`" pos=`"0,$y`" width=`"830`" height=`"28`" depth=`"2`" visible=`"{x${i}vis}`">")
  [void]$sb.AppendLine("`t`t`t`t<label depth=`"3`" pos=`"14,-2`" width=`"560`" height=`"26`" font_size=`"20`" text=`"{x${i}text}`" />")
  [void]$sb.AppendLine((Btn "x${i}del" 626 -1 100 26 "{x${i}deltxt}"))
  [void]$sb.AppendLine("`t`t`t</rect>")
}
[void]$sb.AppendLine((Btn 'exprev' 430 -460 70 30 'Prev'))
[void]$sb.AppendLine((Btn 'exnext' 508 -460 70 30 'Next'))
[void]$sb.AppendLine((Btn 'addlook' 590 -460 228 30 'Exclude what I aim at'))
[void]$sb.AppendLine((Lbl 12 -622 806 30 '{status}' 20))
[void]$sb.AppendLine((Btn 'clearex' 446 -620 150 30 'Clear excludes'))
[void]$sb.AppendLine((Btn 'back' 606 -620 100 30 'Back'))
[void]$sb.AppendLine((Btn 'close' 714 -620 104 30 'Close'))
[void]$sb.AppendLine("`t`t</window>")
[void]$sb.AppendLine("`t</append>")
[void]$sb.AppendLine('</configs>')
$out = Join-Path (Get-Location) "BaitUI\mod\Config\XUi_InGame\windows.xml"
[IO.File]::WriteAllText($out, $sb.ToString().Replace("`r`n","`n"))
"wrote $out"