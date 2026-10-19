using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ShipbreakerVr;

/// <summary>
/// SAFE menu world-lock (0.4.5).
///
/// 0.4.4 broke the VR view by converting nearly ALL root canvases to World Space and
/// parking them in front of the HMD (fullscreen overlays blocked the camera).
///
/// This version is whitelist-only for menu-like canvases and never touches cameras,
/// XR/HMD utility canvases, fades, or HUDs.
/// </summary>
public class VrMenuWorldLock : MonoBehaviour
{
    private const float ForwardOffset = 1.8f;
    private const float HeightOffset = -0.1f;
    private const float ScanInterval = 0.75f;
    private const float StatusInterval = 2.0f;

    /// <summary>Only canvases whose names match these are candidates.</summary>
    private static readonly string[] MenuAllowHints =
    {
        "MainMenu", "FrontEnd", "Pause", "FreePlay", "Free Mode", "Profile",
        "LevelSelect", "Weekly", "Options", "Settings", "HabScreen", "Certificate",
        "Inventory", "UIView", "Doozy"
    };

    /// <summary>Hard skip — never convert (protects camera / XR / overlays).</summary>
    private static readonly string[] HardSkipHints =
    {
        "VrCamera", "XR", "OpenXR", "SteamVR", "reticule", "reticle", "crosshair",
        "HUD", "Hud", "Marker", "fade", "Fade", "splash", "Splash", "loader", "Loader",
        "FPS", "Console", "UnityExplorer", "Cursor", "Tooltip", "Toast", "Notify",
        "Overlay", "Distortion", "Reboot", "Speech"
    };

    private float nextScan;
    private float nextStatus;
    private readonly Dictionary<int, LockedCanvas> locked = new();
    private int skippedProtected;
    private int skippedNonMenu;

