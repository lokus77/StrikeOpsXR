using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace StrikeOpsXR;

internal class ModConfig
{
    public readonly ConfigEntry<bool> DisableVR;
    public readonly ConfigEntry<bool> FocusWindow;
    public readonly ConfigEntry<bool> Diagnostics;
    public readonly ConfigEntry<string> OpenXRRuntimeJson;
    public readonly ConfigEntry<float> RenderScale;
    public readonly ConfigEntry<string> StereoRenderMode;

    public readonly ConfigEntry<float> WorldScale;
    public readonly ConfigEntry<bool> FollowCameraPitch;
    public readonly ConfigEntry<float> HeightOffset;
    public readonly ConfigEntry<float> BackOffset;
    public readonly ConfigEntry<Key> RecenterKey;
    public readonly ConfigEntry<float> ScaleSpeed;
    public readonly ConfigEntry<bool> FollowPlayer;
    public readonly ConfigEntry<float> FollowSharpness;
    public readonly ConfigEntry<float> FollowCaptureDelay;
    public readonly ConfigEntry<float> CameraMaxDistance;
    public readonly ConfigEntry<float> CameraTurnSpeed;

    public readonly ConfigEntry<bool> ControllersEnabled;

    public readonly ConfigEntry<bool> AimWithController;
    public readonly ConfigEntry<bool> ShowLaser;
    public readonly ConfigEntry<bool> VRButtonPrompts;
    public readonly ConfigEntry<bool> ShowReticle;
    public readonly ConfigEntry<float> AimMaxDistance;
    public readonly ConfigEntry<bool> Haptics;
    public readonly ConfigEntry<float> HapticStrength;

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
    public readonly ConfigEntry<bool> UiDisableBlurredBackground;
    public readonly ConfigEntry<float> CursorSize;

    public readonly ConfigEntry<bool> DisableDepthOfField;
    public readonly ConfigEntry<bool> DisableMotionBlur;
    public readonly ConfigEntry<bool> DisableLensEffects;
    public readonly ConfigEntry<bool> DisableCameraShake;

    public readonly ConfigEntry<float> FarClipMultiplier;
    public readonly ConfigEntry<float> ShadowDistance;
    public readonly ConfigEntry<float> LodBiasMultiplier;
    public readonly ConfigEntry<float> TerrainDetailMultiplier;
    public readonly ConfigEntry<float> TreeBillboardDistance;
    public readonly ConfigEntry<float> DetailDensity;
    public readonly ConfigEntry<float> OceanExtension;
    public readonly ConfigEntry<float> OceanExtensionDepth;
    public readonly ConfigEntry<string> DisabledRendererFeatures;
    public readonly ConfigEntry<bool> UncapFrameRate;
    public readonly ConfigEntry<float> ObjectCullDistance;
    public readonly ConfigEntry<string> ObjectCullExceptLayers;

