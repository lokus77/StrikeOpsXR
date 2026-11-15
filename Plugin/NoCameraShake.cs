using HarmonyLib;
using Robosaru;

namespace StrikeOpsXR;

/// <summary>
/// Quita el temblor de cámara del juego (ruido Perlin de Cinemachine) al recibir impactos
/// y al usar el turbo: en VR mueve la vista y marea. La vibración de los mandos se mantiene.
/// </summary>
internal static class NoCameraShake
{
    public static void Install(Harmony harmony)
    {
        if (!Plugin.Cfg.DisableCameraShake.Value)
            return;

        var skip = new HarmonyMethod(typeof(NoCameraShake), nameof(Skip));
        harmony.Patch(AccessTools.Method(typeof(CameraFollow), nameof(CameraFollow.TakeHit)), prefix: skip);
        harmony.Patch(AccessTools.Method(typeof(CameraFollow), nameof(CameraFollow.BoostRumble)), prefix: skip);
        Plugin.Log.LogInfo("Temblor de cámara desactivado (impactos y turbo)");
    }

    private static bool Skip() => false;
}
