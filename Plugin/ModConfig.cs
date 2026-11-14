using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace StrikeOpsXR;

internal class ModConfig
{
    public readonly ConfigEntry<bool> DisableVR;
    public readonly ConfigEntry<bool> Diagnostics;
    public readonly ConfigEntry<string> OpenXRRuntimeJson;
    public readonly ConfigEntry<float> RenderScale;

    public readonly ConfigEntry<float> WorldScale;
    public readonly ConfigEntry<bool> FollowCameraPitch;
    public readonly ConfigEntry<float> HeightOffset;
    public readonly ConfigEntry<float> BackOffset;
    public readonly ConfigEntry<Key> RecenterKey;
    public readonly ConfigEntry<float> ScaleSpeed;
    public readonly ConfigEntry<bool> FollowPlayer;
    public readonly ConfigEntry<float> FollowSharpness;
    public readonly ConfigEntry<float> FollowCaptureDelay;
    public readonly ConfigEntry<float> CameraDistance;
    public readonly ConfigEntry<float> CameraYaw;
    public readonly ConfigEntry<float> CameraTurnSpeed;

    public readonly ConfigEntry<bool> ControllersEnabled;

    public readonly ConfigEntry<bool> AimWithController;
    public readonly ConfigEntry<bool> ShowLaser;
    public readonly ConfigEntry<bool> VRButtonPrompts;
    public readonly ConfigEntry<bool> ShowReticle;
    public readonly ConfigEntry<float> AimMaxDistance;

    public readonly ConfigEntry<bool> UiEnabled;
    public readonly ConfigEntry<float> UiDistance;
    public readonly ConfigEntry<float> UiWidth;
    public readonly ConfigEntry<float> UiHeight;
    public readonly ConfigEntry<bool> UiFlipY;
    public readonly ConfigEntry<bool> ShowCursor;
    public readonly ConfigEntry<bool> UiTransparentBackdrops;
    public readonly ConfigEntry<float> UiBackdropMinArea;
    public readonly ConfigEntry<string> UiReplaceImageFile;
    public readonly ConfigEntry<string> UiReplaceImageMatch;
    public readonly ConfigEntry<bool> UiReplaceImageFullScreen;
    public readonly ConfigEntry<string> UiHideTexts;
    public readonly ConfigEntry<string> UiHideElements;
    public readonly ConfigEntry<float> CursorSize;

    public readonly ConfigEntry<bool> DisableDepthOfField;
    public readonly ConfigEntry<bool> DisableMotionBlur;
    public readonly ConfigEntry<bool> DisableLensEffects;

