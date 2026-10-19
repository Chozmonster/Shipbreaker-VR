using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace ShipbreakerVr;

[BepInPlugin("ShipbreakerVr", "ShipbreakerVr", "0.4.4")]
public class ShipbreakerVrMod : BaseUnityPlugin
{
    private bool enableLights = true;
    private List<Light> lights;

    private void Awake()
    {
        ModLog.Init(Logger);
        ModLog.Info($"======= ShipbreakerVr 0.4.4 starting (game {Application.version}) =======");
        ModLog.Info("Expect log tags: [WorldLock] [UiPointer] [VrCamera] [XR]");

        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
        ModLog.Info("Harmony patches applied.");

        var xr = gameObject.AddComponent<ModXrManager>();
        xr.InitConfig(Config);

        gameObject.AddComponent<VrMenuWorldLock>();
        gameObject.AddComponent<VrUiLaser>();

        ModLog.Info("Components: ModXrManager, VrMenuWorldLock, VrUiLaser.");
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
