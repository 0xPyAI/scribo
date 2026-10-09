@echo off
setlocal

:: 1. Direct executable (in release folder or root)
if exist "%~dp0Scribo.exe" (
    start "" "%~dp0Scribo.exe"
    exit /b
)

:: 2. Published release binary
if exist "%~dp0bin\Release\net9.0-windows\win-x64\publish\Scribo.exe" (
    start "" "%~dp0bin\Release\net9.0-windows\win-x64\publish\Scribo.exe"
    exit /b
)

:: 3. Standard release build
if exist "%~dp0bin\Release\net9.0-windows\Scribo.exe" (
    start "" "%~dp0bin\Release\net9.0-windows\Scribo.exe"
    exit /b
)

:: 4. Standard debug build
if exist "%~dp0bin\Debug\net9.0-windows\Scribo.exe" (
    start "" "%~dp0bin\Debug\net9.0-windows\Scribo.exe"
    exit /b
)

:: 5. Fallback to compiling and running via .NET CLI
start dotnet run
