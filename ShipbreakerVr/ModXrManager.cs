using System;
using System.Collections;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace ShipbreakerVr;

public class ModXrManager : MonoBehaviour
{
    public static bool IsVrEnabled;
    public static ModXrManager Instance { get; private set; }

    private static OpenXRLoaderBase _openXrLoader;
    private bool isXrSetUp;
    private bool autoEnterRunning;
    private ConfigEntry<bool> autoEnterVr;
    private ConfigEntry<bool> allowF3Toggle;
    private ConfigEntry<int> autoEnterMaxAttempts;
    private ConfigEntry<float> autoEnterRetrySeconds;

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
        autoEnterMaxAttempts = config.Bind(
            "VR",
            "AutoEnterMaxAttempts",
            40,
            "How many times to retry OpenXR start before giving up (retries also fire on scene loads).");
        autoEnterRetrySeconds = config.Bind(
            "VR",
            "AutoEnterRetrySeconds",
            1.0f,
            "Seconds between auto-enter retries while OpenXR/SteamVR is still coming up.");
    }

    private void Awake()
    {
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        Debug.Log("[ShipbreakerVr] ModXrManager started. AutoEnterVr=" +
                  (autoEnterVr != null && autoEnterVr.Value));
        if (autoEnterVr == null || !autoEnterVr.Value) return;
        EnsureAutoEnterLoop();
    }

    private void Update()
    {
        if (allowF3Toggle != null && allowF3Toggle.Value && Input.GetKeyDown(KeyCode.F3))
            ToggleXr();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (IsVrEnabled) return;
        if (autoEnterVr == null || !autoEnterVr.Value) return;
        Debug.Log($"[ShipbreakerVr] Scene loaded '{scene.name}' — ensuring auto-VR retry loop.");
        EnsureAutoEnterLoop();
    }

    private void EnsureAutoEnterLoop()
    {
        if (autoEnterRunning || IsVrEnabled) return;
        StartCoroutine(AutoEnterVrWhenReady());
    }

    private IEnumerator AutoEnterVrWhenReady()
    {
        autoEnterRunning = true;
        var maxAttempts = autoEnterMaxAttempts != null ? Math.Max(1, autoEnterMaxAttempts.Value) : 40;
        var delay = autoEnterRetrySeconds != null ? Math.Max(0.25f, autoEnterRetrySeconds.Value) : 1f;

        // Let the first Unity frames finish; SteamVR/OpenXR often isn't ready at plugin Start().
        yield return null;
        yield return null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (IsVrEnabled)
            {
                Debug.Log("[ShipbreakerVr] Auto-VR already active — stopping retry loop.");
                break;
            }

            Debug.Log($"[ShipbreakerVr] Auto-enter VR attempt {attempt}/{maxAttempts}...");
            var error = TryEnableVr();
            if (IsVrEnabled)
            {
                Debug.Log("[ShipbreakerVr] Auto-entered VR (no F3 required).");
                LogXrStatus();
                break;
            }

            Debug.LogWarning(
                $"[ShipbreakerVr] Auto-enter VR attempt {attempt} did not stick. {error} " +
                "Will retry (SteamVR/OpenXR may still be starting). F3 remains emergency toggle.");

            yield return new WaitForSecondsRealtime(delay);
        }

        if (!IsVrEnabled)
        {
            Debug.LogError(
                "[ShipbreakerVr] Auto-enter VR gave up after retries. You are likely in SteamVR " +
                "theater/flat mode. Check BepInEx\\LogOutput.log, confirm winhttp.dll is next to " +
                "Shipbreaker.exe, confirm SteamVR is the active OpenXR runtime, then press F3 once. " +
                "See install-and-test.md → theater-mode troubleshooting.");
            LogXrStatus();
        }

        autoEnterRunning = false;

        // If a scene load arrives later and VR still isn't up, OnSceneLoaded restarts the loop.
    }

    private void ToggleXr()
    {
        if (IsVrEnabled)
        {
            TryDisableVr();
        }
        else
        {
            var error = TryEnableVr();
            if (!IsVrEnabled)
                Debug.LogError($"[ShipbreakerVr] F3 enable failed: {error}");
            else
                Debug.Log("[ShipbreakerVr] F3 enabled VR.");
            LogXrStatus();
        }
    }

    /// <summary>Enable VR; returns null on success, otherwise an error/detail string.</summary>
    public string TryEnableVr()
    {
        try
        {
            if (!isXrSetUp)
            {
                var setupError = SetUpXr();
                if (setupError != null) return setupError;
            }

            if (XRGeneralSettings.Instance == null || XRGeneralSettings.Instance.Manager == null)
                return "XRGeneralSettings.Manager is null after setup.";

            var manager = XRGeneralSettings.Instance.Manager;
            var loader = manager.activeLoader;
            if (loader == null) return "No active XR loader (OpenXR).";

            // Match upstream ShipbreakerVR enable order, but tolerate already-initialized loaders.
            try
            {
                manager.StartSubsystems();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ShipbreakerVr] StartSubsystems: {e.Message}");
            }

            if (!loader.Initialize())
                Debug.LogWarning("[ShipbreakerVr] activeLoader.Initialize() returned false (may already be init).");

            if (!loader.Start())
                Debug.LogWarning("[ShipbreakerVr] activeLoader.Start() returned false.");

            // Re-resolve loader typed reference after start.
            _openXrLoader = manager.ActiveLoaderAs<OpenXRLoaderBase>() ?? _openXrLoader;
            IsVrEnabled = IsInitialized || IsDisplaySubsystemRunning();

            if (!IsVrEnabled)
                return "OpenXR loader started but isInitialized/display subsystem still false.";

            // Re-anchor any already-converted menus in front of the player (world-locked).
            RecenterAllVrUi();
            return null;
        }
        catch (Exception e)
        {
            // Allow a later retry to re-run setup (e.g. asset bundle / runtime not ready yet).
            isXrSetUp = false;
            _openXrLoader = null;
            IsVrEnabled = false;
            return e.ToString();
        }
    }

    private void TryDisableVr()
    {
        try
        {
            if (XRGeneralSettings.Instance?.Manager?.activeLoader != null)
            {
                XRGeneralSettings.Instance.Manager.activeLoader.Stop();
                XRGeneralSettings.Instance.Manager.activeLoader.Deinitialize();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ShipbreakerVr] Disable VR error: {e.Message}");
        }

        IsVrEnabled = false;
        Debug.Log("[ShipbreakerVr] VR disabled (F3).");
    }

    /// <summary>Returns null on success, otherwise error detail. Does not throw.</summary>
    private string SetUpXr()
    {
        try
        {
            Debug.Log("[ShipbreakerVr] SetUpXr: loading xrmanager asset bundle...");
            var xrManagerBundle = VrAssetManager.LoadBundle("xrmanager");

            foreach (var xrManager in xrManagerBundle.LoadAllAssets())
                Debug.Log($"[ShipbreakerVr] Loaded xrManager asset: {xrManager.name}");

            var instance = XRGeneralSettings.Instance;
            if (instance == null) return "XRGeneralSettings.Instance is null after loading asset bundle.";

            var xrManagerSettings = instance.Manager;
            if (xrManagerSettings == null) return "XRManagerSettings is null.";

            xrManagerSettings.InitializeLoaderSync();
            if (xrManagerSettings.activeLoader == null)
                return "InitializeLoaderSync produced no activeLoader. Is SteamVR the OpenXR runtime? Is the headset awake?";

            _openXrLoader = xrManagerSettings.ActiveLoaderAs<OpenXRLoaderBase>();
            if (_openXrLoader == null)
                return "Active loader is not OpenXRLoaderBase (got " +
                       xrManagerSettings.activeLoader.GetType().FullName + ").";

            // Force-load OpenXRSettings so feature types resolve with the asset bundle.
            _ = OpenXRSettings.Instance;

            isXrSetUp = true;
            Debug.Log("[ShipbreakerVr] SetUpXr OK — OpenXR loader ready.");
            return null;
        }
        catch (Exception e)
        {
            isXrSetUp = false;
            _openXrLoader = null;
            return e.ToString();
        }
    }

    private static bool IsDisplaySubsystemRunning()
    {
        var displays = new System.Collections.Generic.List<XRDisplaySubsystem>();
        SubsystemManager.GetInstances(displays);
        foreach (var d in displays)
        {
            if (d != null && d.running) return true;
        }

        return false;
    }

    private static void LogXrStatus()
    {
        try
        {
            var loaderName = XRGeneralSettings.Instance?.Manager?.activeLoader != null
                ? XRGeneralSettings.Instance.Manager.activeLoader.name
                : "(none)";
            Debug.Log(
                $"[ShipbreakerVr] XR status: IsVrEnabled={IsVrEnabled}, IsInitialized={IsInitialized}, " +
                $"displayRunning={IsDisplaySubsystemRunning()}, loader={loaderName}, " +
                $"XRSettings.enabled={XRSettings.enabled}, loadedDeviceName={XRSettings.loadedDeviceName}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ShipbreakerVr] LogXrStatus failed: {e.Message}");
        }
    }

    private static void RecenterAllVrUi()
    {
        try
        {
            foreach (var ui in FindObjectsOfType<VrUi>())
                ui.PlaceInFrontOfPlayer(force: true);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ShipbreakerVr] RecenterAllVrUi: {e.Message}");
        }
    }
}
