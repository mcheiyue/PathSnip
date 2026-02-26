@echo off
chcp 65001 >nul

echo ========================================
echo PathSnip Build Script
echo ========================================
echo.

echo [1/2] Killing PathSnip process...
taskkill /F /IM PathSnip.exe 2>nul
echo Done

echo.
echo [2/2] Building project...
dotnet build "d:\github\PathSnip\src\PathSnip\PathSnip.csproj" -c Release

if %errorlevel% equ 0 (
    echo.
    echo ========================================
    echo Build SUCCESS!
    echo ========================================
) else (
    echo.
    echo ========================================
    echo Build FAILED!
    echo ========================================
    exit /b 1
)
