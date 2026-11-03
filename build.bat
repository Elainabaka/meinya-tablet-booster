@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
title Meinya Tablet Booster - build

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" goto thieu_net

echo [Meinya] Đang biên dịch MeinyaTabletBooster.exe ...
echo [Meinya] Building MeinyaTabletBooster.exe ...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if errorlevel 1 goto loi_build
if not exist "%~dp0MeinyaTabletBooster.exe" goto loi_build

echo.
echo [Meinya] Xong. Đang mở app. / Done. Starting the app.
start "" "%~dp0MeinyaTabletBooster.exe"
exit /b 0

:thieu_net
echo.
echo [Meinya] Không tìm thấy trình biên dịch C# của .NET Framework 4.x:
echo     %CSC%
echo Windows 10 và 11 thường có sẵn .NET Framework 4.8 (trình biên dịch đi kèm nó). Cách sửa:
echo   1. Cài .NET Framework 4.8 từ trang Microsoft: https://dotnet.microsoft.com/download/dotnet-framework/net48
echo   2. Hoặc bỏ qua bước build: tải sẵn MeinyaTabletBooster.exe ở mục Releases của repo.
echo.
echo [Meinya] .NET Framework 4.x C# compiler not found. Install .NET Framework 4.8 from Microsoft,
echo or download the ready-made exe from the repo's Releases page.
echo.
pause
exit /b 1

:loi_build
echo.
echo [Meinya] Biên dịch hoặc tự kiểm tra bị lỗi, xem dòng báo lỗi phía trên.
echo [Meinya] Build or self-test failed, see the error lines above.
echo.
pause
exit /b 1
