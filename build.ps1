<#
.SYNOPSIS
  Builds every mod in this repo into ready-to-install folders and release zips.

.EXAMPLE
  .\build.ps1                       # build all, output in .\dist
  .\build.ps1 -Install              # also copy the mods into the game's Mods folder
  .\build.ps1 -GameDir "D:\Games\7 Days To Die"
  .\build.ps1 -Only Speedometer     # just one mod (folder name)
#>
param(
    [string]$GameDir,
    [switch]$Install,
    [string[]]$Only
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$root = $PSScriptRoot
$dist = Join-Path $root 'dist'

# Folder in repo -> folder name in Mods\ (and the .csproj, if the mod has code)
$mods = @(
    @{ Dir = 'RemoveZombieDogs';   Name = 'RemoveZombieDogs';         Project = $null },
    @{ Dir = 'QuestDisconnectFix'; Name = 'AzraelQuestDisconnectFix'; Project = 'src\AzraelQuestDisconnectFix.csproj' },
    @{ Dir = 'Speedometer';        Name = 'AzraelSpeedometer';        Project = 'src\AzraelSpeedometer.csproj' }
)
if ($Only) { $mods = $mods | Where-Object { $Only -contains $_.Dir -or $Only -contains $_.Name } }

function New-ModZip([string]$sourceDir, [string]$zipPath, [string]$folderName) {
    # Forward-slash entry names so the zip unpacks correctly on Linux servers too.
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
    try {
        Get-ChildItem $sourceDir -Recurse -File | ForEach-Object {
            $rel = $_.FullName.Substring($sourceDir.Length).TrimStart('\', '/').Replace('\', '/')
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, "$folderName/$rel")
        }
    } finally { $zip.Dispose() }
}

New-Item -ItemType Directory -Force $dist | Out-Null
foreach ($m in $mods) {
    $out = Join-Path $dist $m.Name
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    Copy-Item (Join-Path $root "$($m.Dir)\mod") $out -Recurse

    if ($m.Project) {
        $a = @('build', (Join-Path $root "$($m.Dir)\$($m.Project)"), '-c', 'Release', '-o', $out, '--nologo', '-v', 'quiet')
        if ($GameDir) { $a += "-p:GameDir=$GameDir" }
        Write-Host "Building $($m.Name)..." -ForegroundColor Cyan
        & dotnet @a
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $($m.Name)" }
    }

    $version = ([xml](Get-Content (Join-Path $out 'ModInfo.xml') -Raw)).xml.Version.value
    $zipPath = Join-Path $dist "$($m.Name)-v$version.zip"
    New-ModZip $out $zipPath $m.Name
    Write-Host "  -> dist\$($m.Name)-v$version.zip" -ForegroundColor Green

    if ($Install) {
        $gd = if ($GameDir) { $GameDir } else { 'E:\SteamLibrary\steamapps\common\7 Days To Die' }
        $target = Join-Path $gd "Mods\$($m.Name)"
        if (Test-Path $target) { Remove-Item $target -Recurse -Force }
        Copy-Item $out $target -Recurse
        Write-Host "  -> installed to $target" -ForegroundColor Green
    }
}
