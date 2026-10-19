using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ShipbreakerVr;

[HarmonyPatch]
public static class Patches
{
    // HarmonyX injects the patched object as __instance (double underscore).
    // A bare "instance" param is treated as a real method argument and fails on
    // parameterless Start()/OnEnable() — which left VR camera unhooked (theater mode).

    [HarmonyPostfix]
    [HarmonyPatch(typeof(LynxCameraController), nameof(LynxCameraController.Start))]
    private static void CreateVrCamera(LynxCameraController __instance)
    {
        var camera = __instance.GetComponent<Camera>();
        if (!camera)
        {
            Debug.LogError("[ShipbreakerVr] Couldn't find Main Camera in LynxCameraController");
            return;
        }

        VrCamera.Create(camera);
        Debug.Log("[ShipbreakerVr] VrCamera hooked on LynxCameraController.Start");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CanvasScaler), "OnEnable")]
    private static void MoveCanvasesToWorldSpace(CanvasScaler __instance)
    {
        var canvas = __instance.GetComponent<Canvas>();

        if (!canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) return;

        canvas.gameObject.AddComponent<VrUi>();
    }
}
