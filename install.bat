@echo off
chcp 65001 >nul
setlocal
title Movo - installer (from source)
rem Installs Movo for the current user: builds movo.exe from ./src, registers .mv/.mvl/.mvs, adds movo to PATH.
rem Run this from a full copy of the repository (needs the folders src, libs and assets next to this file).
set "ROOT=%~dp0"
set "DIR=%LOCALAPPDATA%\Movo"
set "LIB=C:\movo\library"
if not exist "%ROOT%src\Program.cs" (
  echo Cannot find the "src" folder next to install.bat. Unzip the whole repository first.
  goto :fail
)
if not exist "%DIR%" mkdir "%DIR%"

echo [1/5] Copying icons...
copy /y "%ROOT%assets\movo.ico" "%DIR%\movo.ico" >nul
if errorlevel 1 goto :fail
copy /y "%ROOT%assets\movolib.ico" "%DIR%\movolib.ico" >nul
if errorlevel 1 goto :fail

echo [2/5] Building movo.exe ^(the compiler is already part of Windows, nothing to install^)...
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo .NET Framework 4 ^(csc.exe^) was not found. It is included in Windows 7/8/10/11 by default.
  goto :fail
)
if exist "%DIR%\movo.exe" del /f /q "%DIR%\movo.exe" >nul 2>&1
"%CSC%" /nologo /optimize+ /codepage:65001 /target:exe /out:"%DIR%\movo.exe" /win32icon:"%DIR%\movo.ico" /r:System.Windows.Forms.dll /r:System.Drawing.dll "%ROOT%src\*.cs" >"%DIR%\build.log" 2>&1
if not exist "%DIR%\movo.exe" (
  "%CSC%" /nologo /optimize+ /codepage:65001 /target:exe /out:"%DIR%\movo.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "%ROOT%src\*.cs" >"%DIR%\build.log" 2>&1
)
if not exist "%DIR%\movo.exe" (
  echo Failed to build movo.exe. Log: %DIR%\build.log
  type "%DIR%\build.log"
  goto :fail
)
echo      Building libraries web and browser into %LIB% ...
if not exist "%LIB%" mkdir "%LIB%"
"%CSC%" /nologo /optimize+ /codepage:65001 /target:library /out:"%LIB%\web.dll" /r:"%DIR%\movo.exe" "%ROOT%libs\Web.cs" >"%DIR%\buildlib.log" 2>&1
"%CSC%" /nologo /optimize+ /codepage:65001 /target:library /out:"%LIB%\browser.dll" /r:"%DIR%\movo.exe" /r:System.Web.Extensions.dll "%ROOT%libs\Browser.cs" >>"%DIR%\buildlib.log" 2>&1
if not exist "%LIB%\web.dll" echo      WARNING: library web was not built. See %DIR%\buildlib.log
if not exist "%LIB%\browser.dll" echo      WARNING: library browser was not built. See %DIR%\buildlib.log

echo [3/5] Setting up file types .mv and .mvl...
reg add "HKCU\Software\Classes\.mv" /ve /d "MovoFile" /f >nul
reg add "HKCU\Software\Classes\MovoFile" /ve /d "Movo Program" /f >nul
reg add "HKCU\Software\Classes\MovoFile\DefaultIcon" /ve /d "%DIR%\movo.ico" /f >nul
reg add "HKCU\Software\Classes\MovoFile\shell\open\command" /ve /d "\"%DIR%\movo.exe\" --pause \"%%1\"" /f >nul
reg add "HKCU\Software\Classes\.mvl" /ve /d "MovoLibFile" /f >nul
reg add "HKCU\Software\Classes\MovoLibFile" /ve /d "Movo Library" /f >nul
reg add "HKCU\Software\Classes\MovoLibFile\DefaultIcon" /ve /d "%DIR%\movolib.ico" /f >nul
reg add "HKCU\Software\Classes\MovoLibFile\shell\open\command" /ve /d "notepad.exe \"%%1\"" /f >nul
if errorlevel 1 goto :fail

echo [4/5] Creating library folder %LIB%...
if not exist "%LIB%" mkdir "%LIB%"
"%DIR%\movo.exe" --init
powershell -NoProfile -Command "$d=$env:DIR; $p=[Environment]::GetEnvironmentVariable('Path','User'); if(-not $p){$p=''}; if(($p -split ';') -notcontains $d){[Environment]::SetEnvironmentVariable('Path',($p.TrimEnd(';')+';'+$d).TrimStart(';'),'User')}" >nul 2>&1

set "ADD=%ROOT%movo-addons"
if not exist "%ADD%\library\Host.mvl" goto :noadd
echo      Installing add-ons Https, Host and .mvs from %ADD% ...
copy /y "%ADD%\library\Https.mvl" "%LIB%\Https.mvl" >nul
copy /y "%ADD%\library\Host.mvl" "%LIB%\Host.mvl" >nul
copy /y "%ADD%\mvs_run.mv" "%DIR%\mvs_run.mv" >nul
> "%DIR%\mvs.bat" echo @"%DIR%\movo.exe" "%DIR%\mvs_run.mv" %%*
reg add "HKCU\Software\Classes\.mvs" /ve /d "MovoHostFile" /f >nul
reg add "HKCU\Software\Classes\MovoHostFile" /ve /d "Movo Host" /f >nul
reg add "HKCU\Software\Classes\MovoHostFile\DefaultIcon" /ve /d "%DIR%\movo.ico" /f >nul
reg add "HKCU\Software\Classes\MovoHostFile\shell\open\command" /ve /d "\"%DIR%\movo.exe\" --pause \"%DIR%\mvs_run.mv\" \"%%1\"" /f >nul
reg add "HKCU\Software\Classes\MovoHostFile\shell\edit\command" /ve /d "notepad.exe \"%%1\"" /f >nul
goto :addok
:noadd
echo      Note: the optional folder movo-addons was not found next to install.bat, so Https, Host and .mvs were not installed.
:addok
echo [5/5] Refreshing icons...
ie4uinit.exe -ClearIconCache >nul 2>&1
ie4uinit.exe -show >nul 2>&1

echo.
echo Done! Movo 1.1 is installed: %DIR%\movo.exe
echo Libraries live in %LIB%  ^(put your own as name.mvl^). Use them with:  put.here NAME
echo Open a NEW command prompt and try:  movo examples\hello.mv
echo If the icon did not change, restart Explorer or the PC.
echo.
pause
exit /b 0

:fail
echo.
echo ERROR during installation. See the messages above.
pause
exit /b 1
