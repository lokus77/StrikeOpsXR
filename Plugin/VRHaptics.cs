using System;
using HarmonyLib;
using Robosaru;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR.Input;
using XRInputDevice = UnityEngine.XR.InputDevice;

namespace StrikeOpsXR;

/// <summary>
/// Vibración de los mandos VR, si la vibración está activada en las opciones del juego:
///   - arma secundaria (misiles): mando izquierdo, en cada disparo / mientras dispara en ráfaga
///   - impactos recibidos: los dos mandos, más fuerte cuanto más daño
/// </summary>
internal class VRHaptics : MonoBehaviour
{
    private static VRHaptics instance;

    private XRInputDevice left, right;
    private InputAction leftAction, rightAction;   // alternativa vía OpenXRInput
    private bool? legacySupported;

    // Ráfaga del arma secundaria (Fire() arranca un temporizador que dispara hasta Stop())
    private WeaponController burstWeapon;
    private float nextBurstPulse;

    public static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(WeaponController), nameof(WeaponController.Fire)),
            postfix: new HarmonyMethod(typeof(VRHaptics), nameof(FirePostfix)));
        harmony.Patch(AccessTools.Method(typeof(WeaponController), nameof(WeaponController.Stop)),
            postfix: new HarmonyMethod(typeof(VRHaptics), nameof(StopPostfix)));
        harmony.Patch(AccessTools.Method(typeof(NetworkPlayer), nameof(NetworkPlayer.TakeHit)),
            postfix: new HarmonyMethod(typeof(VRHaptics), nameof(TakeHitPostfix)));
    }

    private void Awake()
    {
        instance = this;
        leftAction = new InputAction("HapticLeft", binding: "<XRController>{LeftHand}/haptic");
        rightAction = new InputAction("HapticRight", binding: "<XRController>{RightHand}/haptic");
        leftAction.Enable();
        rightAction.Enable();
    }

    private void OnDestroy()
    {
        leftAction?.Dispose();
        rightAction?.Dispose();
        if (instance == this)
            instance = null;
    }

    private static bool Enabled => Plugin.Cfg.Haptics.Value && LocalPlayerInput.GamePadVibration;

    private static bool IsLocalSecondary(WeaponController w)
    {
        var p = NetworkPlayer.LocalPlayer;
        return p != null && p.vehicle != null && p.vehicle.secondaryWeapon == w;
    }

    private static void FirePostfix(WeaponController __instance)
    {
        try
        {
            if (instance == null || !Enabled || !IsLocalSecondary(__instance))
                return;

            var s = Plugin.Cfg.HapticStrength.Value;
            instance.Pulse(true, false, 0.7f * s, 0.12f);
            if (__instance.continuousFire)
            {
                instance.burstWeapon = __instance;
                instance.nextBurstPulse = Time.time + 0.15f;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Haptics Fire: {ex.Message}");
        }
    }

    private static void StopPostfix(WeaponController __instance)
    {
        if (instance != null && instance.burstWeapon == __instance)
            instance.burstWeapon = null;
    }

    private static void TakeHitPostfix(NetworkPlayer __instance, int damage)
    {
        try
        {
            if (instance == null || !Enabled || __instance != NetworkPlayer.LocalPlayer)
                return;

            // Daño pequeño = toque corto; daño grande = sacudida fuerte
            var k = Mathf.Clamp01(0.4f + damage / 50f);
            var s = Plugin.Cfg.HapticStrength.Value;
            instance.Pulse(true, true, k * s, 0.1f + 0.25f * k);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Haptics TakeHit: {ex.Message}");
        }
    }

    private void Update()
    {
        if (burstWeapon == null)
            return;

        if (!burstWeapon.isActiveAndEnabled || !Enabled)
        {
            burstWeapon = null;
            return;
        }

        if (Time.time >= nextBurstPulse)
        {
            nextBurstPulse = Time.time + 0.15f;
            Pulse(true, false, 0.5f * Plugin.Cfg.HapticStrength.Value, 0.08f);
        }
    }

    private void Pulse(bool leftHand, bool rightHand, float amplitude, float duration)
    {
        amplitude = Mathf.Clamp01(amplitude);
        if (amplitude <= 0f)
            return;

        if (leftHand) Send(ref left, XRNode.LeftHand, leftAction, amplitude, duration);
        if (rightHand) Send(ref right, XRNode.RightHand, rightAction, amplitude, duration);
    }

    private void Send(ref XRInputDevice dev, XRNode node, InputAction action, float amplitude, float duration)
    {
        if (!dev.isValid)
            dev = InputDevices.GetDeviceAtXRNode(node);

        // 1. API XR de Unity. Con OpenXR devuelve false aunque el mando sí vibra,
        //    así que no se mira el resultado.
        if (dev.isValid)
        {
            dev.SendHapticImpulse(0, amplitude, duration);
            if (legacySupported == null)
            {
                legacySupported = true;
                Plugin.Log.LogInfo("Vibración: API XR de Unity");
            }
            return;
        }

        // 2. OpenXR: acción enlazada a la salida háptica del mando
        try
        {
            if (Plugin.Cfg.Diagnostics.Value)
                LogDiagnosticsOnce();
            var device = action.controls.Count > 0 ? action.controls[0].device : null;
            OpenXRInput.SendHapticImpulse(action, amplitude, duration, device);
        }
        catch (Exception ex)
        {
            if (errorsLogged++ < 3)
                Plugin.Log.LogWarning($"Vibración OpenXR: {ex}");
        }
    }

    private int errorsLogged;
    private bool diagnosticsLogged;

    private void LogDiagnosticsOnce()
    {
        if (diagnosticsLogged)
            return;
        diagnosticsLogged = true;

        Plugin.Log.LogInfo($"Vibración: controles enlazados izq={leftAction.controls.Count} der={rightAction.controls.Count}");
        foreach (var d in InputSystem.devices)
        {
            if (d is not UnityEngine.InputSystem.XR.XRController && !(d.layout ?? "").Contains("OpenXR"))
                continue;
            var haptic = d.TryGetChildControl("haptic");
            Plugin.Log.LogInfo($"Vibración: dispositivo {d.layout} '{d.name}' usos [{string.Join(",", d.usages)}] " +
                               $"háptico: {(haptic != null ? haptic.GetType().Name : "no")}");
        }
    }
}
