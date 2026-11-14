using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace StrikeOpsXR;

[BepInPlugin(GUID, NAME, VERSION)]
public class Plugin : BaseUnityPlugin
{
    public const string GUID = "strikeopsxr";
    public const string NAME = "StrikeOpsXR";
    public const string VERSION = "0.1.0";

    internal static Plugin Instance { get; private set; }
    internal static ModConfig Cfg { get; private set; }
    internal static BepInEx.Logging.ManualLogSource Log { get; private set; }

    public static bool VREnabled { get; private set; }

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        Cfg = new ModConfig(Config);

        Log.LogInfo($"{NAME} v{VERSION}");

        var disableVr = Cfg.DisableVR.Value ||
                        Environment.GetCommandLineArgs().Any(a =>
                            string.Equals(a, "--disable-vr", StringComparison.OrdinalIgnoreCase));
        if (disableVr)
        {
            Log.LogWarning("VR desactivado por configuración o --disable-vr");
            return;
        }

        if (!PreloadRuntimeDependencies())
            return;

        // Objeto propio: algunos juegos destruyen el GameObject del manager de BepInEx
        var go = new GameObject("StrikeOpsXR");
        go.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(go);
        go.AddComponent<VRBootstrap>();
    }

    /// <summary>
    /// El chainloader de BepInEx se ejecuta antes de que el juego inicialice el Input System.
    /// Si OpenXR arranca en ese momento, su subsistema de input fuerza la inicialización del
    /// Input System a mitad de carga y Unity se cuelga (InputManager.InitializeData).
    /// Por eso se esperan unos frames: el dispositivo Vulkan ya existe de todos modos.
    /// </summary>
    private class VRBootstrap : MonoBehaviour
    {
        private IEnumerator Start()
        {
            for (var i = 0; i < 3; i++)
                yield return null;

            // Forzar aquí, ya fuera de cualquier callback nativo, por si el juego aún no lo tocó
            _ = InputSystem.settings;

            // Los tipos de Unity.XR.* solo se tocan dentro de StartVR (método aparte, sin inlining),
            // para que no se resuelvan antes de haber cargado RuntimeDeps.
            VREnabled = StartVR();

            if (!VREnabled)
            {
                Log.LogError("No se pudo arrancar OpenXR. El juego sigue en modo plano.");
                Log.LogError("¿Está el casco conectado y el runtime OpenXR (SteamVR / Meta Link / VDXR) en marcha?");
                yield break;
            }

            Instance.SetupVRSession(gameObject);
        }
    }

    private bool PreloadRuntimeDependencies()
    {
        try
        {
            var deps = Path.Combine(Path.GetDirectoryName(Info.Location)!, "RuntimeDeps");
            foreach (var file in Directory.GetFiles(deps, "*.dll"))
            {
                try
                {
                    Assembly.LoadFile(file);
                    Log.LogDebug($"Cargado {Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"No se pudo precargar {Path.GetFileName(file)}: {ex.Message}");
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.LogError($"Falta la carpeta RuntimeDeps junto a StrikeOpsXR.dll: {ex.Message}");
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool StartVR()
    {
        try
        {
            return OpenXRBoot.Initialize(Cfg.OpenXRRuntimeJson.Value);
        }
        catch (Exception ex)
        {
            Log.LogError($"Excepción iniciando OpenXR: {ex}");
            return false;
        }
    }

    private void SetupVRSession(GameObject go)
    {
        XRSettings.eyeTextureResolutionScale = Mathf.Clamp(Cfg.RenderScale.Value, 0.3f, 2f);

        // Que el VR no se pause cuando la ventana de escritorio pierde el foco
        Application.runInBackground = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

        var harmony = new HarmonyLib.Harmony(GUID);
        IpdScale.Install(harmony);
        go.AddComponent<GameConfig>(); // resolución VR desde config.cfg junto al .exe
        go.AddComponent<VRRig>();
        go.AddComponent<Diagnostics>();

        if (Cfg.ControllersEnabled.Value)
        {
            try
            {
                VRControls.Install(harmony);
                VRAim.Install(harmony);
                if (Cfg.VRButtonPrompts.Value)
                    VRPrompts.Install(harmony);
                go.AddComponent<VRControls>();
                go.AddComponent<VRAim>();
            }
            catch (Exception ex)
            {
                Log.LogError($"No se pudieron instalar los controles VR: {ex}");
            }
        }
        if (Cfg.UiEnabled.Value)
            go.AddComponent<UiOverlay>();
        PostFx.Apply();

        SceneManager.sceneLoaded += (_, _) => PostFx.Apply();

        Log.LogInfo("Sesión VR lista");
    }
}
