<#
  Bot Beacons uninstaller (per user). Stops the app, removes the Start with Windows entry, the Start menu
  shortcut and the program folder. Settings, pairing and beacons.json in %APPDATA%\BotBeacons are kept unless
  -RemoveData is given. The optional projects folder is never touched.
  powershell -NoProfile -ExecutionPolicy Bypass -File uninstall.ps1 [-RemoveData]
#>
param([switch]$RemoveData)
$installDir = Join-Path $env:LOCALAPPDATA "Programs\BotBeacons"
$dataDir = Join-Path $env:APPDATA "BotBeacons"
Get-Process -Name "BotBeacons" -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -like "$installDir*" } | Stop-Process -Force
Start-Sleep -Milliseconds 500
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "BotBeacons" -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath("Programs")) "Bot Beacons.lnk") -ErrorAction SilentlyContinue
Remove-Item $installDir -Recurse -Force -ErrorAction SilentlyContinue
if ($RemoveData) { Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue; Write-Output "[Bot Beacons] Removed the app and its data." }
else { Write-Output "[Bot Beacons] Removed the app. Settings kept in $dataDir (use -RemoveData to delete them)." }
