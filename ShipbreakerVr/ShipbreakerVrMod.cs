using System;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace ShipbreakerVr;

[BepInPlugin("ShipbreakerVr", "ShipbreakerVr", "0.4.7")]
public class ShipbreakerVrMod : BaseUnityPlugin
{
    private bool enableLights = true;
    private List<Light> lights;
    private Harmony harmony;

    private void Awake()
    {
        ModLog.Init(Logger);
        ModLog.Info($"======= ShipbreakerVr 0.4.7 starting (game {Application.version}) =======");
        ModLog.Info(
            $"Camera independent. Menus TargetDistance={MenuWorldPlacement.TargetDistance}m. " +
            "Laser=XRNode hand world pose. Tags: [XR] [VrCamera] [WorldLock] [UiPointer]");

        harmony = new Harmony("ShipbreakerVr");

        // 1) Camera FIRST — must succeed even if menu patching fails.
        try
        {
            CameraPatches.Apply(harmony);
        }
        catch (Exception e)
        {
            ModLog.Error("[VrCamera] FAILED to apply camera Harmony patch: " + e);
        }

        // 2) Menu patches — isolated; failure must not block XR components.
        try
        {
            MenuPatches.Apply(harmony);
        }
        catch (Exception e)
        {
            ModLog.Warn("[WorldLock] menu Harmony patch failed (XR/camera still proceed): " + e.Message);
        }

        // 3) Always create runtime components (0.4.4 never reached here when Harmony aborted).
        try
        {
            var xr = gameObject.AddComponent<ModXrManager>();
            xr.InitConfig(Config);
            ModLog.Info("[XR] ModXrManager component added.");
        }
        catch (Exception e)
        {
            ModLog.Error("[XR] Failed to add ModXrManager: " + e);
        }

        try
        {
            gameObject.AddComponent<VrMenuWorldLock>();
            gameObject.AddComponent<VrUiLaser>();
            ModLog.Info("[WorldLock][UiPointer] VrMenuWorldLock + VrUiLaser added (0.4.7 closer menus + hand-space laser).");
        }
        catch (Exception e)
        {
            ModLog.Warn("Menu components failed to add: " + e.Message);
        }

        ModLog.Info("======= ShipbreakerVr 0.4.7 Awake complete =======");
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F5)) SetPotatoQuality();
        if (Input.GetKeyDown(KeyCode.F6)) DisableLights();
    }

    private static void SetPotatoQuality()
    {
        QualitySettings.SetQualityLevel(0);
        ModLog.Info("Potato quality (F5).");
    }

    private void DisableLights()
    {
        enableLights = !enableLights;

        if (!enableLights)
        {
            lights = new List<Light>();
            foreach (var light in FindObjectsOfType<Light>())
            {
                if (light.enabled) lights.Add(light);
                light.enabled = false;
            }

            ModLog.Info("Lights disabled (F6).");
        }
        else
        {
            foreach (var light in lights) light.enabled = true;
            ModLog.Info("Lights restored (F6).");
        }
    }
}
