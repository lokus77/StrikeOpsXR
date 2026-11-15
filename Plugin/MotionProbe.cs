using System;
using System.Linq;
using UnityEngine;

namespace StrikeOpsXR;

/// <summary>
/// Diagnóstico (Diagnostics = true): mide fotograma a fotograma cómo se mueven el helicóptero visible,
/// la raíz del jugador y la cámara VR, para localizar tirones. Cada 5 s, en vuelo, escribe una línea [Movimiento].
/// </summary>
internal class MotionProbe : MonoBehaviour
{
    private VRRig rig;
    private Vector3 lastVeh, lastRoot, lastRel;
    private bool has;
    private float next;
    private int n, vehStalls, rootStalls;
    private double sumDt, sumDt2, maxDt, sumV, sumV2, sumR, sumR2, sumRel2, maxRel;
    private bool loggedSetup;

    private void Awake() => rig = GetComponent<VRRig>();
    private void OnEnable() => Application.onBeforeRender += Sample;
    private void OnDisable() => Application.onBeforeRender -= Sample;

    private void Sample()
    {
        try
        {
            if (!Plugin.Cfg.Diagnostics.Value) { has = false; return; }
            var p = Robosaru.NetworkPlayer.LocalPlayer;
            var cam = rig != null ? rig.XrCamera : null;
            if (p == null || p.vehicle == null || cam == null) { has = false; return; }

            if (!loggedSetup) { loggedSetup = true; LogSetup(p); }

            var veh = p.vehicle.transform.position;
            var root = p.transform.position;
            var rel = veh - cam.transform.position; // helicóptero visto desde la cámara
            var dt = Time.unscaledDeltaTime;

            if (has && dt > 0f)
            {
                var dv = (veh - lastVeh).magnitude;
                var dr = (root - lastRoot).magnitude;
                var vv = dv / dt;
                var vr = dr / dt;
                if (vv > 5f) // solo en movimiento
                {
                    n++;
                    sumDt += dt; sumDt2 += dt * dt; maxDt = Math.Max(maxDt, dt);
                    sumV += vv; sumV2 += vv * vv;
                    sumR += vr; sumR2 += vr * vr;
                    if (dv < 0.3f * vv * dt) vehStalls++;
                    if (dr < 0.01f) rootStalls++;
                    var j = (rel - lastRel).magnitude; // cuánto salta el helicóptero en la vista
                    sumRel2 += j * j; maxRel = Math.Max(maxRel, j);
                }
            }
            lastVeh = veh; lastRoot = root; lastRel = rel; has = true;

            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 5f;
            if (n < 30) { Reset(); return; }

            double Cv(double s, double s2) { var m = s / n; var v = s2 / n - m * m; return m > 0 ? Math.Sqrt(Math.Max(0, v)) / m : 0; }
            Plugin.Log.LogInfo(
                $"[Movimiento] {n} fotogramas: dt medio {sumDt / n * 1000:F1} ms (variación {Cv(sumDt, sumDt2):P0}, máx {maxDt * 1000:F1}) | " +
                $"helicóptero visible {sumV / n:F0} m/s (variación {Cv(sumV, sumV2):P0}, parados {vehStalls}) | " +
                $"raíz {sumR / n:F0} m/s (variación {Cv(sumR, sumR2):P0}, parados {rootStalls}) | " +
                $"salto en la vista: medio {Math.Sqrt(sumRel2 / n) * 100:F1} cm, máx {maxRel * 100:F1} cm");
            Reset();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Movimiento] {ex.Message}");
            enabled = false;
        }
    }

    private void Reset()
    {
        n = vehStalls = rootStalls = 0;
        sumDt = sumDt2 = maxDt = sumV = sumV2 = sumR = sumR2 = sumRel2 = maxRel = 0;
    }

    private static void LogSetup(Robosaru.NetworkPlayer p)
    {
        var rb = p.GetComponent<Rigidbody>();
        var msg = $"[Movimiento] física {1f / Time.fixedDeltaTime:F0} Hz, raíz '{p.name}' rigidbody " +
                  $"{(rb != null ? $"interp {rb.interpolation}, kinematic {rb.isKinematic}" : "no")}, " +
                  $"visible '{p.vehicle.name}' (hijo de '{p.vehicle.transform.parent?.name}')";
        try
        {
            var runnerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Fusion.NetworkRunner")).FirstOrDefault(t => t != null);
            var runner = runnerType != null ? FindObjectOfType(runnerType) : null;
            if (runner != null)
            {
                string Prop(string name) => runnerType.GetProperty(name)?.GetValue(runner)?.ToString() ?? "?";
                msg += $" | Fusion: modo {Prop("GameMode")}, tick {Prop("TickRate")}, deltaTime {Prop("DeltaTime")}";
            }
            foreach (var c in p.GetComponents<Component>())
                if (c != null && c.GetType().Name.Contains("NetworkRigidbody") || c != null && c.GetType().Name.Contains("NetworkTransform"))
                {
                    var t = c.GetType();
                    string P(string name) => t.GetProperty(name)?.GetValue(c)?.ToString() ?? "?";
                    msg += $" | {t.Name}: InterpolationTarget {P("InterpolationTarget")}, " +
                           $"InterpolationDataSource {P("InterpolationDataSource")}, InterpolationSpace {P("InterpolationSpace")}";
                }
        }
        catch (Exception ex) { msg += $" | (Fusion: {ex.Message})"; }
        Plugin.Log.LogInfo(msg);
    }
}
