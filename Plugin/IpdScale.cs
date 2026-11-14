using System;
using HarmonyLib;
using UnityEngine;

namespace StrikeOpsXR;

/// <summary>
/// Escalar el rig solo amplía el movimiento de la cabeza: Unity calcula la separación entre ojos
/// (IPD) en espacio de cámara, sin la escala del padre. Sin IPD escalada no hay efecto maqueta.
///
/// URP crea cada ojo con <c>new XRView(proj, view, ...)</c> (XRSystem.BuildView). Este prefijo
/// desplaza la posición del ojo alejándola del centro de la cabeza por el factor WorldScale.
/// </summary>
internal static class IpdScale
{
    internal static Camera Camera;
    internal static float Scale = 1f;

    private static bool logged;

    public static void Install(Harmony harmony)
    {
        try
        {
            var type = AccessTools.TypeByName("UnityEngine.Experimental.Rendering.XRView");
            var ctor = type == null
                ? null
                : AccessTools.Constructor(type,
                    new[] { typeof(Matrix4x4), typeof(Matrix4x4), typeof(Rect), typeof(Mesh), typeof(int) });

            if (ctor == null)
            {
                Plugin.Log.LogWarning("No se encontró XRView(...): la IPD no escalará con WorldScale");
                return;
            }

            harmony.Patch(ctor, prefix: new HarmonyMethod(typeof(IpdScale), nameof(Prefix)));
            Plugin.Log.LogInfo("Parche de IPD instalado");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"No se pudo parchear XRView: {ex.Message}");
        }
    }

    private static void Prefix(ref Matrix4x4 viewMatrix)
    {
        if (Camera == null || Mathf.Approximately(Scale, 1f))
            return;

        var center = Camera.transform.position;
        var eye = (Vector3)viewMatrix.inverse.GetColumn(3);
        var offset = eye - center;

        // Por seguridad: si el ojo no está cerca de la cabeza, la vista no es de esta cámara
        if (offset.sqrMagnitude > 0.25f)
            return;

        var newEye = center + offset * Scale;

        if (!logged)
        {
            logged = true;
            Plugin.Log.LogInfo($"IPD: semiseparación {offset.magnitude:F4} m → {(offset * Scale).magnitude:F3} (escala {Scale:F2})");
        }

        // V' = V · T(eye - newEye)  ⇔  inverse(V') = T(newEye - eye) · inverse(V)
        viewMatrix *= Matrix4x4.Translate(eye - newEye);
    }
}
