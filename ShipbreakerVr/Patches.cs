using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ShipbreakerVr;

[HarmonyPatch]
public static class Patches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(LynxCameraController), nameof(LynxCameraController.Start))]
    private static void CreateVrCamera(LynxCameraController __instance)
    {
        var camera = __instance.GetComponent<Camera>();
        if (!camera)
        {
            ModLog.Error("[VrCamera] Couldn't find Main Camera on LynxCameraController");
            return;
        }

        VrCamera.Create(camera);
        ModLog.Info("[VrCamera] hooked on LynxCameraController.Start");
    }

    /// <summary>
    /// Still observe CanvasScaler enables for diagnostics. Actual world-lock is
    /// performed by VrMenuWorldLock scanning all root canvases (Doozy menus may
    /// not go through this path alone).
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CanvasScaler), "OnEnable")]
    private static void ObserveCanvasScaler(CanvasScaler __instance)
    {
        var canvas = __instance.GetComponent<Canvas>();
        if (!canvas || !canvas.isRootCanvas) return;
        ModLog.Info(
            $"[WorldLock][Menu] CanvasScaler.OnEnable on '{canvas.name}' mode={canvas.renderMode} " +
            $"(scanner will world-lock if VR on)");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Canvas), "OnEnable")]
    private static void ObserveCanvas(Canvas __instance)
    {
        if (!__instance || !__instance.isRootCanvas) return;
        ModLog.Info(
            $"[WorldLock][Menu] Canvas.OnEnable '{__instance.name}' mode={__instance.renderMode} " +
            $"camera={( __instance.worldCamera ? __instance.worldCamera.name : "null")}");
    }
}
