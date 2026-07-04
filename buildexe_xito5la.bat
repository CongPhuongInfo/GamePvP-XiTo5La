@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul

echo ===============================================
echo   Build GamePvP-XiTo5La (Xi To 5 La An Diem)
echo ===============================================

REM ---- Tim vbc.exe qua Release DWORD trong registry (.NET Framework 4.x) ----
set VBC=
for /f "tokens=3" %%A in ('reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release 2^>nul ^| findstr Release') do set RELEASE=%%A

set FRAMEWORK_DIR=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FRAMEWORK_DIR%\vbc.exe" set FRAMEWORK_DIR=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
set VBC=%FRAMEWORK_DIR%\vbc.exe

if not exist "%VBC%" (
    echo [LOI] Khong tim thay vbc.exe tai %VBC%
    echo Hay cai .NET Framework 4.x Developer Pack roi thu lai.
    pause
    exit /b 1
)

echo Dung vbc.exe tai: %VBC%

set OUT=GamePvP-XiTo5La.exe

"%VBC%" /nologo /target:winexe /out:%OUT% ^
    /optimize+ /platform:anycpu ^
    /reference:System.dll ^
    /reference:System.Core.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.Windows.Forms.dll ^
    /imports:System,System.Collections.Generic,System.Linq ^
    Program.vb Form1.vb XiTo5LaGame.vb NetworkHub.vb NetworkPeer.vb

if errorlevel 1 (
    echo.
    echo [LOI] Build that bai. Xem loi phia tren.
    pause
    exit /b 1
)

echo.
echo ===============================================
echo   Build thanh cong: %OUT%
echo   Nho copy thu muc Assets\Cards\ (sprite la bai)
echo   ra cung thu muc voi file .exe truoc khi chay.
echo ===============================================
pause
