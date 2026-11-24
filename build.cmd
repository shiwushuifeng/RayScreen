@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" echo [!] csc.exe not found & exit /b 1

if not exist "bin" mkdir "bin"
set "ICON="
if exist "assets\app.ico" set "ICON=/win32icon:assets\app.ico"

set COMMON=/nologo /codepage:65001 /utf8output /optimize+ /warn:4 %ICON% /win32manifest:src\app.manifest

echo [1/2] GUI  -^> bin\RayScreen.exe
"%CSC%" %COMMON% /target:winexe /out:bin\RayScreen.exe src\RayScreen.cs /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll
if errorlevel 1 exit /b 1

echo [2/2] CLI  -^> bin\RayScreen-cli.exe
"%CSC%" %COMMON% /define:CLI /target:exe /out:bin\RayScreen-cli.exe src\RayScreen.cs /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll
if errorlevel 1 exit /b 1

echo.
echo Build OK:
dir /b bin
endlocal