    public ModConfig(ConfigFile file)
    {
        DisableVR = file.Bind("General", "DisableVR", false,
            "Arranca el juego en plano aunque el mod esté instalado (equivale a --disable-vr)");
        Diagnostics = file.Bind("General", "Diagnostics", true,
            "Escribe cada 5 s en el log la posición de la cámara del juego, el jugador y los oyentes de audio");
        OpenXRRuntimeJson = file.Bind("General", "OpenXRRuntimeJson", "",
            "Ruta a un JSON de runtime OpenXR para forzarlo (vacío = runtime por defecto del sistema)");
        RenderScale = file.Bind("Rendering", "RenderScale", 1.0f,
            "Solo el valor inicial: la resolución VR se ajusta en config.cfg junto a StrikeOps.exe (VRRenderScale / VREyeWidth)");

        WorldScale = file.Bind("Rig", "WorldScale", 30.0f,
            "Escala del rig VR. >1 = te haces gigante y el mundo parece una maqueta (efecto diorama)");
        FollowCameraPitch = file.Bind("Rig", "FollowCameraPitch", false,
            "true = el rig hereda la inclinación de la cámara del juego. false = horizonte nivelado (más cómodo)");
        HeightOffset = file.Bind("Rig", "HeightOffset", 0f,
            "Desplazamiento vertical del rig respecto a la cámara del juego (metros del mundo)");
        BackOffset = file.Bind("Rig", "BackOffset", 0f,
            "Desplazamiento hacia atrás del rig respecto a la cámara del juego (metros del mundo)");
        RecenterKey = file.Bind("Rig", "RecenterKey", Key.F8,
            "Tecla para recentrar la vista");
        ScaleSpeed = file.Bind("Rig", "ScaleSpeed", 1.0f,
            "Velocidad del ajuste de escala con grip izquierdo + stick (1 = x2.7 por segundo a fondo)");

        ControllersEnabled = file.Bind("Controls", "Enabled", true,
            "Mandos VR: Y = E (mapa), A = Enter, stick izquierdo = flechas en menús / movimiento en juego");

        AimWithController = file.Bind("Controls", "AimWithController", true,
            "Apuntar con el mando derecho: mueve el ratón al punto del mundo al que apunta el mando");
        ShowLaser = file.Bind("Controls", "ShowLaser", false,
            "Dibuja un láser desde el mando derecho hasta el punto apuntado");

        ShowReticle = file.Bind("Controls", "ShowReticle", true,
            "Mira amarilla en el mundo 3D en el punto al que apunta el juego");
        AimMaxDistance = file.Bind("Controls", "AimMaxDistance", 400f,
            "Si el mando apunta lejos o al cielo y no toca nada: distancia (m) del helicóptero a la que se apunta");
        VRButtonPrompts = file.Bind("Controls", "VRButtonPrompts", true,
            "Las indicaciones en pantalla muestran el botón del mando VR (A, B, X, Y...) en vez de la tecla");

        UiEnabled = file.Bind("UI", "Enabled", true,
            "Muestra el HUD y los menús 2D en un panel flotante dentro del casco");
        UiDistance = file.Bind("UI", "Distance", 1.5f,
            "Distancia del panel a la cabeza (metros reales)");
        UiWidth = file.Bind("UI", "Width", 1.8f,
            "Ancho del panel (metros reales). El alto sigue la proporción de la pantalla");
        UiHeight = file.Bind("UI", "Height", -0.1f,
            "Altura del centro del panel respecto a los ojos (metros reales)");
        UiFlipY = file.Bind("UI", "FlipY", false,
            "Invierte el panel verticalmente (si se ve boca abajo)");
        UiTransparentBackdrops = file.Bind("UI", "TransparentBackdrops", true,
            "Hace transparentes los rellenos de color grandes (fondo negro del loading, recuadros de menú)");
        UiBackdropMinArea = file.Bind("UI", "BackdropMinArea", 0.3f,
            "Fracción mínima de pantalla (0-1) que debe ocupar un relleno para hacerlo transparente");
        UiReplaceImageFile = file.Bind("UI", "ReplaceImageFile", "splash.png",
            "PNG que sustituye a una imagen de la interfaz (ruta absoluta o relativa a la carpeta del plugin). Vacío = no sustituir");
        UiReplaceImageMatch = file.Bind("UI", "ReplaceImageMatch", "gamepad",
            "Se sustituye la imagen cuyo nombre (o el del elemento) contiene este texto. Los nombres salen en el log como 'Imagen UI:'");
        UiReplaceImageFullScreen = file.Bind("UI", "ReplaceImageFullScreen", true,
            "true = la imagen de sustitución ocupa toda la pantalla (ajustada sin deformar); false = en el tamaño del elemento original");
        UiHideTexts = file.Bind("UI", "HideTexts", "best with gamepad|best with a gamepad|mejor con mando|mejor con un mando",
            "Textos de la interfaz que se ocultan si contienen alguna de estas frases (separadas por |, sin distinguir mayúsculas)");
        UiHideElements = file.Bind("UI", "HideElements", "VisualElement_TargetAim|VisualElement_TargetHelper",
            "Elementos de la interfaz (por nombre, separados por |) que se ocultan. Por defecto, la mira y la línea de " +
            "puntos al objetivo fijado, que el juego dibuja en 2D y en VR no coinciden con el mundo (ya está la mira 3D)");
        ShowCursor = file.Bind("UI", "ShowCursor", true,
            "Dibuja el cursor del ratón sobre el panel");
        CursorSize = file.Bind("UI", "CursorSize", 0.04f,
            "Tamaño del cursor (metros reales)");

        FollowPlayer = file.Bind("Rig", "FollowPlayer", true,
            "En partida la vista VR sigue al helicóptero (true) o a la cámara del juego (false)");
        FollowSharpness = file.Bind("Rig", "FollowSharpness", 4f,
            "Rapidez del seguimiento (más alto = más pegado al helicóptero, más bajo = más suave)");
        FollowCaptureDelay = file.Bind("Rig", "FollowCaptureDelay", 2f,
            "Segundos tras aparecer antes de fijar la distancia/ángulo de seguimiento (deja que la cámara del juego se coloque)");

        CameraDistance = file.Bind("Rig", "CameraDistance", 0.75f,
            "Distancia de la vista al helicóptero respecto a la de la cámara del juego (1 = igual, <1 = más cerca). " +
            "En juego: stick derecho arriba/abajo");

        CameraYaw = file.Bind("Rig", "CameraYaw", 0f,
            "Giro de la vista alrededor del helicóptero (grados). En juego: stick derecho izquierda/derecha");
        CameraTurnSpeed = file.Bind("Rig", "CameraTurnSpeed", 60f,
            "Velocidad de giro de la vista (grados por segundo con el stick a fondo)");

        DisableDepthOfField = file.Bind("PostFx", "DisableDepthOfField", true,
            "Desactiva profundidad de campo y tilt-shift (no funcionan bien en VR)");
        DisableMotionBlur = file.Bind("PostFx", "DisableMotionBlur", true,
            "Desactiva motion blur");
        DisableLensEffects = file.Bind("PostFx", "DisableLensEffects", true,
            "Desactiva distorsión de lente, aberración cromática y proyección Panini");
    }
}
