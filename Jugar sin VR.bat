@echo off
rem Arranca Reflex Unit Strike Ops en el monitor, sin VR (el mod StrikeOpsXR no se activa).
rem Quita la preinicializacion OpenXR de boot.config mientras se juega y la vuelve a poner al salir.
cd /d "%~dp0"
set "BOOT=StrikeOps_Data\boot.config"
set "KEY=xrsdk-pre-init-library"

if exist "%BOOT%" (
    findstr /v /b /c:"%KEY%" "%BOOT%" > "%BOOT%.tmp"
    move /y "%BOOT%.tmp" "%BOOT%" > nul
)

set "EXE=StrikeOps.exe"
if exist "Strike Ops.exe" set "EXE=Strike Ops.exe"
start "" /wait "%EXE%" --disable-vr %*

if exist "%BOOT%" (
    findstr /v /b /c:"%KEY%" "%BOOT%" > "%BOOT%.tmp"
    move /y "%BOOT%.tmp" "%BOOT%" > nul
    echo %KEY%=UnityOpenXR>> "%BOOT%"
)
