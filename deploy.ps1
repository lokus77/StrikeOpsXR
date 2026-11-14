<#
  Compila StrikeOpsXR y lo instala en el juego (requiere BepInEx 5 x64 ya instalado).

    .\deploy.ps1                         # compila (Release) e instala
    .\deploy.ps1 -Configuration Debug
    .\deploy.ps1 -GamePath "D:\juegos\Reflex.Unit.Strike.Ops"
    .\deploy.ps1 -Uninstall              # quita el mod y los archivos OpenXR del juego
#>
param(
    [string]$GamePath = "C:\juegos\Reflex.Unit.Strike.Ops",
    [string]$Configuration = "Release",
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

$bepinex   = Join-Path $GamePath "BepInEx"
$plugDir   = Join-Path $bepinex "plugins\StrikeOpsXR"
$patchDir  = Join-Path $bepinex "patchers\StrikeOpsXR"
$dataDir   = Join-Path $GamePath "StrikeOps_Data"
$subsysDir = Join-Path $dataDir "UnitySubsystems\UnityOpenXR"
$nativeDir = Join-Path $dataDir "Plugins\x86_64"

if (-not (Test-Path (Join-Path $GamePath "StrikeOps.exe"))) {
    throw "No encuentro StrikeOps.exe en $GamePath"
}

if ($Uninstall) {
    foreach ($p in $plugDir, $patchDir, $subsysDir,
                   (Join-Path $nativeDir "UnityOpenXR.dll"), (Join-Path $nativeDir "openxr_loader.dll")) {
        if (Test-Path $p) { Remove-Item $p -Recurse -Force -Confirm:$false; Write-Host "Eliminado $p" }
    }
    Write-Host "StrikeOpsXR desinstalado (BepInEx se queda)." -ForegroundColor Green
    return
}

if (-not (Test-Path (Join-Path $bepinex "core\BepInEx.dll"))) {
    throw "BepInEx 5 no está instalado en $GamePath. Instala BepInEx 5.4.x (win x64) y arranca el juego una vez."
}

Write-Host "Compilando ($Configuration)..."
dotnet build (Join-Path $root "StrikeOpsXR.sln") -c $Configuration -p:GamePath="$GamePath" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "La compilación falló" }

$bin = Join-Path $root "bin\$Configuration"

New-Item -ItemType Directory -Force (Join-Path $plugDir "RuntimeDeps")  | Out-Null
New-Item -ItemType Directory -Force (Join-Path $patchDir "RuntimeDeps") | Out-Null

Copy-Item (Join-Path $bin "StrikeOpsXR\StrikeOpsXR.dll") $plugDir -Force
Copy-Item (Join-Path $root "lib\managed\*.dll") (Join-Path $plugDir "RuntimeDeps") -Force

Copy-Item (Join-Path $bin "StrikeOpsXR.Preload\StrikeOpsXR.Preload.dll") $patchDir -Force
Copy-Item (Join-Path $root "lib\native\*.dll") (Join-Path $patchDir "RuntimeDeps") -Force

Write-Host "Instalado en $GamePath" -ForegroundColor Green
Write-Host "  $plugDir"
Write-Host "  $patchDir"
