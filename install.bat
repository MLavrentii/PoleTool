@echo off
setlocal
rem Builds and installs the Pole Tool as an autoload bundle. Close AutoCAD first.
set "ROOT=%~dp0"
set "ACAD=%~1"
if not defined ACAD set "ACAD=C:\Program Files\Autodesk\AutoCAD 2027"
set "BUILT=%ROOT%bin\Release\net10.0-windows"
set "DEST=%APPDATA%\Autodesk\ApplicationPlugins\PoleTool.bundle"
tasklist /FI "IMAGENAME eq acad.exe" /NH 2>nul | find /I "acad.exe" >nul
if not errorlevel 1 (echo ERROR: close all AutoCAD windows first. & pause & exit /b 1)
dotnet build "%ROOT%PoleTool.csproj" -c Release -p:AcadDir="%ACAD%"
if errorlevel 1 (echo BUILD FAILED - installation not changed. & pause & exit /b 1)
if exist "%DEST%" rmdir /S /Q "%DEST%"
mkdir "%DEST%\Contents"
copy /Y "%ROOT%PackageContents.xml" "%DEST%\" >nul
copy /Y "%BUILT%\PoleTool.dll" "%DEST%\Contents\" >nul
if exist "%BUILT%\PoleTool.pdb" copy /Y "%BUILT%\PoleTool.pdb" "%DEST%\Contents\" >nul
fc /B "%BUILT%\PoleTool.dll" "%DEST%\Contents\PoleTool.dll" >nul
if errorlevel 1 (echo ERROR: copy failed. & pause & exit /b 1)
echo Installed to "%DEST%". Start AutoCAD and type POLETOOL.
pause
