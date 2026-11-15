using System;
using System.Collections.Generic;
using StylizedWater2;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StrikeOpsXR;

/// <summary>
/// Ajustes del mundo para VR:
///   - El mar es un plano de 7.5 x 10 km: en plano la cámara mira hacia abajo y no se ve su borde,
///     pero en VR se mira al horizonte. Se añade debajo una copia mucho más grande, solo visual.
///   - Árboles y detalle a distancia (LOD bias, distancias del terreno) configurables: en VR desaparecían antes.
///   - Distancia de dibujado y sombras configurables (rendimiento).
///   - Escribe en el log los valores originales.
/// </summary>
internal class VRWorldTweaks : MonoBehaviour
{
    private VRRig vrRig;
    private float nextCheck;
    private bool loggedCamera;

    private void Awake() => vrRig = GetComponent<VRRig>();

    private void Update()
    {
        if (Time.unscaledTime < nextCheck)
            return;
        nextCheck = Time.unscaledTime + 1f;

        try
        {
            var cam = vrRig != null ? vrRig.XrCamera : null;
            if (cam == null || !cam.enabled)
                return;

            ReloadConfigIfChanged();
            ApplyFarClip(cam);
            ApplyLod();
            ApplyTerrains();
            ExtendOcean();
            ApplyShadows();
            ApplyRendererFeatures();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Ajustes del mundo VR: {ex.Message}");
            nextCheck = Time.unscaledTime + 10f;
        }
    }

    private void ApplyFarClip(Camera cam)
    {
        // La cámara VR copia la del juego en cada cambio de cámara; se reaplica cada segundo
        var src = vrRig.SourceCamera;
        var baseFar = src != null ? src.farClipPlane : cam.farClipPlane;
        var far = Mathf.Max(cam.farClipPlane, baseFar * Mathf.Max(1f, Plugin.Cfg.FarClipMultiplier.Value));
        cam.farClipPlane = far;
        ApplyLayerCull(cam);

        if (!loggedCamera)
        {
            loggedCamera = true;
            Plugin.Log.LogInfo($"[Mundo] cámara del juego: far {baseFar:F0} -> VR far {far:F0}. " +
                               $"LOD bias {QualitySettings.lodBias:F2}, calidad '{QualitySettings.names[QualitySettings.GetQualityLevel()]}'");
        }
    }

    // ---- Recorte por capa ----
    // En VR se mira al horizonte y entra todo el mapa en el encuadre: cada objeto se prepara dos veces
    // (una por ojo) y eso satura el procesador. El terreno y el mar se siguen viendo hasta el horizonte;
    // el resto de capas (edificios, vehículos, decoración) deja de dibujarse a partir de ObjectCullDistance.

    private readonly float[] cullDistances = new float[32];
    private bool layersLogged;

    private void ApplyLayerCull(Camera cam)
    {
        var d = Plugin.Cfg.ObjectCullDistance.Value;
        var except = new HashSet<string>(
            Plugin.Cfg.ObjectCullExceptLayers.Value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < 32; i++)
        {
            var name = LayerMask.LayerToName(i);
            cullDistances[i] = d > 0f && !except.Contains(name.Trim()) ? d : 0f; // 0 = sin límite (far clip)
        }
        // Los paneles del HUD y la mira del mod están a menos de 500 m: el recorte no les afecta
        cam.layerCullSpherical = true;
        cam.layerCullDistances = cullDistances;

        // Se escribe una vez ya en partida (en el menú casi no hay objetos)
        if (!layersLogged && Plugin.Cfg.Diagnostics.Value && Robosaru.NetworkPlayer.LocalPlayer != null)
        {
            layersLogged = true;
            var counts = new Dictionary<int, int>();
            foreach (var r in FindObjectsOfType<Renderer>())
            {
                counts.TryGetValue(r.gameObject.layer, out var c);
                counts[r.gameObject.layer] = c + 1;
            }
            var parts = new List<string>();
            foreach (var kv in counts)
                parts.Add($"{LayerMask.LayerToName(kv.Key)}({kv.Key})={kv.Value}{(cullDistances[kv.Key] > 0f ? "" : " sin recorte")}");
            Plugin.Log.LogInfo($"[Mundo] recorte de objetos a {d:F0} m. Objetos por capa: {string.Join(", ", parts)}");
            var near = 0; var far = 0;
            var p = cam.transform.position;
            foreach (var r in FindObjectsOfType<Renderer>())
                if (r.enabled && r.gameObject.activeInHierarchy)
                    if ((r.bounds.center - p).sqrMagnitude > d * d) far++; else near++;
            Plugin.Log.LogInfo($"[Mundo] objetos activos: {near} a menos de {d:F0} m, {far} más lejos");
        }
    }

