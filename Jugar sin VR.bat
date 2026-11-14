@echo off
rem Arranca Reflex Unit Strike Ops en el monitor, sin VR (el mod StrikeOpsXR no se activa)
cd /d "%~dp0"
if exist "Strike Ops.exe" (
    start "" "Strike Ops.exe" --disable-vr %*
) else (
    start "" "StrikeOps.exe" --disable-vr %*
)
