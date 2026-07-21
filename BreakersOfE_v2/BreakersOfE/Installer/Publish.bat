@echo off
echo ============================================================
echo  Breakers of E — Build and Publish
echo ============================================================
echo.

set PROJECT=..\BreakersOfE\BreakersOfE.csproj
set OUT_X64=publish\x64
set OUT_X86=publish\x86

echo [1/2] Publishing x64...
dotnet publish %PROJECT% ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=false ^
  -o %OUT_X64%

if %ERRORLEVEL% neq 0 (
    echo ERROR: x64 publish failed.
    pause
    exit /b 1
)

echo.
echo [2/2] Publishing x86...
dotnet publish %PROJECT% ^
  -c Release ^
  -r win-x86 ^
  --self-contained true ^
  -p:PublishSingleFile=false ^
  -o %OUT_X86%

if %ERRORLEVEL% neq 0 (
    echo ERROR: x86 publish failed.
    pause
    exit /b 1
)

echo.
echo ============================================================
echo  Publish complete!
echo  Now open BreakersOfE_Setup.iss in Inno Setup and compile.
echo ============================================================
pause