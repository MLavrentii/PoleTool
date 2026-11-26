@echo off
rem Build the Pole Tool (.NET 10 SDK). If AutoCAD is elsewhere: build.bat "D:\Autodesk\AutoCAD 2027"
set ACAD=%~1
if "%ACAD%"=="" set ACAD=C:\Program Files\Autodesk\AutoCAD 2027
dotnet build "%~dp0PoleTool.csproj" -c Release -p:AcadDir="%ACAD%"
if errorlevel 1 (echo. & echo BUILD FAILED - copy the complete error list for review. & pause & exit /b 1)
echo.
echo Built: %~dp0bin\Release\net10.0-windows\PoleTool.dll
echo Run install.bat (AutoCAD closed) or NETLOAD the DLL, then type POLETOOL.
pause
