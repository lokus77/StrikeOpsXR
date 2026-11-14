using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace StrikeOpsXR;

/// <summary>
/// El HUD y los menús del juego son UI Toolkit (PanelSettings) y se dibujan como overlay de
/// pantalla, que no llega al casco. Cada PanelSettings pasa a renderizar a su propio
/// RenderTexture del tamaño de la pantalla. Así el layout y la entrada del ratón siguen igual,
/// porque UI Toolkit usa la posición de pantalla tal cual. Las texturas se muestran en un panel
/// flotante delante del jugador, dibujado siempre encima del mundo. El cursor es de hardware
/// (Cursor.SetCursor) y tampoco llega al casco, así que se dibuja uno propio sobre el panel.
/// </summary>
internal class UiOverlay : MonoBehaviour
{
    private class Panel
    {
        public PanelSettings Settings;
        public RenderTexture Texture;
        public GameObject Quad;
        public Material Material;
        public bool OriginalClear;
        public Color OriginalClearValue;
    }

    private readonly Dictionary<PanelSettings, Panel> panels = new();

    private VRRig vrRig;
    private Transform root;
    private Transform cursor;
    private Material cursorMaterial;
    private Shader shader;
    private int width, height;
    private float nextScan;
    private float nextVisibilityCheck;
    private readonly HashSet<PanelSettings> activeSettings = new();
    private string lastSummary;

    private void Start()
    {
        vrRig = GetComponent<VRRig>();

        shader = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Plugin.Log.LogError("No hay shader para el panel de UI (UI/Default)");
            enabled = false;
            return;
        }

        root = new GameObject("StrikeOpsXR_UI").transform;
        root.SetParent(vrRig.Rig, false);

        cursorMaterial = CreateMaterial(CreateCursorTexture(), 4999);
        cursor = CreateQuad("StrikeOpsXR_Cursor", cursorMaterial).transform;
        cursor.SetParent(root, false);

