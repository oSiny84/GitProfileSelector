@echo off
cd /d "%~dp0"
if exist "dist\GitOwnerSelector\GitOwnerSelector.exe" (
    start "" "dist\GitOwnerSelector\GitOwnerSelector.exe"
) else (
    dotnet run --project "src\GitOwnerSelector\GitOwnerSelector.csproj" -c Release
    if errorlevel 1 pause
)