    public ModConfig(ConfigFile file)
    {
        DisableVR = file.Bind("General", "DisableVR", false,
            "Arranca el juego en plano aunque el mod esté instalado (equivale a --disable-vr)");
        FocusWindow = file.Bind("General", "FocusWindow", true,
            "Al arrancar, si la ventana del juego queda minimizada o en segundo plano, la restaura y le da el foco");
        Diagnostics = file.Bind("General", "Diagnostics", false,
            "Trazas de prueba en el log: cada 5 s cámara, jugador, audio y rendimiento (FPS), apuntado, mandos y UI. Apagado = log limpio");
        OpenXRRuntimeJson = file.Bind("General", "OpenXRRuntimeJson", "",
            "Ruta a un JSON de runtime OpenXR para forzarlo (vacío = runtime por defecto del sistema)");
        RenderScale = file.Bind("Rendering", "RenderScale", 1.0f,
            "Solo el valor inicial: la resolución VR se ajusta en config.cfg junto a StrikeOps.exe (VRRenderScale / VREyeWidth)");
        StereoRenderMode = file.Bind("Rendering", "StereoRenderMode", "MultiPass",
            new ConfigDescription("Cómo se dibujan los dos ojos (requiere reiniciar el juego). MultiPass = la escena entera dos veces, " +
                "funciona siempre. SinglePass = los dos ojos a la vez (multiview en Vulkan): mucho menos procesador, pero solo se ve " +
                "bien si los shaders del juego lo soportan; con este juego no funciona (ojo izquierdo blanco, derecho negro): los shaders no traen esas variantes",
                new AcceptableValueList<string>("MultiPass", "SinglePass")));

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
        Haptics = file.Bind("Controls", "Haptics", true,
            "Vibración de los mandos VR (misiles e impactos). Además tiene que estar activada la vibración en las opciones del juego");
        HapticStrength = file.Bind("Controls", "HapticStrength", 1f,
            "Intensidad de la vibración (0 - 1)");
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
        UiDisableBlurredBackground = file.Bind("UI", "DisableBlurredBackground", true,
            "Quita el fondo desenfocado que el juego pone detrás de algunos menús (¿Seleccionar vehículo?, pausa). " +
            "En VR se ve como un recuadro gris opaco. Requiere reiniciar el juego");
        UiHideElements = file.Bind("UI", "HideElements", "VisualElement_Target",
            "Elementos de la interfaz (por nombre, separados por |) que se ocultan. Por defecto, VisualElement_Target (el recuadro, la mira y la línea de " +
            "puntos al objetivo fijado; también vale el principio del nombre), que el juego dibuja en 2D y en VR no coinciden con el mundo (ya está la mira 3D)");
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

        CameraMaxDistance = file.Bind("Rig", "CameraMaxDistance", 1.25f,
            "Lo más lejos que se puede alejar la vista con el stick derecho (arriba/abajo; al arrancar empieza en 0.75), respecto a la cámara del juego " +
            "(1 = igual que el juego, unos 220 m; 1.25 = unos 275 m). Entre 0.15 y 3");

        CameraTurnSpeed = file.Bind("Rig", "CameraTurnSpeed", 60f,
            "Velocidad de giro de la vista (grados por segundo con el stick a fondo)");

        DisableDepthOfField = file.Bind("PostFx", "DisableDepthOfField", true,
            "Desactiva profundidad de campo y tilt-shift (no funcionan bien en VR)");
        DisableMotionBlur = file.Bind("PostFx", "DisableMotionBlur", true,
            "Desactiva motion blur");
        DisableLensEffects = file.Bind("PostFx", "DisableLensEffects", true,
            "Desactiva distorsión de lente, aberración cromática y proyección Panini");
        DisableCameraShake = file.Bind("PostFx", "DisableCameraShake", true,
            "Quita el temblor de cámara al recibir impactos y con el turbo (la vibración de los mandos se mantiene)");

        OceanExtension = file.Bind("World", "OceanExtension", 8f,
            "El mar del juego es un plano de 7.5 x 10 km y en VR se ve su borde en el horizonte. Se añade debajo una copia " +
            "visual este número de veces más grande. 0 = desactivado. Coste de rendimiento muy bajo");
        OceanExtensionDepth = file.Bind("World", "OceanExtensionDepth", 0.5f,
            "Metros por debajo del mar original a los que se coloca la copia (para que no se mezclen dentro del mapa)");
        FarClipMultiplier = file.Bind("World", "FarClipMultiplier", 1f,
            "Distancia de dibujado respecto a la cámara del juego (1 = igual; el juego ya dibuja hasta 100 km)");
        ShadowDistance = file.Bind("World", "ShadowDistance", 450f,
            "Distancia de las sombras en metros (0 = la del juego, 600). Más bajo = más FPS");
        ObjectCullDistance = file.Bind("World", "ObjectCullDistance", 800f,
            "Distancia (m) a partir de la que no se dibujan los objetos (edificios, vehículos, decoración). En VR se mira " +
            "al horizonte y entra todo el mapa, lo que satura el procesador. El terreno y el mar se siguen viendo hasta el " +
            "horizonte. 0 = sin límite. Más bajo = más FPS");
        ObjectCullExceptLayers = file.Bind("World", "ObjectCullExceptLayers", "Terrain|Water|UI",
            "Capas que no se recortan nunca (nombres separados por |). Las capas salen en el log como '[Mundo] recorte de objetos'");
        UncapFrameRate = file.Bind("World", "UncapFrameRate", true,
            "Quita el límite de FPS de las opciones del juego (VSync / Max 60...). En VR los FPS los marca el casco");
        DisabledRendererFeatures = file.Bind("World", "DisabledRendererFeatures", "ScreenSpaceAmbientOcclusion",
            "Efectos del renderer que se desactivan, separados por | (en VR se calculan una vez por ojo). Los disponibles " +
            "salen en el log como '[Mundo] efectos del renderer'. Por ejemplo: ScreenSpaceAmbientOcclusion (sombreado de " +
            "contacto, caro), EdgeDetectionRenderer (contornos), FSRScriptableRenderFeature, DecalRendererFeature. Vacío = ninguno");
        LodBiasMultiplier = file.Bind("World", "LodBiasMultiplier", 2f,
            "Multiplica el nivel de detalle a distancia (LOD bias). Más alto = los árboles lejanos no desaparecen, " +
            "pero cuesta bastantes FPS. 1 = como el juego");
        TerrainDetailMultiplier = file.Bind("World", "TerrainDetailMultiplier", 1f,
            "Multiplica la distancia de la vegetación del terreno (hierba, arbustos; los árboles no). El juego la dibuja hasta 1000 m, que en VR " +
            "cuesta procesador. 0.5 = 500 m. 1 = como el juego. Más alto = menos FPS");
        DetailDensity = file.Bind("World", "DetailDensity", 1f,
            "Densidad de la vegetación del terreno (hierba, arbustos) respecto al juego (0 - 1). 0.5 = la mitad de matas. " +
            "Más bajo = más FPS");
        TreeBillboardDistance = file.Bind("World", "TreeBillboardDistance", 100f,
            "Hasta qué distancia (m) se dibujan los árboles en 3D; más allá el juego usa imágenes planas, que en VR " +
            "no se ven bien. 0 = como el juego (300). Más alto = cuesta FPS");
    }
}
