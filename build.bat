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
pause
