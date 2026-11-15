@echo off
echo Fuerza el runtime OpenXR de Virtual Desktop (VDXR) en la config de StrikeOpsXR
set "CFG=%~dp0BepInEx\config\strikeopsxr.cfg"
if not exist "%CFG%" (
    echo No se encuentra "%CFG%". Arranca el juego una vez con el mod para que se cree.
    pause
    exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -Command "$p=$env:CFG; $t=[IO.File]::ReadAllText($p); if ($t -notmatch '(?m)^OpenXRRuntimeJson =') { Write-Host 'No esta la linea OpenXRRuntimeJson en la config.'; exit 1 }; $t=$t -replace '(?m)^OpenXRRuntimeJson =[^\r\n]*','OpenXRRuntimeJson = C:\Program Files\Virtual Desktop Streamer\OpenXR\virtualdesktop-openxr.json'; [IO.File]::WriteAllText($p,$t); Write-Host 'Hecho: OpenXR forzado a Virtual Desktop (VDXR).'"
pause