    // ---- Nivel de detalle ----

    private float? originalLodBias;

    private void ApplyLod()
    {
        originalLodBias ??= QualitySettings.lodBias;
        var target = originalLodBias.Value * Mathf.Max(0.1f, Plugin.Cfg.LodBiasMultiplier.Value);
        if (!Mathf.Approximately(QualitySettings.lodBias, target))
        {
            QualitySettings.lodBias = target;
            Plugin.Log.LogInfo($"[Mundo] LOD bias {originalLodBias:F2} -> {target:F2}");
        }
    }

    private readonly Dictionary<int, (float detail, float trees, float billboards, float basemap, float density)> terrainOriginals = new();

    private void ApplyTerrains()
    {
        var k = Mathf.Max(0.1f, Plugin.Cfg.TerrainDetailMultiplier.Value);
        var billboard = Plugin.Cfg.TreeBillboardDistance.Value;

        foreach (var t in Terrain.activeTerrains)
        {
            if (t == null)
                continue;

            var id = t.GetInstanceID();
            if (!terrainOriginals.TryGetValue(id, out var o))
            {
                o = (t.detailObjectDistance, t.treeDistance, t.treeBillboardDistance, t.basemapDistance, t.detailObjectDensity);
                terrainOriginals[id] = o;
                if (terrainOriginals.Count == 1)
                    Plugin.Log.LogInfo($"[Mundo] terreno: detalle {o.detail:F0}, árboles {o.trees:F0}, " +
                                       $"árboles 3D hasta {o.billboards:F0}, basemap {o.basemap:F0}, densidad vegetación {o.density:F2}");
            }

            // Árboles 3D hasta 'billboard' m: más allá el terreno usa imágenes planas, que en VR no se ven bien
            var bb = billboard > 0f ? billboard : o.billboards;
            t.detailObjectDistance = o.detail * k;
            var density = Mathf.Clamp01(o.density * Plugin.Cfg.DetailDensity.Value);
            if (!Mathf.Approximately(t.detailObjectDensity, density))
                t.detailObjectDensity = density;
            // El multiplicador solo afecta a la vegetación (hierba, arbustos): es lo que más cuesta y lo que menos
            // se nota de lejos. Los árboles siguen a la distancia del juego.
            t.treeDistance = Mathf.Max(o.trees, bb);
            t.treeBillboardDistance = bb;
            t.basemapDistance = o.basemap;
        }
    }

    // ---- Mar ----

    private readonly HashSet<int> extendedOceans = new();

