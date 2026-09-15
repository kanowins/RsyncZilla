@echo off
echo ========================================================
echo   Compilando y Publicando RsyncZilla (Release win-x64)
echo ========================================================
dotnet publish src\RsyncZilla\RsyncZilla.csproj -c Release -r win-x64 --self-contained false -o dist\RsyncZilla
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] La compilacion ha fallado.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ========================================================
echo   Compilacion finalizada con exito!
echo   Ejecutable disponible en: dist\RsyncZilla\RsyncZilla.exe
echo ========================================================

set ISCC="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not exist %ISCC% set ISCC="C:\Program Files\Inno Setup 6\ISCC.exe"
if not exist %ISCC% set ISCC="%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
where iscc >nul 2>nul
if %ERRORLEVEL% EQU 0 set ISCC=iscc

if exist %ISCC% (
    echo.
    echo ========================================================
    echo   Compilando Instalador Inno Setup...
    echo ========================================================
    if not exist dist\installer mkdir dist\installer
    if exist "%TEMP%\rsynczilla_build" rmdir /s /q "%TEMP%\rsynczilla_build"
    %ISCC% /O"%TEMP%\rsynczilla_build" installer\RsyncZilla.iss
    if not errorlevel 1 (
        copy /y "%TEMP%\rsynczilla_build\*.exe" dist\installer\ >nul
        rmdir /s /q "%TEMP%\rsynczilla_build"
        echo [OK] Instalador generado en dist\installer\
    ) else (
        echo [ERROR] Fallo al compilar el instalador Inno Setup.
    )
)

pause