        Plugin.Log.LogInfo($"Panel de UI en VR listo (shader {shader.name})");
    }

    private void OnDestroy()
    {
        foreach (var p in panels.Values)
            Restore(p);
        panels.Clear();
    }

    private void LateUpdate()
    {
        if (root == null)
            return;

        if (Screen.width != width || Screen.height != height)
        {
            width = Mathf.Max(Screen.width, 16);
            height = Mathf.Max(Screen.height, 16);
            foreach (var p in panels.Values)
                Restore(p);
            panels.Clear();
            nextScan = 0;
        }

        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 1f;
            ScanPanels();
        }

        if (Time.unscaledTime >= nextVisibilityCheck)
        {
            nextVisibilityCheck = Time.unscaledTime + 0.2f;
            UpdateVisibility();
        }

        UpdateLayout();
        UpdateCursor();
    }

    /// <summary>
    /// Un panel sin UIDocument activo deja de repintarse y su textura se queda congelada
    /// con la última imagen (p. ej. la pantalla "plays best with gamepad"), tapando al resto.
    /// Solo se muestran los paneles con algún documento activo.
    /// </summary>
    private void UpdateVisibility()
    {
        CheckReplacementShown();

        activeSettings.Clear();
        var summary = new System.Text.StringBuilder();
        foreach (var doc in FindObjectsOfType<UIDocument>())
            if (doc.isActiveAndEnabled && doc.panelSettings != null && doc.rootVisualElement != null &&
                doc.rootVisualElement.resolvedStyle.display != DisplayStyle.None)
            {
                activeSettings.Add(doc.panelSettings);
                summary.Append($"{doc.name}→{doc.panelSettings.GetInstanceID()} ");
                if (Plugin.Cfg.UiTransparentBackdrops.Value)
                    ClearBackdrops(doc);
            }

        var s = summary.ToString();
        if (s != lastSummary)
        {
            lastSummary = s;
            Plugin.Log.LogInfo($"Documentos UI activos: {(s.Length == 0 ? "ninguno" : s)}");
        }

        foreach (var p in panels.Values)
        {
            if (p.Quad == null)
                continue;

            var visible = activeSettings.Contains(p.Settings);
            if (p.Quad.activeSelf == visible)
                continue;

            p.Quad.SetActive(visible);
            if (!visible)
            {
                // Borrar la imagen congelada para que no reaparezca al volver a mostrarse
                var prev = RenderTexture.active;
                RenderTexture.active = p.Texture;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = prev;
            }

            Plugin.Log.LogInfo($"Panel {p.Settings.GetInstanceID()}: {(visible ? "visible" : "oculto")}");
        }
    }

    // ---- paneles ----

    private void ScanPanels()
    {
        foreach (var settings in Resources.FindObjectsOfTypeAll<PanelSettings>())
        {
            if (settings == null || panels.ContainsKey(settings))
                continue;

            // Si el propio juego ya renderiza este panel a una textura, no tocarlo
            if (settings.targetTexture != null)
                continue;

            var p = new Panel
            {
                Settings = settings,
                OriginalClear = settings.clearColor,
                OriginalClearValue = settings.colorClearValue,
                Texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                {
                    name = $"StrikeOpsXR_UI_{settings.name}"
                }
            };
            p.Texture.Create();

            settings.targetTexture = p.Texture;
            settings.clearColor = true;
            settings.colorClearValue = Color.clear;

            var queue = 3500 + Mathf.Clamp(Mathf.RoundToInt(settings.sortingOrder), -400, 400);
            p.Material = CreateMaterial(p.Texture, queue);
            p.Quad = CreateQuad($"StrikeOpsXR_Panel_{settings.name}", p.Material);
            p.Quad.transform.SetParent(root, false);

            p.Quad.SetActive(false); // UpdateVisibility decide
            nextVisibilityCheck = 0;
            panels[settings] = p;
            Plugin.Log.LogInfo($"UI redirigida al casco: {settings.name} #{settings.GetInstanceID()} (orden {settings.sortingOrder})");
        }

        // Paneles destruidos (cambio de escena)
        List<PanelSettings> dead = null;
        foreach (var kv in panels)
            if (kv.Key == null)
                (dead ??= new List<PanelSettings>()).Add(kv.Key);
        if (dead != null)
            foreach (var k in dead)
            {
                Restore(panels[k]);
                panels.Remove(k);
            }
    }

    private static void Restore(Panel p)
    {
        if (p.Settings != null && p.Settings.targetTexture == p.Texture)
        {
            p.Settings.targetTexture = null;
            p.Settings.clearColor = p.OriginalClear;
            p.Settings.colorClearValue = p.OriginalClearValue;
        }

        if (p.Quad != null) Destroy(p.Quad);
        if (p.Material != null) Destroy(p.Material);
        if (p.Texture != null) p.Texture.Release();
    }

    private void UpdateLayout()
    {
        var cfg = Plugin.Cfg;
        var w = cfg.UiWidth.Value;
        var h = w * height / width;

        // Hijo del rig: unidades = metros reales del casco, sea cual sea WorldScale
        root.localPosition = new Vector3(0f, cfg.UiHeight.Value, cfg.UiDistance.Value);
        root.localRotation = Quaternion.identity;
        root.localScale = new Vector3(w, cfg.UiFlipY.Value ? -h : h, 1f);

        // Que el panel no quede fuera de los planos de recorte ni del culling de la cámara XR
        var cam = vrRig.XrCamera;
        if (cam == null)
            return;

        var scale = vrRig.Rig.lossyScale.x;
        var needed = (cfg.UiDistance.Value + w) * scale * 1.5f;
        if (cam.farClipPlane < needed)
            cam.farClipPlane = needed;

        var layer = PickLayer(cam.cullingMask);
        foreach (var p in panels.Values)
            if (p.Quad != null) p.Quad.layer = layer;
        cursor.gameObject.layer = layer;
    }

    internal static int PickLayer(int mask)
    {
        if ((mask & (1 << 5)) != 0) return 5;   // UI
        if ((mask & 1) != 0) return 0;          // Default
        for (var i = 0; i < 32; i++)
            if ((mask & (1 << i)) != 0)
                return i;
        return 0;
    }

    // ---- fondos opacos ----

    private readonly HashSet<VisualElement> seenOpaque = new();
    private readonly List<VisualElement> elements = new();

    /// <summary>
    /// En VR los rellenos de color a pantalla completa (fondo negro del loading, recuadros de
    /// diálogo) tapan el mundo. Los que ocupan más de UI.BackdropMinArea de la pantalla se hacen
    /// transparentes. Solo el color de fondo: las imágenes y los textos no se tocan.
    /// El resto de fondos opacos grandes se listan en el log una vez, para poder afinar.
    /// </summary>
    private void ClearBackdrops(UIDocument doc)
    {
        var root = doc.rootVisualElement;
        var rb = root.worldBound;
        var rootArea = rb.width * rb.height;
        if (float.IsNaN(rootArea) || rootArea < 1f)
            return;

        var minArea = Plugin.Cfg.UiBackdropMinArea.Value;

        elements.Clear();
        root.Query<VisualElement>().ToList(elements);
        foreach (var ve in elements)
        {
            ReplaceImage(doc, ve);
            HideText(doc, ve);
            HideElement(doc, ve);

            if (ve.resolvedStyle.display == DisplayStyle.None || ve.resolvedStyle.visibility == Visibility.Hidden)
                continue;

            var c = ve.resolvedStyle.backgroundColor;
            if (c.a < 0.3f)
                continue;

            var b = ve.worldBound;
            var frac = b.width * b.height / rootArea;
            if (float.IsNaN(frac) || frac < 0.05f)
                continue;

            var clear = frac >= minArea;
            if (seenOpaque.Add(ve))
                Plugin.Log.LogInfo($"Fondo UI {doc.name}/{ve.name} [{string.Join(" ", ve.GetClasses())}] " +
                                   $"color {ColorUtility.ToHtmlStringRGBA(c)}, {frac:P0} de pantalla" +
                                   (clear ? " → transparente" : ""));

            if (clear)
                ve.style.backgroundColor = new Color(c.r, c.g, c.b, 0f);
        }

        if (seenOpaque.Count > 2000)
            seenOpaque.Clear();
    }

    // ---- sustitución de imágenes (p. ej. "plays best with gamepad") ----

    private readonly HashSet<string> loggedImages = new();
    private Texture2D replacement;
    private bool replacementLoaded;

    private void ReplaceImage(UIDocument doc, VisualElement ve)
    {
        var bg = ve.resolvedStyle.backgroundImage;
        Object asset = bg.texture != null ? bg.texture
            : bg.sprite != null ? bg.sprite
            : bg.renderTexture != null ? bg.renderTexture
            : bg.vectorImage;
        if (asset == null || asset == replacement)
            return;

        var imgName = asset.name;
        if (loggedImages.Add(imgName))
            Plugin.Log.LogInfo($"Imagen UI: '{imgName}' en {doc.name}/{ve.name}");

        var match = Plugin.Cfg.UiReplaceImageMatch.Value;
        if (string.IsNullOrEmpty(match) ||
            (imgName.IndexOf(match, System.StringComparison.OrdinalIgnoreCase) < 0 &&
             ve.name.IndexOf(match, System.StringComparison.OrdinalIgnoreCase) < 0))
            return;

        var tex = LoadReplacement();
        if (tex == null)
            return;

        if (Plugin.Cfg.UiReplaceImageFullScreen.Value)
        {
            // A pantalla completa en la raíz del documento, ajustada sin deformar;
            // el elemento original se queda sin imagen
            // Capa propia encima de todo el documento (la raíz tiene paneles opacos encima que la tapan),
            // a pantalla completa, ajustada sin deformar y sin capturar clics
            replacedRoot?.RemoveFromHierarchy();
            var overlay = new VisualElement { name = "StrikeOpsXR_Splash", pickingMode = PickingMode.Ignore };
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0;
            overlay.style.top = 0;
            overlay.style.right = 0;
            overlay.style.bottom = 0;
            overlay.style.backgroundImage = new StyleBackground(tex);
            overlay.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            overlay.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            overlay.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            overlay.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            doc.rootVisualElement.Add(overlay);

            replacedRoot = overlay;
            replacedElement = ve;
            ve.style.backgroundImage = new StyleBackground((Texture2D)null);
        }
        else
        {
            ve.style.backgroundImage = new StyleBackground(tex);
        }

        Plugin.Log.LogInfo($"Imagen UI '{imgName}' sustituida por {Plugin.Cfg.UiReplaceImageFile.Value}");
    }

    // ---- ocultar textos ----

    private string[] hideTexts;

    /// <summary>Oculta los textos que contienen alguna de las frases de UI.HideTexts (separadas por '|').</summary>
    private void HideText(UIDocument doc, VisualElement ve)
    {
        if (ve is not TextElement te || string.IsNullOrEmpty(te.text) ||
            te.resolvedStyle.display == DisplayStyle.None)
            return;

        hideTexts ??= Plugin.Cfg.UiHideTexts.Value
            .Split(new[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

        foreach (var h in hideTexts)
            if (te.text.IndexOf(h, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                te.style.display = DisplayStyle.None;
                Plugin.Log.LogInfo($"Texto UI oculto: '{te.text}' ({doc.name}/{te.name})");
                return;
            }
    }

    // ---- ocultar elementos por nombre ----

    private HashSet<string> hideElements;

    /// <summary>
    /// Marcadores del HUD que el juego coloca proyectando desde su cámara (mira y línea de puntos
    /// al objetivo fijado): en el panel VR no coinciden con el mundo 3D. Se ocultan por nombre.
    /// </summary>
    private void HideElement(UIDocument doc, VisualElement ve)
    {
        if (string.IsNullOrEmpty(ve.name))
            return;

        hideElements ??= new HashSet<string>(Plugin.Cfg.UiHideElements.Value
            .Split(new[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0), System.StringComparer.OrdinalIgnoreCase);

        if (!hideElements.Contains(ve.name) || ve.resolvedStyle.display == DisplayStyle.None)
            return;

        ve.style.display = DisplayStyle.None;
        Plugin.Log.LogInfo($"Elemento UI oculto: {doc.name}/{ve.name}");
    }

    private VisualElement replacedRoot, replacedElement;

    /// <summary>Quita la imagen a pantalla completa cuando el elemento original deja de verse.</summary>
    private void CheckReplacementShown()
    {
        if (replacedRoot == null)
            return;

        // Visible mientras el elemento original (y sus padres) lo estén. La opacidad se copia
        // (fundidos de entrada/salida) en vez de retirar la imagen al estar a 0.
        var shown = replacedElement != null && replacedElement.panel != null;
        var opacity = 1f;
        for (var e = replacedElement; shown && e != null; e = e.parent)
        {
            if (e.resolvedStyle.display == DisplayStyle.None || e.resolvedStyle.visibility == Visibility.Hidden)
                shown = false;
            opacity *= e.resolvedStyle.opacity;
        }

        if (shown)
        {
            replacedRoot.style.opacity = opacity;
            return;
        }

        replacedRoot.RemoveFromHierarchy();
        replacedRoot = replacedElement = null;
        Plugin.Log.LogInfo("Imagen de sustitución retirada");
    }

    private Texture2D LoadReplacement()
    {
        if (replacementLoaded)
            return replacement;
        replacementLoaded = true;

        var file = Plugin.Cfg.UiReplaceImageFile.Value;
        if (string.IsNullOrEmpty(file))
            return null;

        // Ruta relativa = junto a StrikeOpsXR.dll
        if (!System.IO.Path.IsPathRooted(file))
            file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Plugin.Instance.Info.Location)!, file);

        if (!System.IO.File.Exists(file))
        {
            Plugin.Log.LogWarning($"No existe la imagen de sustitución: {file}");
            return null;
        }

        replacement = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "StrikeOpsXR_Replacement" };
        if (!replacement.LoadImage(System.IO.File.ReadAllBytes(file)))
        {
            Plugin.Log.LogWarning($"No se pudo leer la imagen: {file}");
            replacement = null;
        }
        return replacement;
    }

    // ---- cursor ----

    private void UpdateCursor()
    {
        var mouse = Mouse.current;
        // En partida, apuntando con el mando, la mira 3D sustituye al cursor
        var show = Plugin.Cfg.ShowCursor.Value && mouse != null && Cursor.visible && panels.Count > 0 &&
                   !VRControls.AimPosition.HasValue;
        cursor.gameObject.SetActive(show);
        if (!show)
            return;

        // Posición del ratón del Input System: píxeles con origen abajo a la izquierda
        var pos = mouse.position.ReadValue();
        var u = Mathf.Clamp01(pos.x / width) - 0.5f;
        var v = Mathf.Clamp01(pos.y / height) - 0.5f;

        var size = Plugin.Cfg.CursorSize.Value;
        var parent = root.localScale;
        cursor.localPosition = new Vector3(u, v, -0.001f);
        cursor.localScale = new Vector3(size / Mathf.Abs(parent.x), size / Mathf.Abs(parent.y), 1f);
    }

    // ---- utilidades ----

    private Material CreateMaterial(Texture tex, int queue)
    {
        var mat = new Material(shader) { mainTexture = tex, renderQueue = queue };
        // UI/Default: siempre encima del mundo
        mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        return mat;
    }

    internal static GameObject CreateQuad(string name, Material mat)
    {
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = QuadMesh;

        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go;
    }

    private static Mesh quadMesh;

    /// <summary>Quad de 1x1 centrado, mirando a -Z (visible desde la cabeza).</summary>
    private static Mesh QuadMesh
    {
        get
        {
            if (quadMesh != null)
                return quadMesh;

            quadMesh = new Mesh
            {
                name = "StrikeOpsXR_Quad",
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0),
                    new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0)
                },
                uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) },
                triangles = new[] { 0, 2, 1, 2, 3, 1 }
            };
            quadMesh.RecalculateBounds();
            // Bounds grandes: que el frustum culling no lo descarte por la escala del rig
            quadMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return quadMesh;
        }
    }

    /// <summary>Anillo blanco con borde negro y punto central.</summary>
    internal static Texture2D CreateCursorTexture()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "StrikeOpsXR_Cursor", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        var c = (n - 1) / 2f;

        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            var d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
            Color32 col = new(0, 0, 0, 0);

            if (d <= 3f || (d >= 22f && d <= 27f))
                col = new Color32(255, 255, 255, 255);
            else if (d <= 5f || (d >= 20f && d <= 29.5f))
                col = new Color32(0, 0, 0, 200);

            px[y * n + x] = col;
        }

        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }
}
