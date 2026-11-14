// Basado en RepoXR.Preload (https://github.com/DaXcess/RepoXR), GPL-3.0.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace StrikeOpsXR.Preload;

/// <summary>
/// Patcher de BepInEx 5. No parchea ningún ensamblado: solo aprovecha que se ejecuta
/// antes de que Unity descubra los subsistemas y cree el dispositivo Vulkan, para colocar
/// UnityOpenXR.dll + openxr_loader.dll y el manifiesto del subsistema OpenXR.
/// </summary>
public static class Preload
{
    public static IEnumerable<string> TargetDLLs { get; } = Array.Empty<string>();

    private const string VR_MANIFEST = """
                                       {
                                         "name": "OpenXR XR Plugin",
                                         "version": "1.10.0",
                                         "libraryName": "UnityOpenXR",
                                         "displays": [
                                           {
                                             "id": "OpenXR Display"
                                           }
                                         ],
                                         "inputs": [
                                           {
                                             "id": "OpenXR Input"
                                           }
                                         ]
                                       }
                                       """;

    private static readonly string[] NativeFiles = ["UnityOpenXR.dll", "openxr_loader.dll"];

    private static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource("StrikeOpsXR.Preload");

    public static void Initialize()
    {
        try
        {
            var dataDir = Path.Combine(Paths.GameRootPath, Paths.ProcessName + "_Data");
            var manifestDir = Path.Combine(dataDir, "UnitySubsystems", "UnityOpenXR");
            var manifest = Path.Combine(manifestDir, "UnitySubsystemsManifest.json");

            var disableVr = Environment.GetCommandLineArgs()
                .Any(a => string.Equals(a, "--disable-vr", StringComparison.OrdinalIgnoreCase));

            if (disableVr)
            {
                // Sin manifiesto, Unity no carga UnityOpenXR.dll en absoluto: el juego arranca 100% plano.
                if (File.Exists(manifest))
                    File.Delete(manifest);

                Logger.LogWarning("--disable-vr: manifiesto OpenXR eliminado, el juego arrancará sin VR");
                return;
            }

            Directory.CreateDirectory(manifestDir);
            File.WriteAllText(manifest, VR_MANIFEST);

            // El juego guarda sus plugins nativos en Plugins/x86_64
            var pluginsDir = Path.Combine(dataDir, "Plugins", "x86_64");
            Directory.CreateDirectory(pluginsDir);

            var here = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            foreach (var file in NativeFiles)
            {
                var src = Path.Combine(here, "RuntimeDeps", file);
                var dst = Path.Combine(pluginsDir, file);

                if (!File.Exists(src))
                {
                    Logger.LogError($"Falta {src}. La instalación de StrikeOpsXR está incompleta.");
                    continue;
                }

                if (File.Exists(dst) && SameFile(src, dst))
                    continue;

                File.Copy(src, dst, true);
                Logger.LogInfo($"Copiado {file} -> {dst}");
            }

            Logger.LogInfo("Assets de runtime OpenXR listos");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error preparando OpenXR: {ex}");
        }
    }

    private static bool SameFile(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        return fa.Length == fb.Length && File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b));
    }

    public static void Patch(AssemblyDefinition assembly)
    {
        // No-op
    }
}
