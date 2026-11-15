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

            var cfg = ReadModConfig();
            var disableVr = Environment.GetCommandLineArgs()
                .Any(a => string.Equals(a, "--disable-vr", StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(Get(cfg, "DisableVR"), "true", StringComparison.OrdinalIgnoreCase);

            if (disableVr)
            {
                // Sin manifiesto, Unity no carga UnityOpenXR.dll en absoluto: el juego arranca 100% plano.
                if (File.Exists(manifest))
                    File.Delete(manifest);
                SetPreInit(dataDir, false);

                Logger.LogWarning("--disable-vr: manifiesto OpenXR eliminado, el juego arrancará sin VR");
                return;
            }

            // Runtime forzado: se fija aquí, antes de que Unity cree el dispositivo Vulkan, para que
            // la preinicialización de OpenXR y el arranque posterior usen el mismo runtime.
            var runtimeJson = Get(cfg, "OpenXRRuntimeJson");
            if (!string.IsNullOrEmpty(runtimeJson))
            {
                Environment.SetEnvironmentVariable("XR_RUNTIME_JSON", runtimeJson);
                Logger.LogInfo($"Runtime OpenXR forzado: {runtimeJson}");
            }

            Directory.CreateDirectory(manifestDir);
            File.WriteAllText(manifest, VR_MANIFEST);

            // Preinicialización: Unity carga UnityOpenXR antes de crear el dispositivo Vulkan y lo crea
            // a través de OpenXR (con las extensiones que pide el runtime). Imprescindible para VDXR.
            SetPreInit(dataDir, true);

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

    private const string PreInitKey = "xrsdk-pre-init-library";
    private const string PreInitLine = PreInitKey + "=UnityOpenXR";

    /// <summary>
    /// Pone o quita xrsdk-pre-init-library en boot.config. Unity lee boot.config al arrancar el proceso,
    /// así que si hay que cambiarlo, el cambio vale para el siguiente arranque.
    /// </summary>
    private static void SetPreInit(string dataDir, bool enable)
    {
        try
        {
            var path = Path.Combine(dataDir, "boot.config");
            if (!File.Exists(path))
                return;

            var lines = File.ReadAllLines(path).ToList();
            var has = lines.Any(l => l.Trim() == PreInitLine);
            if (has == enable && lines.Count(l => l.TrimStart().StartsWith(PreInitKey)) == (enable ? 1 : 0))
            {
                if (enable)
                    Logger.LogInfo("boot.config: preinicialización OpenXR activa");
                return;
            }

            var backup = path + ".strikeopsxr.bak";
            if (!File.Exists(backup))
                File.Copy(path, backup);

            lines.RemoveAll(l => l.TrimStart().StartsWith(PreInitKey));
            if (enable)
                lines.Add(PreInitLine);
            File.WriteAllLines(path, lines);

            Logger.LogWarning(enable
                ? "boot.config: añadida la preinicialización OpenXR. Tendrá efecto en el PRÓXIMO arranque " +
                  "(hace falta para Virtual Desktop / VDXR)"
                : "boot.config: quitada la preinicialización OpenXR");
        }
        catch (Exception ex)
        {
            Logger.LogError($"No se pudo actualizar boot.config: {ex.Message}");
        }
    }

    /// <summary>Lee [General] de BepInEx/config/strikeopsxr.cfg sin depender del plugin.</summary>
    private static Dictionary<string, string> ReadModConfig()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var path = Path.Combine(Paths.ConfigPath, "strikeopsxr.cfg");
            if (!File.Exists(path))
                return d;

            var section = "";
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith("["))
                {
                    section = line.Trim('[', ']');
                    continue;
                }
                if (section != "General" || line.Length == 0 || line.StartsWith("#"))
                    continue;
                var i = line.IndexOf('=');
                if (i > 0)
                    d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"No se pudo leer strikeopsxr.cfg: {ex.Message}");
        }
        return d;
    }

    private static string Get(Dictionary<string, string> d, string key) =>
        d.TryGetValue(key, out var v) ? v : "";

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
