using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace ShipbreakerVr;

[BepInPlugin("ShipbreakerVr", "ShipbreakerVr", "0.4.0")]
public class ShipbreakerVrMod : BaseUnityPlugin
{
    private bool enableLights = true;
    private List<Light> lights;

    private void Awake()
    {
        Debug.Log($"Loaded ShipbreakerVr {Info.Metadata.Version}. Game version: {Application.version}");

        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());

        var xr = gameObject.AddComponent<ModXrManager>();
        xr.InitConfig(Config);

        gameObject.AddComponent<VrUiLaser>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F5)) SetPotatoQuality();
        if (Input.GetKeyDown(KeyCode.F6)) DisableLights();
    }

    private static void SetPotatoQuality()
    {
        QualitySettings.SetQualityLevel(0);
        Debug.Log("[ShipbreakerVr] Potato quality (F5).");
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

            Debug.Log("[ShipbreakerVr] Lights disabled (F6).");
        }
        else
        {
            foreach (var light in lights) light.enabled = true;
            Debug.Log("[ShipbreakerVr] Lights restored (F6).");
        }
    }
}
