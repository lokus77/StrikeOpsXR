using HarmonyLib;

namespace StrikeOpsXR;

/// <summary>
/// Los menús del juego (p. ej. "¿Seleccionar vehículo?", pausa) ponen detrás un BlurredBackground
/// (plugin Kamgam): una malla con la imagen de la pantalla plana desenfocada. En el panel VR se ve como
/// un recuadro gris opaco que tapa el mundo. Se anula solo su dibujo; el diálogo y sus botones siguen.
/// </summary>
internal static class NoBlurredBackground
{
    public static void Install(Harmony harmony)
    {
        if (!Plugin.Cfg.UiDisableBlurredBackground.Value)
            return;

        var type = AccessTools.TypeByName("Kamgam.UIToolkitBlurredBackground.BlurredBackground");
        var method = type != null ? AccessTools.Method(type, "OnGenerateVisualContent") : null;
        if (method == null)
        {
            Plugin.Log.LogWarning("No se encontró BlurredBackground.OnGenerateVisualContent: el fondo desenfocado de los menús se mantiene");
            return;
        }

        harmony.Patch(method, prefix: new HarmonyMethod(typeof(NoBlurredBackground), nameof(Skip)));
        Plugin.Log.LogInfo("Fondo desenfocado de los menús desactivado");
    }

    private static bool Skip() => false;
}
