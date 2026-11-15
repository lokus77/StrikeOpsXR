using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using InputDevice = UnityEngine.XR.InputDevice;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace StrikeOpsXR;

/// <summary>
/// Diseño "cámara al lado":
///  - La cámara original del juego (la que mueve Cinemachine, Camera.main) sigue viva para que
///    la lógica que depende de ella (movimiento relativo a cámara, apuntado con ratón,
///    marcadores del HUD) funcione sin parches. Se la deja renderizando nada a un RenderTexture.
///  - Una cámara XR nueva, hija de un rig que copia posición + yaw de la original, renderiza al casco.
///    El pose del casco se aplica a mano con UnityEngine.XR.InputDevices.
/// </summary>
internal class VRRig : MonoBehaviour
{
    private Transform rig;
    private Transform head;
    private Camera xrCamera;

    // Cámara original y su estado previo, para restaurarlo si cambia
    private Camera source;
    private int sourceCullingMask;
    private RenderTexture sourceTarget;
    private bool sourcePostProcessing;
    private StereoTargetEyeMask sourceStereoEye;
    private RenderTexture dummyTarget;

    private InputDevice hmd;
    private InputDevice leftHand;
    private bool loggedLeftHand;
    private bool needsRecenter = true;
    private bool wasPresent;
    private bool recenterClickLatch;

    // Escala del mundo en vivo (grip izquierdo + stick izquierdo)
    private const float MinScale = 0.1f;
    private const float MaxScale = 100f;
    private const float StickDeadzone = 0.15f;
    private float worldScale;
    private bool scaling;
    private bool resetLatch;
    private Vector3 recenterPosition;
    private float recenterYaw;

    internal Transform Rig => rig;
    internal Camera XrCamera => xrCamera;
    internal Camera SourceCamera => source;

    /// <summary>Pose del espacio de tracking (como la del casco) al mundo, con el mismo recentrado.</summary>
    internal void TrackingToWorld(Vector3 pos, Quaternion rot, out Vector3 worldPos, out Quaternion worldRot)
    {
        var undoYaw = Quaternion.Euler(0f, -recenterYaw, 0f);
        worldPos = rig.TransformPoint(undoYaw * (pos - recenterPosition));
        worldRot = rig.rotation * (undoYaw * rot);
    }

    private static readonly FieldInfo RendererIndexField =
        typeof(UniversalAdditionalCameraData).GetField("m_RendererIndex", BindingFlags.Instance | BindingFlags.NonPublic);

    private void Awake()
    {
        rig = new GameObject("StrikeOpsXR_Rig").transform;
        DontDestroyOnLoad(rig.gameObject);

        head = new GameObject("StrikeOpsXR_Camera").transform;
        head.SetParent(rig, false);

        xrCamera = head.gameObject.AddComponent<Camera>();
        xrCamera.enabled = false;
        xrCamera.tag = "Untagged"; // Camera.main debe seguir siendo la del juego

        worldScale = Mathf.Clamp(Plugin.Cfg.WorldScale.Value, MinScale, MaxScale);

        Application.onBeforeRender += OnBeforeRender;
    }

    private void OnDestroy()
    {
        Application.onBeforeRender -= OnBeforeRender;
        ReleaseSource();
    }

    private void Update()
    {
        var key = Plugin.Cfg.RecenterKey.Value;
        if (Keyboard.current != null && key != Key.None && Keyboard.current[key].wasPressedThisFrame)
            needsRecenter = true;

        HandleScaleInput();
        HandleZoomInput();
    }

    /// <summary>
    /// Mantener grip izquierdo: stick izquierdo arriba/abajo = más grande/más pequeño (exponencial).
    /// Grip + clic del stick = volver a 1. Al soltar el grip se guarda en el .cfg.
    /// </summary>
    private void HandleScaleInput()
    {
        if (!leftHand.isValid)
        {
            leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!leftHand.isValid)
                return;
        }

        if (!loggedLeftHand)
        {
            loggedLeftHand = true;
            Plugin.Log.LogInfo($"Mando izquierdo: {leftHand.name} ({leftHand.characteristics})");
        }

