@echo off
cd /d "%~dp0"
taskkill /IM Okiro.exe /F >nul 2>&1
if exist logo.ico (
    "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /out:"Okiro.exe" /win:"logo.ico" /r:System.Windows.Forms.dll /r:System.Drawing.dll "Okiro.cs"
) else (
    "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /out:"Okiro.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "Okiro.cs"
)
if %errorlevel% equ 0 (
    start "" "Okiro.exe"
) else (
    echo.
    echo Compilation failed! Check the error message above.
    pause
)
