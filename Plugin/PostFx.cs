using System;
using Robosaru;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StrikeOpsXR;

/// <summary>
/// Desactiva efectos de post-procesado que en VR marean o se rompen en estéreo.
/// </summary>
internal static class PostFx
{
    public static void Apply()
    {
        try
        {
            var cfg = Plugin.Cfg;

            if (cfg.DisableDepthOfField.Value)
            {
                // Tilt-shift propio del juego (efecto maqueta con desenfoque)
                foreach (var tilt in UnityEngine.Object.FindObjectsOfType<TiltShiftControl>(true))
                    tilt.enabled = false;
            }

            foreach (var volume in UnityEngine.Object.FindObjectsOfType<Volume>(true))
            {
                var profile = volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile;
                if (profile == null)
                    continue;

                if (cfg.DisableDepthOfField.Value)
                    Deactivate<DepthOfField>(profile);

                if (cfg.DisableMotionBlur.Value)
                    Deactivate<MotionBlur>(profile);

                if (cfg.DisableLensEffects.Value)
                {
                    Deactivate<LensDistortion>(profile);
                    Deactivate<ChromaticAberration>(profile);
                    Deactivate<PaniniProjection>(profile);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"PostFx: {ex.Message}");
        }
    }

    private static void Deactivate<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet<T>(out var component) && component.active)
        {
            component.active = false;
            Plugin.Log.LogDebug($"Desactivado {typeof(T).Name} en {profile.name}");
        }
    }
}
