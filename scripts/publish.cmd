@echo off
rem Avvia publish.ps1 senza modificare i criteri di esecuzione di PowerShell del sistema.
rem Esempi:  scripts\publish        scripts\publish -Arm64 -Zip
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1" %*
set "PUBLISH_EXIT=%ERRORLEVEL%"

rem Con il doppio clic la finestra resterebbe aperta un istante: aspetta un tasto.
echo %CMDCMDLINE% | find /i "/c" >nul && pause
exit /b %PUBLISH_EXIT%
