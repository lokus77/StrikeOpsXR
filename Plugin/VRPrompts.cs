using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Robosaru;
using UnityEngine;

namespace StrikeOpsXR;

/// <summary>
/// Indicaciones de botón en pantalla ("Q aterrizar", "ENTER"...).
/// El juego las dibuja con sprites de DeviceSprites: por cada acción hay un keyboardSprite y un
/// gamePadSprite, y elige según LocalPlayerInput.KeyboardMouse. Como jugamos en modo teclado/ratón
/// (por el apuntado), se sustituye el keyboardSprite de cada acción por el sprite de mando del
/// botón VR que tiene asignado. Los botones del Quest ocupan las mismas posiciones que los de un
/// mando Xbox (A abajo, B derecha, X izquierda, Y arriba), así que valen esos iconos.
/// </summary>
internal static class VRPrompts
{
    // Binding de teclado/ratón del juego → control de gamepad equivalente al botón VR asignado
    private static readonly Dictionary<string, string> KeyToPad = new(StringComparer.OrdinalIgnoreCase)
    {
        ["<Keyboard>/enter"] = "buttonSouth",       // A
        ["<Keyboard>/numpadEnter"] = "buttonSouth",
        ["<Keyboard>/z"] = "buttonEast",            // B
        ["<Keyboard>/q"] = "buttonWest",            // X
        ["<Keyboard>/e"] = "buttonNorth",           // Y
        ["<Keyboard>/t"] = "buttonEast",            // B mantenido
        ["<Keyboard>/rightArrow"] = "rightShoulder", // grip derecho
        ["<Keyboard>/space"] = "leftShoulder",      // grip izquierdo
        ["<Keyboard>/escape"] = "rightStickPress",  // clic stick derecho
        ["<Mouse>/leftButton"] = "rightTrigger",    // gatillo derecho
        ["<Mouse>/rightButton"] = "leftTrigger",    // gatillo izquierdo
    };

    private static bool logged;

    public static void Install(Harmony harmony)
    {
        // RefreshInputActionsSprites recalcula los sprites desde los bindings: volver a aplicar después
        harmony.Patch(AccessTools.Method(typeof(DeviceSprites), nameof(DeviceSprites.RefreshInputActionsSprites)),
            postfix: new HarmonyMethod(typeof(VRPrompts), nameof(Apply)));
    }

    /// <summary>Aplica a todos los DeviceSprites cargados. Devuelve cuántos sprites cambió.</summary>
    public static int ApplyAll()
    {
        var n = 0;
        foreach (var ds in Resources.FindObjectsOfTypeAll<DeviceSprites>())
            n += ApplyTo(ds);
        return n;
    }

    private static void Apply(DeviceSprites __instance) => ApplyTo(__instance);

    private static int ApplyTo(DeviceSprites ds)
    {
        if (ds == null || ds.inputActionUiElements == null)
            return 0;

        if (!logged)
        {
            logged = true;
            Plugin.Log.LogInfo("Sprites de botones: " +
                               string.Join(", ", (ds.inputSprites ?? Array.Empty<Sprite>()).Where(s => s).Select(s => s.name)));
        }

        var changed = 0;
        foreach (var el in ds.inputActionUiElements)
        {
            var action = el?.inputActionReference != null ? el.inputActionReference.action : null;
            if (action == null)
                continue;

            string pad = null;
            foreach (var b in action.bindings)
                if (!b.isComposite && b.effectivePath != null && KeyToPad.TryGetValue(b.effectivePath, out pad))
                    break;

            if (pad == null)
                continue;

            var sprite = FindPadSprite(ds, pad);
            if (sprite == null || el.keyboardSprite == sprite)
                continue;

            Plugin.Log.LogInfo($"Indicación {action.name}: {(el.keyboardSprite ? el.keyboardSprite.name : "-")} → {sprite.name}");
            el.keyboardSprite = sprite;
            changed++;
        }

        if (changed > 0)
        {
            try { ds.RefreshVisualElements(); }
            catch { /* aún sin UI */ }
        }

        return changed;
    }

    private static Sprite FindPadSprite(DeviceSprites ds, string control)
    {
        foreach (var layout in new[] { "Gamepad", "XInputController", "XInputControllerWindows" })
        {
            try
            {
                var s = ds.TryGetControlSprite(layout, control, false);
                if (s != null) return s;
            }
            catch { /* sigue probando */ }
        }

        // Por nombre: "buttonSouth", "Gamepad_buttonSouth"...
        return ds.inputSprites?.FirstOrDefault(s => s && s.name.IndexOf(control, StringComparison.OrdinalIgnoreCase) >= 0)
               ?? ds.extraSprites?.FirstOrDefault(e => e != null && string.Equals(e.controlName, control, StringComparison.OrdinalIgnoreCase))?.sprite;
    }
}
