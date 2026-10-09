@echo off
rem  Publishes Flint for Windows as one optimised Flint.exe: every library is bundled inside it,
rem  ReadyToRun-compiled for a fast start, with no debug symbols. The .NET runtime is left out (it adds 100 MB):
rem  the target machine needs the .NET 10 Runtime, and Flint.exe offers its download when that is missing.
rem
rem  Output: artifacts\win-x64\Flint.exe
rem  Usage:  build-windows.bat [--arch win-arm64] [--clean]
rem  The Linux twin is build-linux.sh; keep the two in step, but for this: Linux bundles the runtime, which a
rem  per-user ~/.dotnet would not be found from the application menu.
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

rem  ReadyToRun keeps its compiled copies in obj\ and does not notice when the runtime they were built against changes
rem  (self-contained or not): a stale copy makes an exe that exits at once without a word. So they are compiled afresh.
set "R2R=src\Flint.App\obj\Release\net10.0\%RID%\R2R"
if exist "%R2R%" rmdir /s /q "%R2R%"

dotnet publish src\Flint.App\Flint.App.csproj -nologo -v q ^
  -c Release -r %RID% -o artifacts\%RID% ^
  -p:SelfContained=false ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:PublishReadyToRun=true ^
  -p:DebugType=none -p:DebugSymbols=false
if errorlevel 1 exit /b %errorlevel%

echo built artifacts\%RID%\Flint.exe
endlocal
