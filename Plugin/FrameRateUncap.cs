using HarmonyLib;
using Robosaru;
using UnityEngine;

namespace StrikeOpsXR;

/// <summary>
/// El juego limita los FPS según su opción "FrameRate" (VSync del monitor, Max 30/60/90...).
/// En VR el ritmo lo marca el casco: ese límite del monitor solo quita FPS. Se quita al arrancar
/// y cada vez que el juego vuelve a aplicar la opción.
/// </summary>
internal static class FrameRateUncap
{
    public static void Install(Harmony harmony)
    {
        if (!Plugin.Cfg.UncapFrameRate.Value)
            return;

        Plugin.Log.LogInfo($"Límite de FPS del juego: vSync {QualitySettings.vSyncCount}, " +
                           $"targetFrameRate {Application.targetFrameRate} -> sin límite (lo marca el casco)");
        Apply();
        harmony.Patch(AccessTools.Method(typeof(Settings), "InitFrameRate"),
            postfix: new HarmonyMethod(typeof(FrameRateUncap), nameof(Apply)));
    }

    private static void Apply()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
    }
}
