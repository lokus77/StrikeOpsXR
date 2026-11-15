using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Robosaru;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;
using InputDevice = UnityEngine.XR.InputDevice;

namespace StrikeOpsXR;

/// <summary>
/// Mandos VR → controles del juego.
///  Siempre:
///   - Y (izq.)              = E (mapa)
///   - A (der.)              = Enter
///  Menús:
///   - Stick izq.            = flechas (el InputSystemUIInputModule hace navegación y repetición)
///  Juego:
///   - Stick izq.            = movimiento analógico (inyectado en LocalPlayerInput.movement)
///   - X (izq.)              = Q (aterrizar)
///   - B (der.) corto        = Z (siguiente objetivo); mantener 0,8 s = T (objetivo en el mapa)
///   - Grip der.             = flecha derecha (siempre)
///   - Stick der.            = acercar/alejar y girar la vista (VRRig)
///   - Grip izq.             = Espacio (siempre)
///   - Clic stick der.       = Escape (siempre)
///   - Clic stick izq.       = recentrar (VRRig)
///   - Gatillo der.          = acción PrimaryFire (su tecla/botón de teclado-ratón)
///   - Gatillo izq.          = acción SecondaryFire (misiles)
/// </summary>
internal class VRControls : MonoBehaviour
{
    private const float MoveDeadzone = 0.15f;
    private const float NavPress = 0.6f;
    private const float NavRelease = 0.4f;
    private const float LongPress = 0.8f;   // s para la pulsación larga de B
    private const float TapHold = 0.1f;     // s que se mantiene la tecla de un toque

    private bool bDown, bLongSent, tHeld;
    private float bStart, tapZUntil;

    // Leído por el parche de LocalPlayerInput.Update
    internal static bool MoveActive;
    internal static Vector2 Move;
    private static bool moveWasActive;

    private InputDevice left, right;
    private bool? lastGameplay;

    private readonly HashSet<Key> wantedKeys = new();
    private readonly HashSet<Key> sentKeys = new();
    private readonly HashSet<MouseButton> wantedMouse = new();
    private readonly HashSet<MouseButton> sentMouse = new();

    private bool loggedBindings;
    private float nextPrompts;

