@echo off
REM ============================================================
REM  TimeTask build + test helper  (ASCII only - do not add CJK)
REM    1) Build main project  (Debug)
REM    2) Build test project  (needs .NET SDK on PATH)
REM    3) Run unit tests via vstest
REM  Full log on failure; ends with pause.
REM ============================================================
setlocal
set "DOTNET_ROOT=C:\Program Files\dotnet"
set "DOTNET_HOST_PATH=C:\Program Files\dotnet\dotnet.exe"
set "PATH=C:\Program Files\dotnet;%PATH%"
cd /d "%~dp0"

set "MSBUILD="
if not defined MSBUILD if exist "D:\tools\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=D:\tools\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD if exist "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD if exist "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD if exist "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD if exist "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD (
  echo [ERROR] MSBuild.exe not found. Open TimeTask.sln in Visual Studio and build there.
  pause
  exit /b 1
)

echo [1/3] Building main project...
"%MSBUILD%" TimeTask.csproj /p:Configuration=Debug /v:m /nologo
if errorlevel 1 goto :fail

echo.
echo [2/3] Building test project...
"%MSBUILD%" TimeTask.Tests\TimeTask.Tests.csproj /p:Configuration=Debug /v:m /nologo
if errorlevel 1 goto :fail

set "VSTEST="
if not defined VSTEST if exist "D:\tools\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" set "VSTEST=D:\tools\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe"
if not defined VSTEST if exist "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" set "VSTEST=C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe"
if not defined VSTEST if exist "C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" set "VSTEST=C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\Extensions\TestPlatform\vstest.console.exe"
if not defined VSTEST (
  echo [WARN] vstest.console.exe not found - skipping tests.
  goto :done
)

echo.
echo [3/3] Running unit tests...
"%VSTEST%" "TimeTask.Tests\bin\Debug\net472\TimeTask.Tests.dll" /Logger:console
if errorlevel 1 goto :fail

:done
echo.
echo [OK] Build + tests finished.
pause
exit /b 0

:fail
echo.
echo [FAIL] Something failed - see output above.
pause
exit /b 1
