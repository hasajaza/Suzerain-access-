# Suzerain Access installer: finds the Suzerain game folder and copies everything from the "files" folder into it.
# Plain console text only, so screen readers read every message.

$here  = Split-Path -Parent $MyInvocation.MyCommand.Path
$files = Join-Path $here "files"

function Wait-Close { Read-Host "Press Enter to close" | Out-Null }

function Find-Game {
    $libraries = New-Object System.Collections.Generic.List[string]
    $steam = $null
    try { $steam = (Get-ItemProperty -Path "HKCU:\Software\Valve\Steam" -Name SteamPath -ErrorAction Stop).SteamPath } catch { }
    if ($steam) {
        $steam = $steam -replace '/', '\'
        $libraries.Add($steam)
        $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            $text = Get-Content -Path $vdf -Raw
            foreach ($m in [regex]::Matches($text, '"path"\s+"([^"]+)"')) {
                $libraries.Add(($m.Groups[1].Value -replace '\\\\', '\'))
            }
        }
    }
    $libraries.Add("C:\Program Files (x86)\Steam")
    $libraries.Add("C:\Program Files\Steam")
    foreach ($lib in $libraries) {
        $game = Join-Path $lib "steamapps\common\Suzerain"
        if (Test-Path (Join-Path $game "GameAssembly.dll")) { return $game }
    }
    return $null
}

Write-Host "Suzerain Access installer."

if (-not (Test-Path $files)) {
    Write-Host "The folder named files is missing next to this installer. Extract the whole zip first, then run Install.cmd again."
    Wait-Close; exit 1
}

if (Get-Process -Name "Suzerain" -ErrorAction SilentlyContinue) {
    Write-Host "Suzerain is running. Close the game, then run Install.cmd again."
    Wait-Close; exit 1
}

$game = Find-Game
if ($game) {
    Write-Host "Found Suzerain in: $game"
} else {
    Write-Host "Suzerain was not found automatically."
    $game = Read-Host "Type or paste the Suzerain game folder, the folder that contains Suzerain.exe, then press Enter"
    $game = $game.Trim().Trim('"')
}

if (-not (Test-Path (Join-Path $game "GameAssembly.dll"))) {
    Write-Host "This folder does not contain Suzerain: $game"
    Wait-Close; exit 1
}

Write-Host "Copying files. Please wait."
# robocopy merges folders reliably; exit codes below 8 mean success.
robocopy "$files" "$game" /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) {
    Write-Host "Copying failed. Right-click Install.cmd and choose Run as administrator, then try again."
    Wait-Close; exit 1
}

# Remove a copy of the mod from older versions, so only one copy exists.
$old = Join-Path $game "BepInEx\plugins\SuzerainAccess\SuzerainAccess.dll"
if (Test-Path $old) { Remove-Item $old -Force -ErrorAction SilentlyContinue }

Write-Host "Done. Suzerain Access is installed."
Write-Host "Now start Suzerain. The first start takes several minutes while BepInEx prepares the game. The game may seem frozen: do not close it."
Write-Host "When it is ready you will hear: Suzerain Access loaded. Press F1 for help."
Wait-Close
