using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ShipbreakerVr;

/// <summary>
/// Camera patch isolated from menu patches. Never abort Awake on menu patch failure.
/// </summary>
public static class CameraPatches
{
    public static void Apply(Harmony harmony)
    {
        var start = AccessTools.Method(typeof(LynxCameraController), "Start");
        if (start == null)
        {
            ModLog.Error("[VrCamera] AccessTools could not find LynxCameraController.Start");
            return;
        }

        harmony.Patch(start,
            postfix: new HarmonyMethod(typeof(CameraPatches), nameof(CreateVrCamera)));
        ModLog.Info("[VrCamera] Harmony postfix on LynxCameraController.Start applied.");
    }

    private static void CreateVrCamera(LynxCameraController __instance)
    {
        try
        {
            var camera = __instance.GetComponent<Camera>();
            if (!camera)
            {
                ModLog.Error("[VrCamera] Couldn't find Main Camera on LynxCameraController");
                return;
            }

            VrCamera.Create(camera);
            ModLog.Info("[VrCamera] hooked on LynxCameraController.Start — stereo path OK");
        }
        catch (Exception e)
        {
            ModLog.Error("[VrCamera] postfix exception: " + e);
        }
    }
}

public static class MenuPatches
{
    public static void Apply(Harmony harmony)
    {
        // 0.4.6: do NOT attach VrUi (stock LateUpdate HMD-follow fought WorldLock in 0.4.5).
        // VrMenuWorldLock owns FrontEnd canvases. Keep a log-only hook for diagnostics.
        var onEnable = AccessTools.Method(typeof(CanvasScaler), "OnEnable");
        if (onEnable == null)
        {
            ModLog.Warn("[WorldLock] CanvasScaler.OnEnable not found — diagnostic hook skipped.");
            return;
        }

        harmony.Patch(onEnable,
            postfix: new HarmonyMethod(typeof(MenuPatches), nameof(LogMenuCanvas)));
        ModLog.Info(
            "[WorldLock] CanvasScaler.OnEnable diagnostic hook applied (no VrUi attach — WorldLock owns menus).");
    }

    private static void LogMenuCanvas(CanvasScaler __instance)
    {
        try
        {
            var canvas = __instance.GetComponent<Canvas>();
            if (!canvas || !canvas.isRootCanvas) return;
            var n = canvas.gameObject.name;
            if (n.IndexOf("FrontEnd", StringComparison.OrdinalIgnoreCase) < 0 &&
                n.IndexOf("Frontend", StringComparison.OrdinalIgnoreCase) < 0 &&
                n.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) < 0 &&
                n.IndexOf("Pause", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            ModLog.Info(
                $"[WorldLock][Menu] CanvasScaler saw '{n}' mode={canvas.renderMode} " +
                $"(WorldLock will lock — not attaching VrUi)");
        }
        catch (Exception e)
        {
            ModLog.Warn("[WorldLock] LogMenuCanvas: " + e.Message);
        }
    }
}
