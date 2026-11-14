using System.Collections.Generic;
using HarmonyLib;
using Robosaru;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;
using InputDevice = UnityEngine.XR.InputDevice;

namespace StrikeOpsXR;

/// <summary>
/// Apuntar con el mando derecho.
/// El juego (modo teclado/ratón) apunta así: Camera.main.ScreenPointToRay(ratón) → Physics.Raycast
/// contra PoolManager.mouseAimingLayer → mouseWorldPos. Aquí se hace al revés: rayo desde el mando
/// contra esas mismas capas, el punto se proyecta a la pantalla de la cámara del juego y se mueve
/// el ratón ahí. Para el juego es como mover el ratón.
/// </summary>
internal class VRAim : MonoBehaviour
{
    private const float MaxDistance = 5000f;

    private static readonly InputFeatureUsage<Vector3> PointerPosition = new("PointerPosition");
    private static readonly InputFeatureUsage<Quaternion> PointerRotation = new("PointerRotation");

    private VRRig vrRig;
    private InputDevice right;
    private bool loggedFeatures;
    private bool? usePointer;
    private string lastHow = "";
    private float nextDiag;
    private Shader shader;
    private Transform reticle;

    private LineRenderer laser;

    private void Start()
    {
        vrRig = GetComponent<VRRig>();

        shader = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
        var go = new GameObject("StrikeOpsXR_Laser");
        DontDestroyOnLoad(go);
        laser = go.AddComponent<LineRenderer>();
        laser.useWorldSpace = true;
        laser.positionCount = 2;
        laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        laser.receiveShadows = false;
        if (shader != null)
            laser.material = new Material(shader);
        laser.startColor = new Color(1f, 0.2f, 0.2f, 0.9f);
        laser.endColor = new Color(1f, 0.2f, 0.2f, 0.3f);
        laser.enabled = false;

        // Mira 3D: punto al que apunta el juego de verdad (NetworkPlayer.localTargetPosition)
        if (shader != null)
        {
            var mat = new Material(shader)
            {
                mainTexture = CreatePlusTexture(),
                color = new Color(1f, 0.9f, 0.2f, 1f),
                renderQueue = 4990
            };
            mat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            reticle = UiOverlay.CreateQuad("StrikeOpsXR_Reticle", mat).transform;
            DontDestroyOnLoad(reticle.gameObject);
            reticle.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (laser != null)
            Destroy(laser.gameObject);
        if (reticle != null)
            Destroy(reticle.gameObject);
    }

    private void LateUpdate()
    {
        laser.enabled = false;
        VRControls.AimPosition = null;
        ControllerRay = null;
        AimPoint = null;
        UpdateReticle();

        if (!Plugin.Cfg.AimWithController.Value || vrRig == null)
            return;

        var gameCam = vrRig.SourceCamera;
        var player = NetworkPlayer.LocalPlayer;
        if (gameCam == null || player == null || NetworkSystem.Paused)
            return;

        if (!TryGetPose(out var trackPos, out var trackRot))
            return;

        vrRig.TrackingToWorld(trackPos, trackRot, out var origin, out var rot);
        var dir = rot * Vector3.forward;
        var scale = vrRig.Rig.lossyScale.x;

        var mask = PoolManager.instance != null ? (int)PoolManager.instance.mouseAimingLayer : ~0;
        if (mask == 0) mask = ~0;

        // 1) Lo mismo que el juego: raycast contra las capas de apuntado.
        // 2) Si no toca nada: plano horizontal a la altura del helicóptero.
        var hit = Physics.Raycast(origin, dir, out var info, MaxDistance * Mathf.Max(1f, scale), mask,
            QueryTriggerInteraction.Ignore);
        var point = info.point;
        // Solo el tipo (no el collider) para no llenar el log al mover el mando
        string how = hit ? $"raycast (capa {LayerMask.LayerToName(info.collider.gameObject.layer)})" : null;

        if (!hit)
        {
            // Sin collider (lejos, o hacia el cielo): punto en la dirección horizontal del mando,
            // a la altura del jugador y como mucho a AimMaxDistance de él.
            var playerPos = player.transform.position;
            var maxDist = Plugin.Cfg.AimMaxDistance.Value;
            var plane = new Plane(Vector3.up, playerPos);

            if (plane.Raycast(new Ray(origin, dir), out var enter) &&
                Vector3.Distance(origin + dir * enter, playerPos) <= maxDist)
            {
                point = origin + dir * enter;
                how = "plano a la altura del jugador";
            }
            else
            {
                var flat = Vector3.ProjectOnPlane(dir, Vector3.up);
                if (flat.sqrMagnitude < 1e-4f)
                    flat = vrRig.Rig.forward; // apuntando recto arriba
                point = playerPos + flat.normalized * maxDist;
                how = "dirección horizontal a distancia máxima";
            }

            hit = true;
        }

        AimPoint = point;

        if (how != lastHow)
        {
            lastHow = how;
            Plugin.Log.LogInfo($"Apuntado: {(how ?? "sin punto (el mando apunta al cielo)")}. Máscara {mask:X}");
        }

        var end = hit ? point : origin + dir * 200f * scale;

        // Láser
        if (Plugin.Cfg.ShowLaser.Value)
        {
            var cam = vrRig.XrCamera;
            laser.gameObject.layer = UiOverlay.PickLayer(cam != null ? cam.cullingMask : 1);
            laser.startWidth = 0.006f * scale;
            laser.endWidth = (hit ? 0.02f : 0.003f) * scale;
            laser.SetPosition(0, origin);
            laser.SetPosition(1, end);
            laser.enabled = true;
        }

        if (!hit)
            return;

        // Punto del mundo → píxel de la cámara del juego (renderiza a un RT del tamaño de la pantalla)
        // Diagnóstico: distancia entre el punto del mando y el punto que el juego usa
        if (Time.unscaledTime >= nextDiag)
        {
            nextDiag = Time.unscaledTime + 5f;
            var game = player.localTargetPosition;
            Plugin.Log.LogInfo($"Apuntado: mando {point:F0}, juego {game:F0}, separación {Vector3.Distance(point, game):F1} m");
        }

        // El punto de mira lo calcula el juego con el rayo del mando (parche de ScreenPointToRay),
        // así que no está limitado a lo que ve la cámara de escritorio.
        ControllerRay = new Ray(origin, dir);

        // La dirección de mirada (OnMoveLook) sí usa la posición del ratón en pantalla respecto al
        // jugador. Si el punto cae fuera de la pantalla, se acerca al jugador por la misma línea
        // hasta que quepa: la dirección se conserva.
        var sp = (Vector2)gameCam.WorldToScreenPoint(point);
        var ps = (Vector2)gameCam.WorldToScreenPoint(player.transform.position);
        if (gameCam.WorldToScreenPoint(point).z <= 0f)
            return;

        VRControls.AimPosition = ClipToScreen(ps, sp);
    }

    // ---- rayo del mando como rayo de apuntado del juego ----

    internal static Ray? ControllerRay;
    internal static Vector3? AimPoint;
    [System.ThreadStatic] private static bool inTargetting;

    public static void Install(Harmony harmony)
    {
        var begin = new HarmonyMethod(typeof(VRAim), nameof(TargettingPrefix));
        var end = new HarmonyMethod(typeof(VRAim), nameof(TargettingPostfix));
        foreach (var t in new[] { typeof(NetworkPlayerAircraft), typeof(NetworkPlayerTank) })
        {
            var m = AccessTools.Method(t, "TargettingCalculation");
            if (m != null)
                harmony.Patch(m, prefix: begin, finalizer: end);
        }

        var swap = new HarmonyMethod(typeof(VRAim), nameof(ScreenPointToRayPrefix));
        harmony.Patch(AccessTools.Method(typeof(Camera), nameof(Camera.ScreenPointToRay), new[] { typeof(Vector3) }),
            prefix: swap);
        harmony.Patch(AccessTools.Method(typeof(Camera), nameof(Camera.ScreenPointToRay),
            new[] { typeof(Vector3), typeof(Camera.MonoOrStereoscopicEye) }), prefix: swap);

        // El raycast de apuntado del juego: si no toca nada, se le da el punto calculado por VRAim
        var fill = new HarmonyMethod(typeof(VRAim), nameof(RaycastPostfix));
        harmony.Patch(AccessTools.Method(typeof(Physics), nameof(Physics.Raycast),
            new[] { typeof(Ray), typeof(RaycastHit).MakeByRefType(), typeof(float), typeof(int) }), postfix: fill);
        harmony.Patch(AccessTools.Method(typeof(Physics), nameof(Physics.Raycast),
            new[] { typeof(Ray), typeof(RaycastHit).MakeByRefType(), typeof(float), typeof(int), typeof(QueryTriggerInteraction) }),
            postfix: fill);

        Plugin.Log.LogInfo("Apuntado VR: parche de TargettingCalculation instalado");
    }

    private static void TargettingPrefix() => inTargetting = true;

    private static System.Exception TargettingPostfix(System.Exception __exception)
    {
        inTargetting = false;
        return __exception;
    }

    private static bool ScreenPointToRayPrefix(ref Ray __result)
    {
        if (!inTargetting || !ControllerRay.HasValue || !Plugin.Cfg.AimWithController.Value)
            return true;

        __result = ControllerRay.Value;
        return false;
    }

    private static void RaycastPostfix(ref bool __result, ref RaycastHit hitInfo)
    {
        if (__result || !inTargetting || !AimPoint.HasValue || !ControllerRay.HasValue ||
            !Plugin.Cfg.AimWithController.Value)
            return;

        var p = AimPoint.Value;
        hitInfo.point = p;
        hitInfo.normal = Vector3.up;
        hitInfo.distance = Vector3.Distance(ControllerRay.Value.origin, p);
        __result = true;
    }

    private static Vector2 ClipToScreen(Vector2 from, Vector2 to)
    {
        const float m = 2f;
        float w = Screen.width, h = Screen.height;
        from = new Vector2(Mathf.Clamp(from.x, m, w - m), Mathf.Clamp(from.y, m, h - m));

        var d = to - from;
        var t = 1f;
        if (d.x > 0) t = Mathf.Min(t, (w - m - from.x) / d.x);
        if (d.x < 0) t = Mathf.Min(t, (m - from.x) / d.x);
        if (d.y > 0) t = Mathf.Min(t, (h - m - from.y) / d.y);
        if (d.y < 0) t = Mathf.Min(t, (m - from.y) / d.y);
        return from + d * Mathf.Max(0f, t);
    }

    /// <summary>"+" blanco (el material lo tiñe de amarillo) con borde oscuro fino.</summary>
    private static Texture2D CreatePlusTexture()
    {
        const int n = 64;
        const int arm = 3;   // medio grosor del trazo
        const int gap = 6;   // hueco central
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { name = "StrikeOpsXR_Plus", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        var c = n / 2;

        bool Plus(int x, int y, int grow)
        {
            var dx = Mathf.Abs(x - c);
            var dy = Mathf.Abs(y - c);
            var inH = dy <= arm + grow && dx <= c - 2 + grow && dx >= gap - grow;
            var inV = dx <= arm + grow && dy <= c - 2 + grow && dy >= gap - grow;
            return inH || inV;
        }

        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
            px[y * n + x] = Plus(x, y, 0) ? new Color32(255, 255, 255, 255)
                : Plus(x, y, 2) ? new Color32(0, 0, 0, 160)
                : new Color32(0, 0, 0, 0);

        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    private void UpdateReticle()
    {
        if (reticle == null)
            return;

        var player = NetworkPlayer.LocalPlayer;
        var cam = vrRig != null ? vrRig.XrCamera : null;
        var show = Plugin.Cfg.ShowReticle.Value && player != null && !NetworkSystem.Paused &&
                   cam != null && cam.enabled && player.localTargetPosition != Vector3.zero;
        reticle.gameObject.SetActive(show);
        if (!show)
            return;

        var p = player.localTargetPosition;
        var eye = cam.transform.position;
        var dist = Vector3.Distance(p, eye);

        // Tamaño angular constante (~1.5°) y siempre mirando a la cabeza
        reticle.position = p;
        reticle.rotation = Quaternion.LookRotation(p - eye, cam.transform.up);
        reticle.localScale = Vector3.one * dist * 0.026f;
        reticle.gameObject.layer = UiOverlay.PickLayer(cam.cullingMask);
    }

    private bool TryGetPose(out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = default;

        if (!right.isValid)
        {
            right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!right.isValid)
                return false;
        }

        if (!loggedFeatures)
        {
            loggedFeatures = true;
            var list = new List<InputFeatureUsage>();
            right.TryGetFeatureUsages(list);
            Plugin.Log.LogInfo($"Mando derecho: {right.name}. Features: {string.Join(", ", list.ConvertAll(f => f.name))}");
        }

        // Pose de "apuntar" de OpenXR si existe (sale de la punta del mando); si no, la de agarre
        if (usePointer != false &&
            right.TryGetFeatureValue(PointerPosition, out pos) &&
            right.TryGetFeatureValue(PointerRotation, out rot))
        {
            if (usePointer == null) Plugin.Log.LogInfo("Apuntado: pose PointerPosition/PointerRotation");
            usePointer = true;
            return true;
        }

        if (usePointer == null) Plugin.Log.LogInfo("Apuntado: pose devicePosition/deviceRotation");
        usePointer = false;
        return right.TryGetFeatureValue(CommonUsages.devicePosition, out pos) &&
               right.TryGetFeatureValue(CommonUsages.deviceRotation, out rot);
    }
}
