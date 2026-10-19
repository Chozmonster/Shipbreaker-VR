using System;
using System.Collections;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace ShipbreakerVr;

public class ModXrManager : MonoBehaviour
{
    public static bool IsVrEnabled;
    public static ModXrManager Instance { get; private set; }

    private static OpenXRLoaderBase _openXrLoader;
    private bool isXrSetUp;
    private ConfigEntry<bool> autoEnterVr;
    private ConfigEntry<bool> allowF3Toggle;

    private static bool IsInitialized => _openXrLoader != null && _openXrLoader.GetValue<bool>("isInitialized");

    public void InitConfig(ConfigFile config)
    {
        autoEnterVr = config.Bind(
            "VR",
            "AutoEnterVr",
            true,
            "Enter VR automatically on launch (no F3). Default: true.");
        allowF3Toggle = config.Bind(
            "VR",
            "AllowF3Toggle",
            true,
            "Allow F3 to toggle VR on/off for debugging. Default: true.");
    }

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (autoEnterVr == null || !autoEnterVr.Value) return;
        StartCoroutine(AutoEnterVrWhenReady());
    }

    private void Update()
    {
        if (allowF3Toggle != null && allowF3Toggle.Value && Input.GetKeyDown(KeyCode.F3))
            ToggleXr();
    }

    private IEnumerator AutoEnterVrWhenReady()
    {
        // Give the game one frame to finish bootstrapping before OpenXR init.
        yield return null;
        yield return null;

        if (IsVrEnabled) yield break;

        try
        {
            SetVrEnabled(true);
            Debug.Log(IsVrEnabled
                ? "[ShipbreakerVr] Auto-entered VR (no F3 required)."
                : "[ShipbreakerVr] Auto-enter VR attempted but OpenXR did not initialize. Check SteamVR/Meta OpenXR runtime, then try F3.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[ShipbreakerVr] Auto-enter VR failed: {e}");
        }
    }

    private void ToggleXr()
    {
        SetVrEnabled(!IsVrEnabled);
    }

    public void SetVrEnabled(bool enabled)
    {
        if (!isXrSetUp) SetUpXr();

        if (enabled == IsVrEnabled) return;

        if (enabled)
        {
            XRGeneralSettings.Instance.Manager.StartSubsystems();
            XRGeneralSettings.Instance.Manager.activeLoader.Initialize();
            XRGeneralSettings.Instance.Manager.activeLoader.Start();
        }
        else
        {
            XRGeneralSettings.Instance.Manager.activeLoader.Stop();
            XRGeneralSettings.Instance.Manager.activeLoader.Deinitialize();
        }

        IsVrEnabled = IsInitialized;
        Debug.Log($"[ShipbreakerVr] VR {(IsVrEnabled ? "enabled" : "disabled")}.");
    }

    private void SetUpXr()
    {
        isXrSetUp = true;

        var xrManagerBundle = VrAssetManager.LoadBundle("xrmanager");

        foreach (var xrManager in xrManagerBundle.LoadAllAssets())
            Debug.Log($"[ShipbreakerVr] Loaded xrManager asset: {xrManager.name}");

        var instance = XRGeneralSettings.Instance;
        if (instance == null) throw new Exception("XRGeneralSettings instance is null");

        var xrManagerSettings = instance.Manager;
        if (xrManagerSettings == null) throw new Exception("XRManagerSettings instance is null");

        xrManagerSettings.InitializeLoaderSync();
        if (xrManagerSettings.activeLoader == null) throw new Exception("Cannot initialize OpenXR Loader");

        _openXrLoader = xrManagerSettings.ActiveLoaderAs<OpenXRLoaderBase>();

        // Force-load OpenXRSettings so OpenXR feature types resolve with the asset bundle.
        // TODO: replace with an explicit assembly load if this ever stops working.
        _ = OpenXRSettings.Instance;
    }
}
