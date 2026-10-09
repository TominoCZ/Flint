@echo off
rem  Publishes Flint for Windows as one self-contained, optimised Flint.exe: the .NET runtime and
rem  every library are bundled inside it, ReadyToRun-compiled for a fast start, with no debug symbols.
rem  The target machine needs nothing installed.
rem
rem  Output: artifacts\win-x64\Flint.exe
rem  Usage:  build-windows.bat [--arch win-arm64] [--clean]
rem  The Linux twin is build-linux.sh; keep the two in step.
setlocal
cd /d "%~dp0"

set "RID=win-x64"
set "CLEAN=false"
:parse
if "%~1"=="" goto parsed
if /i "%~1"=="--arch"  (set "RID=%~2" & shift & shift & goto parse)
if /i "%~1"=="-a"      (set "RID=%~2" & shift & shift & goto parse)
if /i "%~1"=="--clean" (set "CLEAN=true" & shift & goto parse)
echo unknown option: %~1 1>&2
exit /b 2
:parsed

if "%CLEAN%"=="true" if exist artifacts rmdir /s /q artifacts

dotnet publish src\Flint.App\Flint.App.csproj -nologo -v q ^
  -c Release -r %RID% -o artifacts\%RID% ^
  -p:SelfContained=true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:PublishReadyToRun=true ^
  -p:DebugType=none -p:DebugSymbols=false
if errorlevel 1 exit /b %errorlevel%

echo built artifacts\%RID%\Flint.exe
endlocal