        // Clic del stick izquierdo = recentrar
        leftHand.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out var stickClick);
        if (stickClick && !recenterClickLatch)
            needsRecenter = true;
        recenterClickLatch = stickClick;

        // El ajuste de escala con grip + stick está desactivado (el grip izquierdo es Espacio).
        // WorldScale se cambia en el .cfg.
        const bool grip = false;

        if (grip)
        {
            scaling = true;

            if (leftHand.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out var click) && click)
            {
                if (!resetLatch)
                {
                    worldScale = 1f;
                    resetLatch = true;
                    Plugin.Log.LogInfo("WorldScale = 1");
                }
            }
            else
            {
                resetLatch = false;
            }

            if (leftHand.TryGetFeatureValue(CommonUsages.primary2DAxis, out var stick) &&
                Mathf.Abs(stick.y) > StickDeadzone)
            {
                // Quitar la zona muerta para que el arranque sea suave
                var y = (Mathf.Abs(stick.y) - StickDeadzone) / (1f - StickDeadzone) * Mathf.Sign(stick.y);
                worldScale *= Mathf.Exp(y * Plugin.Cfg.ScaleSpeed.Value * Time.unscaledDeltaTime);
                worldScale = Mathf.Clamp(worldScale, MinScale, MaxScale);
            }
        }
        else if (scaling)
        {
            scaling = false;
            resetLatch = false;
            Plugin.Cfg.WorldScale.Value = Mathf.Round(worldScale * 100f) / 100f; // BepInEx guarda el .cfg
            Plugin.Log.LogInfo($"WorldScale guardado: {Plugin.Cfg.WorldScale.Value}");
        }
    }

    private void LateUpdate()
    {
        TrackSourceCamera();

        // Las escenas cargan vcams nuevas: reasignarlas de vez en cuando
        if (source != null && Time.unscaledTime >= nextVcamScan)
        {
            nextVcamScan = Time.unscaledTime + 1f;
            MoveVcamsToLayer();
        }

        UpdatePose();
    }

    // Justo antes de renderizar: pose más reciente del casco y posición final de Cinemachine
    private void OnBeforeRender()
    {
        // El seguimiento va aquí y no en LateUpdate: Fusion interpola el helicóptero visible en su propio
        // LateUpdate, así se usa siempre su posición final de este fotograma
        UpdateFollow();
        UpdatePose();
    }

    private void TrackSourceCamera()
    {
        var main = Camera.main;

        if (main == xrCamera)
            return;

        if (main == source && source != null)
            return;

        ReleaseSource();

        if (main == null)
        {
            xrCamera.enabled = false;
            return;
        }

        AttachSource(main);
    }

    private void AttachSource(Camera cam)
    {
        source = cam;
        Plugin.Log.LogInfo($"Cámara del juego: {cam.name} (escena {cam.gameObject.scene.name})");

        // Copia ajustes (clear, culling, clip, etc.). CopyFrom también copia el transform: se corrige en UpdatePose.
        xrCamera.CopyFrom(cam);
        xrCamera.targetTexture = null;
        xrCamera.stereoTargetEye = StereoTargetEyeMask.Both;
        xrCamera.depth = cam.depth;
        CopyUrpData(cam, xrCamera);

        // Neutralizar la original: sin geometría, a un RT del tamaño de la pantalla
        // (mismo tamaño => WorldToScreenPoint del HUD sigue dando coordenadas válidas)
        sourceCullingMask = cam.cullingMask;
        sourceTarget = cam.targetTexture;

        if (dummyTarget == null || dummyTarget.width != Screen.width || dummyTarget.height != Screen.height)
        {
            if (dummyTarget != null)
                dummyTarget.Release();
            dummyTarget = new RenderTexture(Mathf.Max(Screen.width, 16), Mathf.Max(Screen.height, 16), 16)
            {
                name = "StrikeOpsXR_Dummy"
            };
        }

        // Sin geometría, pero Cinemachine solo usa las cámaras virtuales cuya capa está en el
        // cullingMask de la cámara real: con 0 la cámara del juego se quedaba congelada (y con ella
        // el oyente de audio de FMOD). Las vcams se pasan a una capa vacía y la cámara ve solo esa.
        cam.cullingMask = 1 << VcamLayer;
        MoveVcamsToLayer();
        cam.targetTexture = dummyTarget;

        // Que deje de ser cámara estéreo: con XR activo Unity mueve sola las cámaras estéreo con la
        // pose del casco y pisa a Cinemachine, y la cámara del juego se quedaba clavada en el barco
        // (y con ella el AudioListener y los cálculos de pantalla del juego).
        sourceStereoEye = cam.stereoTargetEye;
        cam.stereoTargetEye = StereoTargetEyeMask.None;

        var srcData = cam.GetUniversalAdditionalCameraData();
        sourcePostProcessing = srcData.renderPostProcessing;
        srcData.renderPostProcessing = false;

        xrCamera.enabled = true;
        needsRecenter = true;

        PostFx.Apply();
    }

    private void ReleaseSource()
    {
        if (source != null)
        {
            source.cullingMask = sourceCullingMask;
            source.targetTexture = sourceTarget;
            source.stereoTargetEye = sourceStereoEye;
            source.GetUniversalAdditionalCameraData().renderPostProcessing = sourcePostProcessing;
        }

        source = null;
    }

    private static void CopyUrpData(Camera from, Camera to)
    {
        var src = from.GetUniversalAdditionalCameraData();
        var dst = to.GetUniversalAdditionalCameraData();

        dst.renderType = CameraRenderType.Base;
        dst.renderPostProcessing = src.renderPostProcessing;
        dst.antialiasing = src.antialiasing;
        dst.antialiasingQuality = src.antialiasingQuality;
        dst.renderShadows = src.renderShadows;
        dst.requiresColorOption = src.requiresColorOption;
        dst.requiresDepthOption = src.requiresDepthOption;
        dst.volumeLayerMask = src.volumeLayerMask;
        dst.volumeTrigger = src.volumeTrigger != null ? src.volumeTrigger : from.transform;
        dst.stopNaN = src.stopNaN;
        dst.dithering = src.dithering;
        dst.allowXRRendering = true;

        if (RendererIndexField != null)
            dst.SetRenderer((int)RendererIndexField.GetValue(src));

        // Cámaras overlay (si el juego apila alguna) también al casco
        dst.cameraStack.Clear();
        if (src.renderType == CameraRenderType.Base && src.cameraStack != null)
            dst.cameraStack.AddRange(src.cameraStack);
    }

    // ---- cámaras virtuales de Cinemachine ----

    private static int vcamLayer = -1;
    private static System.Type vcamType;
    private float nextVcamScan;

    /// <summary>Capa sin nombre (sin uso en el juego), empezando por la última.</summary>
    private static int VcamLayer
    {
        get
        {
            if (vcamLayer >= 0) return vcamLayer;
            vcamLayer = 31;
            for (var i = 31; i >= 8; i--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i)))
                {
                    vcamLayer = i;
                    break;
                }
            Plugin.Log.LogInfo($"Capa para las cámaras virtuales de Cinemachine: {vcamLayer}");
            return vcamLayer;
        }
    }

    private void MoveVcamsToLayer()
    {
        if (vcamType == null)
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                if ((vcamType = asm.GetType("Cinemachine.CinemachineVirtualCameraBase")) != null)
                    break;
        if (vcamType == null)
            return;

        var moved = 0;
        foreach (var o in Resources.FindObjectsOfTypeAll(vcamType))
            if (o is Component c && c.gameObject.scene.IsValid() && c.gameObject.layer != VcamLayer)
            {
                c.gameObject.layer = VcamLayer;
                moved++;
            }

        if (moved > 0)
            Plugin.Log.LogInfo($"{moved} cámaras virtuales de Cinemachine movidas a la capa {VcamLayer}");
    }

    // ---- seguimiento del helicóptero ----

    private bool following;
    private Vector3 followPos;          // posición del rig (antes de offsets de config)
    private Vector3 smoothedTarget;     // posición suavizada del jugador
    private Vector3 followOffset;       // cámara del juego - jugador, capturado
    private Transform followTarget;
    private float captureAt = -1f;

    /// <summary>
    /// La cámara del juego (CameraFollow + Cinemachine) deja al helicóptero alejarse mucho antes de
    /// moverse: en VR se sale de la vista. En partida el rig sigue al jugador directamente, con el
    /// mismo desplazamiento que tenía la cámara del juego respecto a él (capturado al aparecer,
    /// y de nuevo al recentrar).
    /// </summary>
    /// <summary>
    /// La raíz del jugador (Fusion NetworkRigidbody) se mueve a saltos, una vez por tic de red/física; lo que
    /// se ve es el vehículo, su InterpolationTarget, que se interpola en cada fotograma. Seguir la raíz hacía que
    /// el helicóptero diera pequeños tirones respecto a la vista.
    /// </summary>
    private static Transform FollowTransform(Robosaru.NetworkPlayer player) =>
        player.vehicle != null ? player.vehicle.transform : player.transform;

    private void UpdateFollow()
    {
        var player = Robosaru.NetworkPlayer.LocalPlayer;
        var target = Plugin.Cfg.FollowPlayer.Value && player != null && source != null ? FollowTransform(player) : null;

        if (target == null)
        {
            following = false;
            followTarget = null;
            return;
        }

        var now = Time.unscaledTime;

        // Jugador nuevo (aparece, reaparece, cambia de vehículo): esperar a que la cámara se coloque
        if (target != followTarget)
        {
            followTarget = target;
            following = false;
            captureAt = now + Plugin.Cfg.FollowCaptureDelay.Value;
        }

        if (needsRecenter && following)
            captureAt = now; // recentrar también vuelve a capturar el encuadre

        if (captureAt >= 0f && now >= captureAt)
        {
            captureAt = -1f;
            followOffset = source.transform.position - target.position;
            smoothedTarget = target.position;
            following = true;
            Plugin.Log.LogInfo($"Cámara VR sigue al jugador ({target.name}, desplazamiento {followOffset:F1})");
        }

        if (!following)
            return;

        // Suavizado exponencial independiente del framerate
        var sharp = Mathf.Max(0.1f, Plugin.Cfg.FollowSharpness.Value);
        smoothedTarget = Vector3.Lerp(smoothedTarget, target.position, 1f - Mathf.Exp(-sharp * Time.unscaledDeltaTime));
        followPos = smoothedTarget + Quaternion.Euler(0f, ViewYaw, 0f) * followOffset * cameraDistance;
    }

    // ---- distancia de la vista: grip derecho + stick derecho ----

    private const float MinDistance = 0.15f;
    private const float DefaultDistance = 0.75f; // al arrancar el juego (no se guarda en el .cfg)
    private static float MaxDistance => Mathf.Clamp(Plugin.Cfg.CameraMaxDistance.Value, MinDistance, 3f);
    private float cameraDistance = -1f;

    /// <summary>
    /// Giro de la vista (grados) alrededor del helicóptero respecto a la cámara del juego.
    /// Solo con FollowPlayer en partida. VRControls lo usa para girar el movimiento.
    /// </summary>
    internal static float ViewYaw;

    /// <summary>Giro realmente aplicado este frame (0 si no se sigue al helicóptero).</summary>
    internal static float AppliedYaw;
    private static bool viewYawLoaded;
    private InputDevice rightHand;
    private bool zooming;

    private void HandleZoomInput()
    {
        if (cameraDistance < 0f)
            cameraDistance = Mathf.Clamp(DefaultDistance, MinDistance, MaxDistance);
        if (!viewYawLoaded)
        {
            viewYawLoaded = true;
            ViewYaw = 0f;
        }

        if (!rightHand.isValid)
        {
            rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!rightHand.isValid)
                return;
        }

        // Stick derecho (sin botones): arriba/abajo = acercar/alejar, izquierda/derecha = girar.
        // No se guarda: cada vez que arranca el juego vuelve a 0.75 y 0°.
        rightHand.TryGetFeatureValue(CommonUsages.primary2DAxis, out var stick);
        var active = false;

        if (Mathf.Abs(stick.x) > StickDeadzone)
        {
            active = true;
            var x = (Mathf.Abs(stick.x) - StickDeadzone) / (1f - StickDeadzone) * Mathf.Sign(stick.x);
            ViewYaw = Mathf.Repeat(ViewYaw + x * Plugin.Cfg.CameraTurnSpeed.Value * Time.unscaledDeltaTime + 180f, 360f) - 180f;
        }

        if (Mathf.Abs(stick.y) > StickDeadzone)
        {
            active = true;
            var y = (Mathf.Abs(stick.y) - StickDeadzone) / (1f - StickDeadzone) * Mathf.Sign(stick.y);
            // Arriba = acercar
            cameraDistance *= Mathf.Exp(-y * Plugin.Cfg.ScaleSpeed.Value * Time.unscaledDeltaTime);
            cameraDistance = Mathf.Clamp(cameraDistance, MinDistance, MaxDistance);
        }

        if (active)
        {
            zooming = true;
        }
        else if (zooming)
        {
            zooming = false;
            if (Plugin.Cfg.Diagnostics.Value)
                Plugin.Log.LogInfo($"Vista: distancia {cameraDistance:F2}, giro {ViewYaw:F0}° (no se guarda: al arrancar vuelve a 0.75 y 0°)");
        }
    }

    private void UpdatePose()
    {
        if (source == null || !xrCamera.enabled)
            return;

        var cfg = Plugin.Cfg;
        var srcT = source.transform;

        // Rig: posición de la cámara del juego, solo yaw (o pitch también si se pide)
        var euler = srcT.eulerAngles;
        AppliedYaw = following ? ViewYaw : 0f;
        euler.y += AppliedYaw;
        var rigRot = cfg.FollowCameraPitch.Value
            ? Quaternion.Euler(euler.x, euler.y, 0f)
            : Quaternion.Euler(0f, euler.y, 0f);
        var yawOnly = Quaternion.Euler(0f, euler.y, 0f);

        // En partida: seguir al helicóptero (UpdateFollow). En menús: la cámara del juego.
        var basePos = following ? followPos : srcT.position;

        rig.SetPositionAndRotation(
            basePos + Vector3.up * cfg.HeightOffset.Value - yawOnly * Vector3.forward * cfg.BackOffset.Value,
            rigRot);
        rig.localScale = Vector3.one * worldScale;
        IpdScale.Camera = xrCamera;
        IpdScale.Scale = worldScale;

        // Cabeza: pose del HMD en el espacio de tracking
        if (!hmd.isValid)
            hmd = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);

        if (!hmd.isValid ||
            !hmd.TryGetFeatureValue(CommonUsages.centerEyePosition, out var pos) ||
            !hmd.TryGetFeatureValue(CommonUsages.centerEyeRotation, out var rot))
            return;

        // Recentrar al ponerse el casco; mientras no está puesto, no gastar el recentrado
        // (al arrancar el juego el casco puede estar en la mesa: la UI saldría desplazada)
        var hasPresence = hmd.TryGetFeatureValue(CommonUsages.userPresence, out var present);
        if (hasPresence)
        {
            if (present && !wasPresent)
                needsRecenter = true;
            wasPresent = present;
        }

        if (needsRecenter && (!hasPresence || present))
        {
            recenterPosition = pos;
            recenterYaw = rot.eulerAngles.y;
            needsRecenter = false;
            Plugin.Log.LogInfo("Vista recentrada");
        }

        var undoYaw = Quaternion.Euler(0f, -recenterYaw, 0f);
        head.localPosition = undoYaw * (pos - recenterPosition);
        head.localRotation = undoYaw * rot;
    }
}
