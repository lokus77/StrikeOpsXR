using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace StrikeOpsXR;

/// <summary>
/// Cada 5 s escribe en el log dónde se va el tiempo de cada fotograma:
///   - FPS medios y peor fotograma
///   - lógica (Update..LateUpdate de todos los scripts, juego + mod), medida en el hilo principal
///   - FrameTimingManager de Unity (CPU principal / render / GPU), si el juego lo tiene activado
///   - tiempos de GPU que dé el runtime XR
/// </summary>
[DefaultExecutionOrder(-32000)]
internal class PerfMonitor : MonoBehaviour
{
    internal static readonly Stopwatch Clock = Stopwatch.StartNew();
    internal static double FrameLogicStart;
    internal static double LogicMs;

    private float next;
    private int frames;
    private double sumDt, maxDt, sumLogic;
    private readonly FrameTiming[] timings = new FrameTiming[1];
    private readonly List<XRDisplaySubsystem> displays = new();
    private bool loggedTimingSupport;

    // Tiempo de CPU (hilo principal) que se pasa preparando cada cámara, y desde el final de la lógica
    // hasta el siguiente fotograma (render + espera a GPU / runtime XR)
    private readonly Dictionary<string, double> cameraMs = new();
    private double cameraStart, renderAndWait, lastLogicEnd;
    internal static double LogicEnd;

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.endCameraRendering -= EndCamera;
    }

    private void BeginCamera(ScriptableRenderContext ctx, Camera cam) => cameraStart = Clock.Elapsed.TotalMilliseconds;

    private void EndCamera(ScriptableRenderContext ctx, Camera cam)
    {
        var key = cam.cameraType == CameraType.Game && cam.stereoEnabled ? "VR" :
                  cam.targetTexture != null ? $"{cam.name} (RT {cam.targetTexture.width}x{cam.targetTexture.height})" : cam.name;
        cameraMs.TryGetValue(key, out var v);
        cameraMs[key] = v + (Clock.Elapsed.TotalMilliseconds - cameraStart);
    }

    private void Awake()
    {
        var end = new GameObject("StrikeOpsXR_PerfEnd");
        end.transform.SetParent(transform, false);
        end.AddComponent<PerfMonitorEnd>();
    }

    private void Update()
    {
        FrameLogicStart = Clock.Elapsed.TotalMilliseconds;
        if (lastLogicEnd > 0)
            renderAndWait += FrameLogicStart - lastLogicEnd;
    }

    internal void EndOfLogic()
    {
        var dt = Time.unscaledDeltaTime * 1000.0;
        frames++;
        sumDt += dt;
        if (dt > maxDt) maxDt = dt;
        sumLogic += LogicMs;
        lastLogicEnd = LogicEnd;

        FrameTimingManager.CaptureFrameTimings();

        if (Time.unscaledTime < next)
            return;
        next = Time.unscaledTime + 5f;
        if (frames == 0)
            return;

        var avg = sumDt / frames;
        var msg = $"[Rendimiento] {1000.0 / avg:F0} fps (fotograma {avg:F1} ms, peor {maxDt:F1} ms) | " +
                  $"lógica scripts {sumLogic / frames:F1} ms";

        if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].cpuFrameTime > 0)
        {
            var t = timings[0];
            msg += $" | CPU principal {t.cpuMainThreadFrameTime:F1} ms, render {t.cpuRenderThreadFrameTime:F1} ms, " +
                   $"GPU {t.gpuFrameTime:F1} ms";
        }
        else if (!loggedTimingSupport)
        {
            loggedTimingSupport = true;
            msg += " | (FrameTimingManager no disponible en este juego)";
        }

        SubsystemManager.GetInstances(displays);
        if (displays.Count > 0)
        {

            if (XRStats.TryGetGPUTimeLastFrame(out var gpu) && gpu > 0f)
                msg += $" | GPU (XR) {gpu * 1000f:F1} ms";
            if (XRStats.TryGetDroppedFrameCount(out var dropped))
                msg += $", perdidos {dropped}";
        }

        msg += $" | render+espera {renderAndWait / frames:F1} ms, de ello cámaras:";
        foreach (var kv in cameraMs)
            msg += $" {kv.Key} {kv.Value / frames:F2}";
        cameraMs.Clear();
        renderAndWait = 0;

        msg += $" | targetFrameRate {Application.targetFrameRate}, vSync {QualitySettings.vSyncCount}";
        Plugin.Log.LogInfo(msg);

        frames = 0;
        sumDt = maxDt = sumLogic = 0;
    }
}

[DefaultExecutionOrder(32000)]
internal class PerfMonitorEnd : MonoBehaviour
{
    private PerfMonitor monitor;

    private void Awake() => monitor = GetComponentInParent<PerfMonitor>();

    private void LateUpdate()
    {
        PerfMonitor.LogicEnd = PerfMonitor.Clock.Elapsed.TotalMilliseconds;
        PerfMonitor.LogicMs = PerfMonitor.LogicEnd - PerfMonitor.FrameLogicStart;
        monitor?.EndOfLogic();
    }
}
