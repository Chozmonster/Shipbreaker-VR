using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ShipbreakerVr;

/// <summary>
/// Camera patch is isolated from menu patches. 0.4.4's Canvas.OnEnable patch used
/// DeclaredMethod on a method that lives on Behaviour — Harmony failed and aborted
/// CreateAndPatchAll before ModXrManager/VrCamera were created.
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
        // OnEnable is inherited (UIBehaviour/MonoBehaviour) — must use AccessTools.Method,
        // NOT DeclaredMethod / patch-type OnEnable on Canvas itself.
        var onEnable = AccessTools.Method(typeof(CanvasScaler), "OnEnable");
        if (onEnable == null)
        {
            ModLog.Warn("[WorldLock] CanvasScaler.OnEnable not found — menu VrUi attach skipped.");
            return;
        }

        harmony.Patch(onEnable,
            postfix: new HarmonyMethod(typeof(MenuPatches), nameof(AttachMenuVrUi)));
        ModLog.Info("[WorldLock] Harmony postfix on CanvasScaler.OnEnable applied (AccessTools.Method).");
    }

    private static void AttachMenuVrUi(CanvasScaler __instance)
    {
        try
        {
            var canvas = __instance.GetComponent<Canvas>();
            if (!canvas || !canvas.isRootCanvas) return;
            if (canvas.renderMode == RenderMode.WorldSpace) return;
            if (canvas.GetComponent<VrUi>()) return;
            if (canvas.GetComponent<Camera>()) return;
            if (canvas.GetComponentInParent<VrCamera>()) return;

            var n = canvas.gameObject.name;
            if (ContainsAny(n, "HUD", "Hud", "reticule", "reticle", "fade", "Fade", "Marker", "XR",
                    "VrCamera"))
            {
                ModLog.Info($"[WorldLock] skip CanvasScaler '{n}' (protected/non-menu)");
                return;
            }

            var looksMenu = ContainsAny(n, "Menu", "FrontEnd", "Pause", "Profile", "FreePlay", "Options",
                "Hab", "Inventory", "Weekly", "Level");
            if (!looksMenu && canvas.GetComponentInChildren<Selectable>(true) == null)
            {
                ModLog.Info($"[WorldLock] skip CanvasScaler '{n}' (no menu hint / selectables)");
                return;
            }

            canvas.gameObject.AddComponent<VrUi>();
            ModLog.Info($"[WorldLock][Menu] attached VrUi via CanvasScaler to '{n}'");
        }
        catch (Exception e)
        {
            ModLog.Warn("[WorldLock] AttachMenuVrUi exception: " + e.Message);
        }
    }

    private static bool ContainsAny(string hay, params string[] needles)
    {
        foreach (var n in needles)
            if (hay.IndexOf(n, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }
}
