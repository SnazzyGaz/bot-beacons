<#
  Bot Beacons installer (per user, no admin rights).

  Builds BotBeacons.exe from the source bundle with the C# compiler that ships with Windows (.NET Framework 4.x),
  installs it to %LOCALAPPDATA%\Programs\BotBeacons, optionally puts a personal beacons.json in place, optionally
  turns on Start with Windows and makes a projects folder, then starts the app (which shows the pairing code).

  Release zip (prebuilt BotBeacons.exe next to this script; nothing is compiled):
  powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1 [-BeaconsJson <beacons.json>] [-AutoStart] [-NoStart]
  From source:
  powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1 -Bundle <bot-beacons-src.zip>
      [-BeaconsJson <beacons.json>] [-AutoStart] [-ProjectsFolder "Bot Projects"] [-NoStart]
  After local edits, rebuild and reinstall from an unpacked source folder (the one holding build.bat):
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\install.ps1 -Source .

  Safe to run again (upgrade): it stops a running Bot Beacons first and keeps settings, pairing and positions.
  It only touches: %LOCALAPPDATA%\Programs\BotBeacons, %APPDATA%\BotBeacons, the HKCU Run value "BotBeacons",
  and the optional projects folder.
#>
param(
  [string]$Bundle = "",
  [string]$Source = "",
  [string]$Exe = "",
  [string]$BeaconsJson = "",
  [switch]$AutoStart,
  [string]$ProjectsFolder = "",
  [switch]$NoStart
)
$ErrorActionPreference = "Stop"
$installDir = Join-Path $env:LOCALAPPDATA "Programs\BotBeacons"
$dataDir = Join-Path $env:APPDATA "BotBeacons"
$work = Join-Path $env:TEMP ("BotBeacons-build-" + [guid]::NewGuid().ToString("N").Substring(0, 8))

function Say($m) { Write-Output ("[Bot Beacons] " + $m) }

# release zip: a prebuilt BotBeacons.exe next to this script is installed as is (no build)
if (-not $Bundle -and -not $Source -and -not $Exe) { $cand = Join-Path $PSScriptRoot "BotBeacons.exe"; if (Test-Path $cand) { $Exe = $cand } }
if (-not $Bundle -and -not $Source -and -not $Exe) { throw "Give -Bundle <bot-beacons-src.zip> or -Source <folder with build.bat>, or run this from the release folder (next to BotBeacons.exe)." }
if ($Exe -and -not (Test-Path $Exe)) { throw "Exe not found: $Exe" }
if ($Bundle -and -not (Test-Path $Bundle)) { throw "Bundle not found: $Bundle" }
if ($Source -and -not (Test-Path (Join-Path $Source "build.bat"))) { throw "No build.bat in source folder: $Source" }
New-Item -ItemType Directory -Force -Path $work, $installDir, $dataDir | Out-Null
if ($Exe) {
  $builtExe = (Resolve-Path $Exe).Path
  $icon = Join-Path (Split-Path $builtExe) "art\icon.ico"
  Say "Using the prebuilt BotBeacons.exe (release zip)."
} else {
  if ($Source) { foreach ($n in "build.bat", "beacons.json", "src", "art", "sounds") { $sp = Join-Path $Source $n; if (Test-Path $sp) { Copy-Item $sp $work -Recurse -Force } } }
  else { Expand-Archive -Path $Bundle -DestinationPath $work -Force }
  $root = $work
  if (-not (Test-Path (Join-Path $root "build.bat"))) { $sub = Get-ChildItem $work -Directory | Select-Object -First 1; if ($sub) { $root = $sub.FullName } }

  Say "Building BotBeacons.exe..."
  $out = & cmd.exe /c "`"$root\build.bat`"" 2>&1
  if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $root "dist\BotBeacons.exe"))) { $out | Select-Object -Last 20 | ForEach-Object { Write-Output $_ }; throw "Build failed." }
  $builtExe = Join-Path $root "dist\BotBeacons.exe"
  $icon = Join-Path $root "art\icon.ico"
}

# stop a running copy (upgrade) - only Bot Beacons, matched by its own install path
$running = Get-Process -Name "BotBeacons" -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -like "$installDir*" }
if ($running) { Say "Stopping the running copy for the upgrade..."; $running | Stop-Process -Force; Start-Sleep -Milliseconds 800 }

Copy-Item $builtExe (Join-Path $installDir "BotBeacons.exe") -Force
if (Test-Path $icon) { Copy-Item $icon (Join-Path $installDir "icon.ico") -Force }
# 1.1.5: alert sounds go next to the exe (release folder sounds\, or dist\sounds\ from the build)
$snd = @((Join-Path (Split-Path $builtExe) "sounds"), $(if ($root) { Join-Path $root "sounds" })) | Where-Object { $_ -and (Test-Path (Join-Path $_ "*.wav")) } | Select-Object -First 1
if ($snd) { New-Item -ItemType Directory -Force -Path (Join-Path $installDir "sounds") | Out-Null; Copy-Item (Join-Path $snd "*.wav") (Join-Path $installDir "sounds") -Force }
Say "Installed to $installDir"

if ($BeaconsJson) {
  if (-not (Test-Path $BeaconsJson)) { throw "beacons.json not found: $BeaconsJson" }
  $null = Get-Content $BeaconsJson -Raw | ConvertFrom-Json   # must be valid JSON
  $dest = Join-Path $dataDir "beacons.json"
  if (Test-Path $dest) { Copy-Item $dest (Join-Path $dataDir ("beacons.before-install." + (Get-Date -Format "yyyyMMddHHmmss") + ".json")) -Force }
  Copy-Item $BeaconsJson $dest -Force
  Say "Personal beacons.json installed."
}

$exe = Join-Path $installDir "BotBeacons.exe"
if ($AutoStart) { & $exe --autostart on | Out-Null; Start-Sleep -Milliseconds 500; Say "Start with Windows: on." }

if ($ProjectsFolder) {
  $pf = Join-Path $env:USERPROFILE $ProjectsFolder
  New-Item -ItemType Directory -Force -Path $pf | Out-Null
  Say "Projects folder: $pf"
}

# Start menu shortcut (per user)
try {
  $lnk = Join-Path ([Environment]::GetFolderPath("Programs")) "Bot Beacons.lnk"
  $ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut($lnk); $s.TargetPath = $exe; $s.WorkingDirectory = $installDir; $s.IconLocation = "$exe,0"; $s.Save()
} catch { Say "Could not create the Start menu shortcut (not needed)." }

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
if (-not $NoStart) { Start-Process -FilePath $exe -WorkingDirectory $installDir; Say "Started. If it is not paired yet, a window shows a one-time pairing code." }
Say "Done."