    private void ExtendOcean()
    {
        var k = Plugin.Cfg.OceanExtension.Value;
        if (k <= 1f)
            return;

        foreach (var w in FindObjectsOfType<WaterObject>())
        {
            var r = w.meshRenderer != null ? w.meshRenderer : w.GetComponent<MeshRenderer>();
            var mf = w.meshFilter != null ? w.meshFilter : w.GetComponent<MeshFilter>();
            if (r == null || mf == null || mf.sharedMesh == null || !extendedOceans.Add(w.GetInstanceID()))
                continue;

            var b = r.bounds;
            if (b.size.x < 1000f && b.size.z < 1000f)
                continue; // solo el mar grande, no charcos ni depósitos

            // Copia solo visual (sin colisiones ni flotación), más grande y un poco por debajo:
            // dentro del mapa manda el mar original, fuera se ve la copia hasta el horizonte.
            var go = new GameObject("StrikeOpsXR_OceanExtension") { layer = w.gameObject.layer };
            go.transform.SetPositionAndRotation(w.transform.position, w.transform.rotation);
            go.transform.localScale = Vector3.Scale(w.transform.lossyScale, new Vector3(k, 1f, k));
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var er = go.AddComponent<MeshRenderer>();
            er.sharedMaterials = r.sharedMaterials;
            er.shadowCastingMode = ShadowCastingMode.Off;
            er.receiveShadows = false;

            // Centrar la copia sobre el mar original y bajarla un poco
            var offset = b.center - er.bounds.center;
            offset.y = -Plugin.Cfg.OceanExtensionDepth.Value;
            go.transform.position += offset;

            Plugin.Log.LogInfo($"[Mundo] mar '{w.name}' {b.size.x:F0}x{b.size.z:F0} m ampliado a " +
                               $"{er.bounds.size.x:F0}x{er.bounds.size.z:F0} m (copia visual)");
        }
    }

    // ---- Efectos del renderer URP (nubes volumétricas, etc.) ----

    private bool featuresLogged;
    private readonly Dictionary<int, bool> originalActive = new();

    private void ApplyRendererFeatures()
    {
        if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset urp)
            return;

        var field = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field?.GetValue(urp) is not ScriptableRendererData[] list)
            return;

        var off = new HashSet<string>(
            Plugin.Cfg.DisabledRendererFeatures.Value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);
        off.RemoveWhere(string.IsNullOrWhiteSpace);
        var names = new List<string>();
        foreach (var data in list)
        {
            if (data == null)
                continue;
            foreach (var f in data.rendererFeatures)
            {
                if (f == null)
                    continue;
                originalActive.TryAdd(f.GetInstanceID(), f.isActive);
                var want = originalActive[f.GetInstanceID()] && !off.Contains(f.GetType().Name.Trim()) && !off.Contains(f.name);
                if (f.isActive != want)
                    f.SetActive(want);
                names.Add($"{f.GetType().Name}{(f.isActive ? "" : " (off)")}");
            }
        }

        if (!featuresLogged)
        {
            featuresLogged = true;
            Plugin.Log.LogInfo($"[Mundo] efectos del renderer: {string.Join(", ", names)}");
        }
    }

    private float? originalShadowDistance;

    private void ApplyShadows()
    {
        if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset urp)
            return;

        if (originalShadowDistance == null)
        {
            originalShadowDistance = urp.shadowDistance;
            Plugin.Log.LogInfo($"[Mundo] sombras del juego: {originalShadowDistance:F0} m");
        }

        var d = Plugin.Cfg.ShadowDistance.Value;
        var target = d > 0f ? d : originalShadowDistance.Value;
        if (!Mathf.Approximately(urp.shadowDistance, target))
        {
            urp.shadowDistance = target;
            Plugin.Log.LogInfo($"[Mundo] sombras -> {target:F0} m");
        }
    }

    // ---- Recarga del .cfg al guardarlo (para ajustar en vivo con el juego abierto) ----

    private DateTime cfgWrite;

    private void ReloadConfigIfChanged()
    {
        var cfg = Plugin.Instance.Config;
        var t = System.IO.File.GetLastWriteTimeUtc(cfg.ConfigFilePath);
        if (cfgWrite == default)
        {
            cfgWrite = t;
            return;
        }
        if (t == cfgWrite)
            return;
        cfgWrite = t;
        cfg.Reload();
        Plugin.Log.LogInfo("strikeopsxr.cfg recargado");
    }
}