    private class LockedCanvas
    {
        public Canvas Canvas;
        public Transform Transform;
        public Vector3 AnchorPos;
        public Quaternion AnchorRot;
        public bool Anchored;
        public string Name;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        ModLog.Info("[WorldLock] SAFE mode (0.4.5): whitelist menus only — will not touch XR/HUD/fade canvases.");
    }

    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ModLog.Info($"[WorldLock] SceneLoaded '{scene.name}' — clear menu anchors.");
        locked.Clear();
        nextScan = 0f;
    }

    private void Update()
    {
        if (!ModXrManager.IsVrEnabled)
        {
            if (locked.Count > 0) locked.Clear();
            return;
        }

        if (Input.GetKeyDown(KeyCode.F4))
        {
            ModLog.Info("[WorldLock] F4 recenter (menus only).");
            RecenterAll();
        }

        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + ScanInterval;
            ScanAndLock();
        }

        EnforceMenuLocksOnly();

        if (Time.unscaledTime >= nextStatus)
        {
            nextStatus = Time.unscaledTime + StatusInterval;
            LogStatus();
        }
    }

    private void ScanAndLock()
    {
        skippedProtected = 0;
        skippedNonMenu = 0;
        var newly = 0;

        foreach (var canvas in FindObjectsOfType<Canvas>())
        {
            if (!canvas || !canvas.isRootCanvas || !canvas.isActiveAndEnabled) continue;

            if (IsProtected(canvas))
            {
                skippedProtected++;
                continue;
            }

            if (!IsMenuCandidate(canvas))
            {
                skippedNonMenu++;
                continue;
            }

            var id = canvas.GetInstanceID();
            if (!locked.ContainsKey(id))
            {
                var state = new LockedCanvas
                {
                    Canvas = canvas,
                    Transform = canvas.transform,
                    Name = Describe(canvas)
                };
                locked[id] = state;
                ConvertAndAnchor(state, "scan-whitelist");
                newly++;
            }
            else
            {
                locked[id].Canvas = canvas;
                locked[id].Transform = canvas.transform;
            }
        }

        var dead = new List<int>();
        foreach (var kv in locked)
            if (!kv.Value.Canvas) dead.Add(kv.Key);
        foreach (var id in dead) locked.Remove(id);

        if (newly > 0)
            ModLog.Info(
                $"[WorldLock][Menu] newlyLocked={newly} tracked={locked.Count} " +
                $"skippedProtected={skippedProtected} skippedNonMenu={skippedNonMenu}");
    }

    private void EnforceMenuLocksOnly()
    {
        foreach (var kv in locked)
        {
            var state = kv.Value;
            var canvas = state.Canvas;
            if (!canvas) continue;

            // If somehow a protected canvas got in, drop it immediately.
            if (IsProtected(canvas))
            {
                ModLog.Warn($"[WorldLock] dropping protected canvas '{state.Name}' from tracker.");
                continue;
            }

            if (canvas.renderMode != RenderMode.WorldSpace)
            {
                ModLog.Warn($"[WorldLock][Menu] '{state.Name}' mode reset to {canvas.renderMode} — re-locking.");
                ConvertAndAnchor(state, "mode-reset");
                continue;
            }

            // Only unparent if parent is a Camera (HMD glue). Don't yank arbitrary UI graphs.
            var parentCam = state.Transform.GetComponentInParent<Camera>();
            if (parentCam != null)
            {
                ModLog.Warn(
                    $"[WorldLock][Menu] '{state.Name}' under camera '{parentCam.name}' — unparenting canvas only.");
                var pos = state.Transform.position;
                var rot = state.Transform.rotation;
                state.Transform.SetParent(null, true);
                state.Transform.SetPositionAndRotation(pos, rot);
            }

            if (!state.Anchored)
                ConvertAndAnchor(state, "enforce-anchor");
        }
    }

    private void ConvertAndAnchor(LockedCanvas state, string reason)
    {
        var canvas = state.Canvas;
        if (!canvas || IsProtected(canvas)) return;

        var cam = VrCamera.ActiveEyeCamera ? VrCamera.ActiveEyeCamera :
            (VrCamera.MainCamera ? VrCamera.MainCamera : Camera.main);
        if (!cam)
        {
            ModLog.Warn($"[WorldLock][Menu] no camera to anchor '{state.Name}' ({reason}).");
            return;
        }

        // Never disable or reparent the camera itself.
        if (canvas.GetComponent<Camera>() != null)
        {
            ModLog.Warn($"[WorldLock] refusing canvas-on-camera '{state.Name}'.");
            return;
        }

        var t = state.Transform;
        if (t.GetComponentInParent<Camera>() != null)
            t.SetParent(null, true);

        if (t.localScale.x > 0.1f || t.localScale.x < 1e-5f)
            t.localScale = Vector3.one * 0.001f;

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;

        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler) scaler.enabled = false;
        var improver = canvas.GetComponent<BBI.Unity.Game.CanvasScalerImprover>();
        if (improver) improver.enabled = false;

        EnsureCollider(canvas);

        var flatForward = cam.transform.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-4f) flatForward = Vector3.forward;
        flatForward.Normalize();

        state.AnchorPos = cam.transform.position + flatForward * ForwardOffset + Vector3.up * HeightOffset;
        state.AnchorRot = Quaternion.LookRotation(flatForward, Vector3.up);
        t.SetPositionAndRotation(state.AnchorPos, state.AnchorRot);
        state.Anchored = true;

        ModLog.Info(
            $"[WorldLock][Menu] LOCKED '{state.Name}' reason={reason} pos={state.AnchorPos} " +
            $"(camera '{cam.name}' untouched)");
    }

    private void RecenterAll()
    {
        foreach (var kv in locked)
        {
            kv.Value.Anchored = false;
            ConvertAndAnchor(kv.Value, "F4");
        }
    }

    public void RecenterAllPublic() => RecenterAll();

    private void LogStatus()
    {
        // Always remind that camera path is separate / protected.
        ModLog.Info(
            $"[WorldLock] status trackedMenus={locked.Count} " +
            $"eyeCam={(VrCamera.ActiveEyeCamera ? VrCamera.ActiveEyeCamera.name : "null")} " +
            $"bodyCam={(VrCamera.MainCamera ? VrCamera.MainCamera.name : "null")} " +
            $"skippedProtected={skippedProtected} skippedNonMenu={skippedNonMenu}");

        var sb = new StringBuilder();
        var i = 0;
        foreach (var kv in locked)
        {
            var s = kv.Value;
            if (!s.Canvas) continue;
            sb.Append($" | {s.Name}");
            if (++i >= 5) break;
        }

        if (i > 0) ModLog.Info("[WorldLock][Menu] " + sb);
    }

    private static bool IsProtected(Canvas canvas)
    {
        if (!canvas) return true;

        // Never touch a canvas on the same object as a Camera, or under VrCamera.
        if (canvas.GetComponent<Camera>() != null) return true;
        if (canvas.GetComponentInParent<VrCamera>() != null) return true;

        var name = canvas.gameObject.name;
        var path = Describe(canvas);
        foreach (var hint in HardSkipHints)
        {
            if (name.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (path.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }

        // Fullscreen Screen Space Overlay without menu keywords — leave alone (0.4.4 broke these).
        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay && !NameLooksLikeMenu(name) &&
            !NameLooksLikeMenu(path))
            return true;

        return false;
    }

    private static bool IsMenuCandidate(Canvas canvas)
    {
        var name = canvas.gameObject.name;
        var path = Describe(canvas);
        if (NameLooksLikeMenu(name) || NameLooksLikeMenu(path)) return true;

        // Doozy / selectable-heavy UI under FrontEnd-ish parents
        if (canvas.GetComponentInChildren<Selectable>(true) != null &&
            (path.IndexOf("UI", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
             path.IndexOf("Menu", System.StringComparison.OrdinalIgnoreCase) >= 0))
            return true;

        return false;
    }

    private static bool NameLooksLikeMenu(string name)
    {
        foreach (var hint in MenuAllowHints)
            if (name.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private static string Describe(Canvas canvas)
    {
        var path = canvas.gameObject.name;
        var p = canvas.transform.parent;
        var depth = 0;
        while (p != null && depth < 4)
        {
            path = p.name + "/" + path;
            p = p.parent;
            depth++;
        }

        return path;
    }

    private static void EnsureCollider(Canvas canvas)
    {
        var rect = canvas.GetComponent<RectTransform>();
        if (!rect) return;
        var col = canvas.GetComponent<BoxCollider>() ?? canvas.gameObject.AddComponent<BoxCollider>();
        var size = rect.rect.size;
        col.size = new Vector3(Mathf.Max(Mathf.Abs(size.x), 100f), Mathf.Max(Mathf.Abs(size.y), 100f), 1f);
        col.center = new Vector3(rect.rect.center.x, rect.rect.center.y, 0f);
        col.isTrigger = true;
        col.enabled = true;
    }
}
