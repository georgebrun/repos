@echo off
echo ============================================================
echo  Breakers of E v2 — Build and Publish
echo ============================================================
echo.

set APP_PROJECT=..\BreakersOfE_v2\BreakersOfE\BreakersOfE.csproj
set AGENT_PROJECT=..\BreakersOfE_v2\BreakersOfE.Agent\BreakersOfE.Agent.csproj
set OUT_X64=publish\v2\x64
set OUT_X64_AGENT=publish\v2\x64\Agent
set OUT_X86=publish\v2\x86
set OUT_X86_AGENT=publish\v2\x86\Agent

echo [1/4] Publishing App x64...
dotnet publish %APP_PROJECT% ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=false ^
  -o %OUT_X64%

if %ERRORLEVEL% neq 0 (
    echo ERROR: App x64 publish failed.
    pause
    exit /b 1
)

echo.
echo [2/4] Publishing Agent x64...
dotnet publish %AGENT_PROJECT% ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=false ^
  -o %OUT_X64_AGENT%

if %ERRORLEVEL% neq 0 (
    echo ERROR: Agent x64 publish failed.
    pause
    exit /b 1
)

echo.
echo [3/4] Publishing App x86...
dotnet publish %APP_PROJECT% ^
  -c Release ^
  -r win-x86 ^
  --self-contained true ^
  -p:PublishSingleFile=false ^
  -o %OUT_X86%

if %ERRORLEVEL% neq 0 (
    echo ERROR: App x86 publish failed.
    pause
    exit /b 1
)

echo.
echo [4/4] Publishing Agent x86...
dotnet publish %AGENT_PROJECT% ^
  -c Release ^
  -r win-x86 ^
  --self-contained true ^
  -p:PublishSingleFile=false ^
  -o %OUT_X86_AGENT%

if %ERRORLEVEL% neq 0 (
    echo ERROR: Agent x86 publish failed.
    pause
    exit /b 1
)

echo.
echo ============================================================
echo  Publish complete!
echo  Now open BreakersOfE_Setup_v2.iss in Inno Setup and compile.
echo ============================================================
pause
