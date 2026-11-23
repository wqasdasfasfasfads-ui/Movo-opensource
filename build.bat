@echo off
rem Builds Movo from source into the "build" folder (no installation, no registry changes).
rem Needs only the C# compiler that ships with Windows (.NET Framework 4).
setlocal
set "ROOT=%~dp0"
set "OUT=%ROOT%build"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo .NET Framework 4 ^(csc.exe^) was not found.
  exit /b 1
)
if not exist "%OUT%" mkdir "%OUT%"
if not exist "%OUT%\library" mkdir "%OUT%\library"

echo Building movo.exe ...
"%CSC%" /nologo /optimize+ /codepage:65001 /target:exe /out:"%OUT%\movo.exe" /win32icon:"%ROOT%assets\movo.ico" /r:System.Windows.Forms.dll /r:System.Drawing.dll "%ROOT%src\*.cs"
if errorlevel 1 exit /b 1

echo Building library web ...
"%CSC%" /nologo /optimize+ /codepage:65001 /target:library /out:"%OUT%\library\web.dll" /r:"%OUT%\movo.exe" "%ROOT%libs\Web.cs"
if errorlevel 1 exit /b 1

echo Building library browser ...
"%CSC%" /nologo /optimize+ /codepage:65001 /target:library /out:"%OUT%\library\browser.dll" /r:"%OUT%\movo.exe" /r:System.Web.Extensions.dll "%ROOT%libs\Browser.cs"
if errorlevel 1 exit /b 1

echo.
echo Done. Run:  build\movo.exe examples\hello.mv
echo Libraries were built into build\library ^(set MOVO_LIB=%OUT%\library to use them from this build^).
exit /b 0
