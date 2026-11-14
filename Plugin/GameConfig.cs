using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.XR;

namespace StrikeOpsXR;

/// <summary>
/// config.cfg junto a StrikeOps.exe, formato clave=valor (# = comentario).
/// Se relee al guardar el fichero, sin reiniciar el juego.
///   VRRenderScale = 1.0   factor sobre la resolución recomendada por el runtime (0.3 - 2.5)
///   VREyeWidth    = 0     ancho por ojo en píxeles; si es > 0 tiene prioridad sobre VRRenderScale
/// </summary>
internal class GameConfig : MonoBehaviour
{
    private const float MinScale = 0.3f, MaxScale = 2.5f;

    private static string FilePath => Path.Combine(Paths.GameRootPath, "config.cfg");

    private DateTime lastWrite;
    private float nextCheck;
    private float baseWidth = -1f;   // ancho recomendado por el runtime con escala 1

    private const string Template =
        "# StrikeOpsXR - resolución VR\r\n" +
        "# Se aplica al guardar este fichero, sin reiniciar el juego.\r\n" +
        "#\r\n" +
        "# VRRenderScale: factor sobre la resolución recomendada por el runtime (0.3 - 2.5).\r\n" +
        "#   1.0 = la recomendada, 1.2 = más nítido (más GPU), 0.8 = más rápido.\r\n" +
        "VRRenderScale=1.0\r\n" +
        "#\r\n" +
        "# VREyeWidth: ancho en píxeles de cada ojo. Si es mayor que 0, manda sobre VRRenderScale.\r\n" +
        "#   El alto sigue la proporción del casco. 0 = usar VRRenderScale.\r\n" +
        "VREyeWidth=0\r\n";

    private void Start()
    {
        if (!File.Exists(FilePath))
        {
            try
            {
                // Valor inicial: el RenderScale que hubiera en el .cfg de BepInEx
                var initial = Template.Replace("VRRenderScale=1.0",
                    "VRRenderScale=" + Plugin.Cfg.RenderScale.Value.ToString("0.0#", CultureInfo.InvariantCulture));
                File.WriteAllText(FilePath, initial);
                Plugin.Log.LogInfo($"Creado {FilePath}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"No se pudo crear {FilePath}: {ex.Message}");
            }
        }

        Apply();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextCheck)
            return;
        nextCheck = Time.unscaledTime + 1f;

        try
        {
            // La resolución recomendada solo se conoce cuando la sesión OpenXR ya está en marcha
            if ((baseWidth < 0f && XRSettings.eyeTextureWidth > 0) ||
                (File.Exists(FilePath) && File.GetLastWriteTimeUtc(FilePath) != lastWrite))
                Apply();
        }
        catch { /* fichero en uso mientras se guarda: se reintenta */ }
    }

    private void Apply()
    {
        var values = Read();
        if (values == null)
            return;

        // Ancho recomendado con escala 1 (se mide una vez, con la escala actual)
        if (baseWidth < 0f && XRSettings.eyeTextureWidth > 0)
            baseWidth = XRSettings.eyeTextureWidth / Mathf.Max(0.01f, XRSettings.eyeTextureResolutionScale);

        var scale = GetFloat(values, "VRRenderScale", 1f);
        var width = GetFloat(values, "VREyeWidth", 0f);

        if (width > 0f && baseWidth > 0f)
            scale = width / baseWidth;

        scale = Mathf.Clamp(scale, MinScale, MaxScale);
        XRSettings.eyeTextureResolutionScale = scale;

        var w = baseWidth > 0f ? Mathf.RoundToInt(baseWidth * scale) : 0;
        var aspect = XRSettings.eyeTextureWidth > 0 ? (float)XRSettings.eyeTextureHeight / XRSettings.eyeTextureWidth : 0f;
        Plugin.Log.LogInfo($"Resolución VR: escala {scale:F2}" +
                           (w > 0 ? $" ≈ {w}x{Mathf.RoundToInt(w * aspect)} por ojo (recomendada {baseWidth:F0} de ancho)" : ""));
    }

    private Dictionary<string, string> Read()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new Dictionary<string, string>();

            lastWrite = File.GetLastWriteTimeUtc(FilePath);
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in File.ReadAllLines(FilePath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    continue;
                var i = line.IndexOf('=');
                if (i > 0)
                    d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
            }
            return d;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"No se pudo leer {FilePath}: {ex.Message}");
            return null;
        }
    }

    private static float GetFloat(Dictionary<string, string> d, string key, float def) =>
        d.TryGetValue(key, out var s) &&
        float.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : def;
}
