using System;
using System.Linq;
using Robosaru;
using UnityEngine;

namespace StrikeOpsXR;

/// <summary>
/// Cada 5 s en partida: dónde están la cámara del juego, el jugador y los oyentes de audio
/// (AudioListener de Unity y StudioListener de FMOD), y qué cámara virtual de Cinemachine manda.
/// </summary>
internal class Diagnostics : MonoBehaviour
{
    private float next;
    private static Type fmodListenerType, brainType;

    private void Start()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            fmodListenerType ??= asm.GetType("FMODUnity.StudioListener");
            brainType ??= asm.GetType("Cinemachine.CinemachineBrain");
        }
    }

    private void Update()
    {
        if (!Plugin.Cfg.Diagnostics.Value || Time.unscaledTime < next)
            return;
        next = Time.unscaledTime + 5f;

        var player = NetworkPlayer.LocalPlayer;
        if (player == null)
            return;

        try
        {
            var pp = player.transform.position;
            var cam = Camera.main;
            var msg = $"[Diag] jugador {pp:F0}";

            if (cam != null)
            {
                msg += $" | cámara {cam.name} {cam.transform.position:F0} (dist {Vector3.Distance(cam.transform.position, pp):F0}, " +
                       $"stereo {cam.stereoTargetEye}, parent {(cam.transform.parent ? cam.transform.parent.name : "-")})";

                if (brainType != null)
                {
                    var brain = cam.GetComponent(brainType);
                    var vcam = brain != null ? brainType.GetProperty("ActiveVirtualCamera")?.GetValue(brain) : null;
                    var vname = vcam?.GetType().GetProperty("Name")?.GetValue(vcam);
                    var follow = vcam?.GetType().GetProperty("Follow")?.GetValue(vcam) as Transform;
                    msg += $" | vcam {vname ?? "-"}, follow {(follow ? $"{follow.name} {follow.position:F0}" : "-")}";
                }
            }

            foreach (var l in FindObjectsOfType<AudioListener>())
                msg += $" | AudioListener {l.name} {l.transform.position:F0} {(l.enabled ? "on" : "off")}";

            if (fmodListenerType != null)
                foreach (var o in Resources.FindObjectsOfTypeAll(fmodListenerType).OfType<Component>())
                {
                    if (!o.gameObject.scene.IsValid()) continue;
                    var att = fmodListenerType.GetProperty("AttenuationObject")?.GetValue(o) as GameObject
                              ?? fmodListenerType.GetField("attenuationObject")?.GetValue(o) as GameObject;
                    msg += $" | FMOD listener {o.name} {o.transform.position:F0} " +
                           $"{((o as Behaviour)?.isActiveAndEnabled == true ? "on" : "off")}" +
                           (att ? $" atten {att.name} {att.transform.position:F0}" : "");
                }

            Plugin.Log.LogInfo(msg);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Diag] {ex.Message}");
        }
    }
}
