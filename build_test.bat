@echo off
REM ============================================================
REM  Legacy quick build helper (ASCII only - do not add CJK).
REM  Prefer build_and_test.bat, which also runs unit tests.
REM ============================================================
echo Building TimeTask (Debug)...

dotnet build TimeTask.csproj --configuration Debug --verbosity minimal

if %ERRORLEVEL% EQU 0 (
    echo Build succeeded.
    echo Open the solution in Visual Studio for further testing.
) else (
    echo Build failed. Check:
    echo   1. NuGet packages restored
    echo   2. .NET Framework 4.7.2 targeting pack installed
    echo   3. Or build with build_and_test.bat ^(MSBuild^)
)

pause
