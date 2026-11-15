// Adaptado de RepoXR (https://github.com/DaXcess/RepoXR, Source/OpenXR.cs), GPL-3.0.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace StrikeOpsXR;

internal static class OpenXRBoot
{
    private static XRGeneralSettings generalSettings;
    private static XRManagerSettings managerSettings;
    private static OpenXRLoader loader;

    public static bool Initialize(string runtimeJson)
    {
        var log = Plugin.Log;

        // 1. ¿Cargó Unity el subsistema? (lo instala el preloader)
        var descriptors = new List<ISubsystemDescriptor>();
        SubsystemManager.GetAllSubsystemDescriptors(descriptors);

        if (!descriptors.Any(d => d.id == "OpenXR Display") || !descriptors.Any(d => d.id == "OpenXR Input"))
        {
            log.LogError("Unity no ha registrado el subsistema OpenXR. ¿Está instalado el preloader " +
                         "(BepInEx/patchers/StrikeOpsXR) y se arrancó el juego sin --disable-vr?");
            return false;
        }

        // 2. Runtime a usar
        if (!string.IsNullOrEmpty(runtimeJson))
        {
            log.LogInfo($"Forzando runtime OpenXR: {runtimeJson}");
            Environment.SetEnvironmentVariable("XR_RUNTIME_JSON", runtimeJson);
        }

        // 3. Objetos de XR Management creados en runtime (en un proyecto Unity serían assets)
        generalSettings ??= ScriptableObject.CreateInstance<XRGeneralSettings>();
        managerSettings ??= ScriptableObject.CreateInstance<XRManagerSettings>();
        loader ??= ScriptableObject.CreateInstance<OpenXRLoader>();

        generalSettings.Manager = managerSettings;
        managerSettings.automaticLoading = false;
        managerSettings.automaticRunning = false;

        // TryAddLoader rechaza loaders que no estén en registeredLoaders (que solo se rellena desde el editor).
        // Igual que RepoXR: la lista activa es un List<XRLoader> por debajo, se edita directamente.
        if (managerSettings.activeLoaders is List<XRLoader> loaders)
        {
            loaders.Clear();
            loaders.Add(loader);
        }
        else if (!managerSettings.TrySetLoaders(new List<XRLoader> { loader }))
        {
            log.LogError($"No se pudo registrar OpenXRLoader (activeLoaders es {managerSettings.activeLoaders?.GetType().FullName})");
            return false;
        }

        // 4. Ajustes de OpenXR
        var settings = OpenXRSettings.Instance;
        if (settings == null)
        {
            ScriptableObject.CreateInstance<OpenXRSettings>(); // Awake() lo registra como instancia de runtime
            settings = OpenXRSettings.Instance;
        }

        if (settings == null)
        {
            log.LogError("No se pudo obtener OpenXRSettings");
            return false;
        }

        // Multi-pass por defecto: el juego (solo pantalla) puede no tener variantes de shader para single-pass instanced
        var singlePass = string.Equals(Plugin.Cfg.StereoRenderMode.Value, "SinglePass", StringComparison.OrdinalIgnoreCase);
        settings.renderMode = singlePass ? OpenXRSettings.RenderMode.SinglePassInstanced : OpenXRSettings.RenderMode.MultiPass;
        log.LogInfo($"Modo estéreo: {settings.renderMode}");
        settings.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.None;
        SetInteractionProfiles(settings);

        // 5. Arrancar
        log.LogInfo("Inicializando OpenXR...");
        managerSettings.InitializeLoaderSync();

        if (managerSettings.activeLoader == null)
        {
            log.LogError("OpenXRLoader no se inicializó (revisa el log de Unity: Player.log)");
            return false;
        }

        managerSettings.StartSubsystems();

        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetInstances(displays);

        // La sesión OpenXR pasa a READY/FOCUSED de forma asíncrona (segundos después),
        // así que aquí basta con que exista el display; no se exige que ya esté "running".
        if (displays.Count == 0)
        {
            log.LogError("No se creó ningún display XR");
            return false;
        }

        log.LogInfo($"OpenXR en marcha. Runtime: {OpenXRRuntime.name} {OpenXRRuntime.version}, " +
                    $"API: {SystemInfo.graphicsDeviceType}");
        return true;
    }

    /// <summary>
    /// RepoXR carga sus features desde un AssetBundle hecho en Unity. Aquí se crean en código.
    /// No son necesarios para el tracking del casco; solo para leer los mandos.
    /// </summary>
    private static void SetInteractionProfiles(OpenXRSettings settings)
    {
        try
        {
            var features = new OpenXRFeature[]
            {
                ScriptableObject.CreateInstance<OculusTouchControllerProfile>(),
                ScriptableObject.CreateInstance<MetaQuestTouchProControllerProfile>(),
                ScriptableObject.CreateInstance<ValveIndexControllerProfile>(),
                ScriptableObject.CreateInstance<HTCViveControllerProfile>(),
                ScriptableObject.CreateInstance<MicrosoftMotionControllerProfile>(),
                ScriptableObject.CreateInstance<HPReverbG2ControllerProfile>(),
                ScriptableObject.CreateInstance<KHRSimpleControllerProfile>(),
            };

            foreach (var f in features)
                f.enabled = true;

            // OpenXRSettings.features es interno
            var field = typeof(OpenXRSettings).GetField("features", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                Plugin.Log.LogWarning("No se encontró OpenXRSettings.features; los mandos no tendrán perfiles");
                return;
            }

            field.SetValue(settings, features);
            Plugin.Log.LogInfo($"{features.Length} perfiles de mando registrados");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"No se pudieron crear los perfiles de mando: {ex.Message}");
        }
    }
}