    public static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(LocalPlayerInput), "Update"),
            prefix: new HarmonyMethod(typeof(VRControls), nameof(LocalPlayerInputUpdatePrefix)));
        Plugin.Log.LogInfo("Controles VR instalados");
    }

    // ___movement: Harmony da acceso por referencia al campo privado LocalPlayerInput.movement
    private static void LocalPlayerInputUpdatePrefix(ref Vector2 ___movement)
    {
        if (MoveActive)
        {
            ___movement = Move;
            moveWasActive = true;
        }
        else if (moveWasActive)
        {
            ___movement = Vector2.zero;
            moveWasActive = false;
        }
    }

    private void Update()
    {
        if (!left.isValid) left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (!right.isValid) right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        wantedKeys.Clear();
        wantedMouse.Clear();

        // Indicaciones de botón: los DeviceSprites se cargan con las escenas
        if (Plugin.Cfg.VRButtonPrompts.Value && Time.unscaledTime >= nextPrompts)
        {
            nextPrompts = Time.unscaledTime + 2f;
            VRPrompts.ApplyAll();
        }

        var gameplay = NetworkPlayer.LocalPlayer != null && !NetworkSystem.Paused;
        if (gameplay != lastGameplay)
        {
            lastGameplay = gameplay;
            Plugin.Log.LogInfo($"Controles VR: modo {(gameplay ? "juego" : "menú")}");
            if (gameplay)
                LogBindings();
        }

        // Botones comunes
        if (Pressed(left, CommonUsages.secondaryButton)) wantedKeys.Add(Key.E);
        if (Pressed(right, CommonUsages.primaryButton)) wantedKeys.Add(Key.Enter);
        if (Pressed(right, CommonUsages.gripButton)) wantedKeys.Add(Key.RightArrow);          // grip der.

        // B: pulsación corta = Z (siguiente objetivo, en partida), al soltar.
        //    Mantener 0,8 s = T (cambiar objetivo en el mapa, siempre), y entonces no se manda Z.
        var b = Pressed(right, CommonUsages.secondaryButton);
        var now = Time.unscaledTime;
        if (b && !bDown)
        {
            bStart = now;
            bLongSent = false;
        }
        if (b && !bLongSent && now - bStart >= LongPress)
        {
            // El juego actúa al pulsar Y al soltar T, así que cada pulsación larga solo cambia
            // el estado de T (pulsa o suelta), para que haya una sola acción por pulsación larga.
            bLongSent = true;
            tHeld = !tHeld;
        }
        if (!b && bDown && !bLongSent && gameplay)
            tapZUntil = now + TapHold;
        bDown = b;

        if (tHeld) wantedKeys.Add(Key.T);
        if (now < tapZUntil) wantedKeys.Add(Key.Z);

        if (Pressed(left, CommonUsages.gripButton)) wantedKeys.Add(Key.Space);                // grip izq.
        if (Pressed(right, CommonUsages.primary2DAxisClick)) wantedKeys.Add(Key.Escape);      // clic stick der.

        var stick = Axis(left);

        if (gameplay)
        {
            var mag = stick.magnitude;
            MoveActive = mag > MoveDeadzone;
            Move = MoveActive
                ? stick.normalized * Mathf.Min(1f, (mag - MoveDeadzone) / (1f - MoveDeadzone))
                : Vector2.zero;

            // La vista VR puede estar girada respecto a la cámara del juego (grip der. + stick der.):
            // girar el stick lo mismo para que "adelante" sea adelante en lo que ves
            if (MoveActive && Mathf.Abs(VRRig.AppliedYaw) > 0.5f)
            {
                var v = Quaternion.Euler(0f, VRRig.AppliedYaw, 0f) * new Vector3(Move.x, 0f, Move.y);
                Move = new Vector2(v.x, v.z);
            }

            if (Pressed(left, CommonUsages.primaryButton)) wantedKeys.Add(Key.Q);    // X
            if (Pressed(right, CommonUsages.triggerButton)) PressAction("PrimaryFire", MouseButton.Left);
            if (Pressed(left, CommonUsages.triggerButton)) PressAction("SecondaryFire", MouseButton.Right);
        }
        else
        {
            MoveActive = false;
            SetNav(Key.UpArrow, stick.y);
            SetNav(Key.DownArrow, -stick.y);
            SetNav(Key.RightArrow, stick.x);
            SetNav(Key.LeftArrow, -stick.x);
        }

        SendKeyboard();
        SendMouse();
    }

    private static bool Pressed(InputDevice dev, InputFeatureUsage<bool> usage) =>
        dev.isValid && dev.TryGetFeatureValue(usage, out var v) && v;

    private static Vector2 Axis(InputDevice dev) =>
        dev.isValid && dev.TryGetFeatureValue(CommonUsages.primary2DAxis, out var v) ? v : Vector2.zero;

    private void SetNav(Key key, float value)
    {
        var down = sentKeys.Contains(key);
        if (down ? value > NavRelease : value > NavPress)
            wantedKeys.Add(key);
    }

    // ---- acciones del juego → su control de teclado/ratón ----

    private readonly Dictionary<string, ButtonControl> actionControls = new();
    private float nextResolve;

    private void PressAction(string actionName, MouseButton fallback)
    {
        var control = ResolveAction(actionName);
        if (control is KeyControl key)
            wantedKeys.Add(key.keyCode);
        else if (control != null && control.device is Mouse && TryMouseButton(control, out var mb))
            wantedMouse.Add(mb);
        else
            wantedMouse.Add(fallback);
    }

    private ButtonControl ResolveAction(string actionName)
    {
        if (Time.unscaledTime >= nextResolve)
        {
            actionControls.Clear();
            nextResolve = Time.unscaledTime + 2f;
        }

        if (actionControls.TryGetValue(actionName, out var cached))
            return cached;

        ButtonControl found = null;
        foreach (var pi in PlayerInput.all)
        {
            var action = pi.actions?.FindAction(actionName);
            if (action == null) continue;
            found = action.controls.OfType<ButtonControl>()
                .FirstOrDefault(c => c.device is Keyboard || c.device is Mouse);
            if (found != null) break;
        }

        actionControls[actionName] = found;
        return found;
    }

    private static bool TryMouseButton(InputControl c, out MouseButton mb)
    {
        switch (c.name)
        {
            case "leftButton": mb = MouseButton.Left; return true;
            case "rightButton": mb = MouseButton.Right; return true;
            case "middleButton": mb = MouseButton.Middle; return true;
            case "forwardButton": mb = MouseButton.Forward; return true;
            case "backButton": mb = MouseButton.Back; return true;
            default: mb = default; return false;
        }
    }

    private void LogBindings()
    {
        if (loggedBindings || !Plugin.Cfg.Diagnostics.Value) return;
        loggedBindings = true;

        foreach (var pi in PlayerInput.all)
        {
            if (pi.actions == null) continue;
            Plugin.Log.LogInfo($"PlayerInput {pi.name}: esquema {pi.currentControlScheme}, mapa {pi.currentActionMap?.name}");
            foreach (var action in pi.actions)
            {
                var b = string.Join(", ", action.bindings.Where(x => !x.isComposite).Select(x => x.effectivePath));
                Plugin.Log.LogInfo($"  {action.actionMap.name}/{action.name}: {b}");
            }
        }
    }

    // ---- inyección ----

    private void SendKeyboard()
    {
        if (wantedKeys.SetEquals(sentKeys))
            return;

        var kb = Keyboard.current;
        if (kb == null)
            return;

        var state = new KeyboardState();
        foreach (var key in kb.allKeys)
            if (key != null && key.isPressed && !sentKeys.Contains(key.keyCode))
                state.Set(key.keyCode, true); // teclas reales pulsadas
        foreach (var k in wantedKeys)
            state.Set(k, true);

        sentKeys.Clear();
        sentKeys.UnionWith(wantedKeys);
        InputSystem.QueueStateEvent(kb, state);
    }

    /// <summary>
    /// Posición de ratón deseada por el apuntado VR (VRAim), en píxeles con origen abajo a la izquierda.
    /// Se inyecta como estado del ratón en el Input System: no depende de que la ventana tenga el foco
    /// (WarpCursorPosition sí, y con el casco puesto la ventana no suele tenerlo).
    /// </summary>
    internal static Vector2? AimPosition;
    private Vector2 sentPosition = new(-1, -1);

    private void SendMouse()
    {
        var aim = AimPosition;
        var moveAim = aim.HasValue && (aim.Value - sentPosition).sqrMagnitude >= 0.25f;

        if (wantedMouse.SetEquals(sentMouse) && !moveAim)
            return;

        var mouse = Mouse.current;
        if (mouse == null)
            return;

        var pos = aim ?? mouse.position.ReadValue();
        var state = new MouseState { position = pos, delta = aim.HasValue ? pos - mouse.position.ReadValue() : Vector2.zero };
        sentPosition = pos;
        void Keep(ButtonControl b, MouseButton mb)
        {
            var real = b.isPressed && !sentMouse.Contains(mb);
            state = state.WithButton(mb, real || wantedMouse.Contains(mb));
        }
        Keep(mouse.leftButton, MouseButton.Left);
        Keep(mouse.rightButton, MouseButton.Right);
        Keep(mouse.middleButton, MouseButton.Middle);
        Keep(mouse.forwardButton, MouseButton.Forward);
        Keep(mouse.backButton, MouseButton.Back);

        sentMouse.Clear();
        sentMouse.UnionWith(wantedMouse);
        InputSystem.QueueStateEvent(mouse, state);
    }
}
