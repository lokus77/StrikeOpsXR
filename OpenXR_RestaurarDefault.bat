@echo off
echo Vuelve al runtime OpenXR por defecto del sistema en la config de StrikeOpsXR
set "CFG=%~dp0BepInEx\config\strikeopsxr.cfg"
if not exist "%CFG%" (
    echo No se encuentra "%CFG%". Arranca el juego una vez con el mod para que se cree.
    pause
    exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -Command "$p=$env:CFG; $t=[IO.File]::ReadAllText($p); if ($t -notmatch '(?m)^OpenXRRuntimeJson =') { Write-Host 'No esta la linea OpenXRRuntimeJson en la config.'; exit 1 }; $t=$t -replace '(?m)^OpenXRRuntimeJson =[^\r\n]*','OpenXRRuntimeJson = '; [IO.File]::WriteAllText($p,$t); Write-Host 'Hecho: OpenXR por defecto del sistema.'"
pause
