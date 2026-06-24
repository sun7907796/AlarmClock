@echo off
chcp 65001 >nul
cd /d "%~dp0"
set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
echo ============================================
echo   桌面鬧鐘 - 重新編譯
echo ============================================
echo.
if not exist "%CSC%" ( echo [錯誤] 找不到編譯器 %CSC% & pause & exit /b 1 )
echo 關閉執行中的程式（若有）...
taskkill /IM AlarmClock.exe /F >nul 2>&1
echo 編譯中 ...
"%CSC%" /nologo /target:winexe /win32icon:clock.ico /out:AlarmClock.exe /reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /optimize+ AlarmClock.cs
if errorlevel 1 ( echo. & echo [失敗] 編譯錯誤，exe 未更新。 & echo. & pause & exit /b 1 )
echo.
echo [成功] 已更新 AlarmClock.exe，啟動中 ...
start "" "AlarmClock.exe"
echo.
echo 完成，可關閉此視窗。
pause
