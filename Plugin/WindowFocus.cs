using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace StrikeOpsXR;

/// <summary>
/// Al arrancar con el runtime OpenXR (Virtual Desktop) la ventana del juego puede quedarse minimizada
/// o detrás de otras. Durante los primeros segundos se restaura y se le da el foco de Windows.
/// </summary>
internal class WindowFocus : MonoBehaviour
{
    private const float Duration = 30f;
    private float next;
    private int focusedChecks;
    private IntPtr hwnd;

    private void Update()
    {
        if (Time.realtimeSinceStartup > Duration || focusedChecks >= 3)
        {
            enabled = false;
            return;
        }
        if (Time.realtimeSinceStartup < next)
            return;
        next = Time.realtimeSinceStartup + 1f;

        try
        {
            if (hwnd == IntPtr.Zero)
                hwnd = FindGameWindow();
            if (hwnd == IntPtr.Zero)
                return;

            var minimized = IsIconic(hwnd);
            if (!minimized && GetForegroundWindow() == hwnd)
            {
                focusedChecks++; // en primer plano: se comprueba un par de veces más por si otro programa lo quita
                return;
            }

            focusedChecks = 0;
            if (minimized)
                ShowWindow(hwnd, SW_RESTORE);
            ForceForeground(hwnd);
            Plugin.Log.LogInfo($"Ventana del juego {(minimized ? "restaurada y " : "")}puesta en primer plano");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"No se pudo dar el foco a la ventana: {ex.Message}");
            enabled = false;
        }
    }

    /// <summary>
    /// Windows solo deja tomar el primer plano a quien tiene la última entrada del usuario: se engancha
    /// temporalmente el hilo de la ventana activa para que lo permita.
    /// </summary>
    private static void ForceForeground(IntPtr h)
    {
        var fg = GetForegroundWindow();
        var fgThread = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, out _) : 0u;
        var myThread = GetCurrentThreadId();
        var attached = fgThread != 0 && fgThread != myThread && AttachThreadInput(myThread, fgThread, true);
        try
        {
            BringWindowToTop(h);
            SetForegroundWindow(h);
            SetFocus(h);
        }
        finally
        {
            if (attached)
                AttachThreadInput(myThread, fgThread, false);
        }
    }

    private static IntPtr FindGameWindow()
    {
        var pid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        var found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var p);
            if (p != pid)
                return true;
            var cls = new StringBuilder(64);
            GetClassName(h, cls, cls.Capacity);
            if (cls.ToString() != "UnityWndClass")
                return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private const int SW_RESTORE = 9;
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder s, int max);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
