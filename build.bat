@echo off
rem Builds dist\BotBeacons.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem No SDK, Visual Studio, installer or admin rights needed.
setlocal enabledelayedexpansion
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find csc.exe from .NET Framework 4.x.
  exit /b 1
)
if not exist dist mkdir dist

rem every PNG under art\ is embedded (art\orb-aura.png -> "orb-aura.png"), plus the default beacons.json
> dist\resources.rsp (
  for %%f in (art\*.png) do echo /resource:"art\%%~nxf",%%~nxf
  echo /resource:"beacons.json",beacons.json
)

rem compile to a temporary file: a failed build never leaves an old exe looking like a new one
del dist\BotBeacons.build.exe >nul 2>&1
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu ^
  /out:dist\BotBeacons.build.exe /win32icon:art\icon.ico ^
  /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll ^
  /reference:System.Security.dll /reference:System.Core.dll ^
  @dist\resources.rsp src\*.cs
if errorlevel 1 (
  del dist\BotBeacons.build.exe >nul 2>&1
  del dist\resources.rsp >nul 2>&1
  echo Build failed. dist\BotBeacons.exe was not changed.
  exit /b 1
)
if not exist dist\BotBeacons.build.exe (
  echo Build failed: no output. dist\BotBeacons.exe was not changed.
  exit /b 1
)
del dist\resources.rsp >nul 2>&1
move /y dist\BotBeacons.build.exe dist\BotBeacons.exe >nul
if errorlevel 1 (
  echo Built, but dist\BotBeacons.exe is in use ^(is Bot Beacons running?^). Exit it and build again.
  exit /b 1
)
echo.
echo Built dist\BotBeacons.exe
endlocal
