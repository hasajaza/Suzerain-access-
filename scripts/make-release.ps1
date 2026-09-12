# Builds the player downloads from the Release build output.
#
#   powershell -ExecutionPolicy Bypass -File scripts\make-release.ps1 -BepInExZip "C:\path\BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip"
#
# Optional: -NvdaClient "C:\path\x64\nvdaControllerClient.dll"  (or put nvdaControllerClient.dll into the native folder)
#
# Creates in the release folder:
#   SuzerainAccess-<version>-complete.zip  : everything (BepInEx, mod, speech files, installer). For most players.
#   SuzerainAccess-<version>-mod-only.zip  : only the mod files, for players who already have BepInEx.
# Build the solution in Visual Studio (Release) first.

param([string]$BepInExZip = "", [string]$NvdaClient = "")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$bin  = Join-Path $root "SuzerainAccess\bin\Release"
$kit  = Join-Path $root "release-kit"
$dll  = Join-Path $bin "SuzerainAccess.dll"
if (-not (Test-Path $dll)) { throw "Build the solution in Release first: $dll not found." }

$version = (Get-Item $dll).VersionInfo.ProductVersion
if (-not $version) { $version = "unknown" }
$version = ($version -split '\+')[0]

if ($NvdaClient -eq "") {
    $candidate = Join-Path $root "native\nvdaControllerClient.dll"
    if (Test-Path $candidate) { $NvdaClient = $candidate }
}

function New-Dir([string]$path) {
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

# Mod files laid out as they belong in the game folder.
function Add-ModFiles([string]$gameRoot) {
    New-Item -ItemType Directory -Force -Path (Join-Path $gameRoot "BepInEx\plugins") | Out-Null
    Copy-Item $dll (Join-Path $gameRoot "BepInEx\plugins")
    foreach ($f in @("UniversalSpeech.dll", "JawsBridge32.exe")) {
        $src = Join-Path $bin $f
        if (Test-Path $src) { Copy-Item $src $gameRoot }
    }
    if ($NvdaClient -ne "") { Copy-Item $NvdaClient (Join-Path $gameRoot "nvdaControllerClient.dll") }
}

function Add-Licenses([string]$dir) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Copy-Item (Join-Path $root "LICENSE") (Join-Path $dir "SuzerainAccess-LICENSE.txt")
    Copy-Item (Join-Path $root "native\UniversalSpeech-LICENSE.txt") $dir
    Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.md") $dir
    Copy-Item (Join-Path $kit "licenses\BepInEx-LICENSE-LGPL-2.1.txt") $dir
    Copy-Item (Join-Path $kit "licenses\Il2CppInterop-LICENSE-LGPL-3.0.txt") $dir
    if ($NvdaClient -ne "") { Copy-Item (Join-Path $kit "licenses\nvdaControllerClient-LICENSE-LGPL-2.1.txt") $dir }
}

function Save-Zip([string]$stage, [string]$zipName) {
    $zip = Join-Path $root "release\$zipName"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
    Write-Host "Created $zip"
}

# ---- mod only (zip root = game folder)
$stage = Join-Path $root "release\stage-mod"
New-Dir $stage
Add-ModFiles $stage
Add-Licenses (Join-Path $stage "SuzerainAccess-licenses")
Copy-Item (Join-Path $root "docs\KEYBOARD.md") $stage
Copy-Item (Join-Path $root "docs\PLAYER-INSTALL.md") $stage
Save-Zip $stage "SuzerainAccess-$version-mod-only.zip"

# ---- complete (installer + files folder)
if ($BepInExZip -eq "") {
    Write-Host "No -BepInExZip given: the complete package was not created."
    exit 0
}
if (-not (Test-Path $BepInExZip)) { throw "BepInEx zip not found: $BepInExZip" }

$stage = Join-Path $root "release\stage-complete"
New-Dir $stage
$files = Join-Path $stage "files"
New-Item -ItemType Directory -Force -Path $files | Out-Null
Expand-Archive -Path $BepInExZip -DestinationPath $files -Force
if (-not (Test-Path (Join-Path $files "winhttp.dll"))) {
    throw "This does not look like a BepInEx 6 IL2CPP Windows zip (winhttp.dll missing at its top level)."
}
# Never ship game-derived or personal files.
foreach ($d in @("BepInEx\interop", "BepInEx\unity-libs", "BepInEx\cache", "BepInEx\config", "BepInEx\LogOutput.log")) {
    $p = Join-Path $files $d
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}
Add-ModFiles $files
Copy-Item (Join-Path $kit "Install.cmd") $stage
Copy-Item (Join-Path $kit "install.ps1") $stage
Copy-Item (Join-Path $kit "README-FIRST.txt") $stage
Copy-Item (Join-Path $root "docs\KEYBOARD.md") $stage
Add-Licenses (Join-Path $stage "licenses")
Save-Zip $stage "SuzerainAccess-$version-complete.zip"
