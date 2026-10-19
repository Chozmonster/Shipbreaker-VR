using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ShipbreakerVr;

/// <summary>
/// Aggressively finds menu canvases and forces TRUE world-lock:
/// - World Space render mode
/// - Unparented from any camera
/// - Position set once (or on recenter), NEVER copied from HMD each frame
///
/// Why 0.4.3 failed in practice: we only attached VrUi via CanvasScaler.OnEnable.
/// Front-end/Doozy menus can stay Screen Space–Camera on the XR camera (inherently
/// HMD-glued) if that hook misses or something resets renderMode. This scanner
/// enforces lock continuously without re-following the head.
/// </summary>
public class VrMenuWorldLock : MonoBehaviour
{
    private const float ForwardOffset = 1.8f;
    private const float HeightOffset = -0.1f;
    private const float ScanInterval = 0.5f;
    private const float StatusInterval = 1.0f;

    private static readonly HashSet<string> SkipNameHints = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "reticule", "reticle", "crosshair", "fps", "console", "unityexplorer", "loader", "fade", "splash"
    };

    private float nextScan;
    private float nextStatus;
    private int lastLockedCount;
    private readonly Dictionary<int, LockedCanvas> locked = new();

    private class LockedCanvas
    {
        public Canvas Canvas;
        public Transform Transform;
        public Vector3 AnchorPos;
        public Quaternion AnchorRot;
        public bool Anchored;
        public string Name;
        public int ModeResetCount;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        ModLog.Info("[WorldLock] VrMenuWorldLock enabled — will scan for menu canvases.");
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ModLog.Info($"[WorldLock] SceneLoaded '{scene.name}' — clearing anchors & rescan.");
        locked.Clear();
        nextScan = 0f;
    }

    private void Update()
    {
        if (!ModXrManager.IsVrEnabled)
        {
            if (locked.Count > 0)
            {
                ModLog.Info("[WorldLock] VR off — releasing tracked canvases.");
                locked.Clear();
            }

            return;
        }

        if (Input.GetKeyDown(KeyCode.F4))
        {
            ModLog.Info("[WorldLock] F4 — force recenter all locked menus.");
            RecenterAll();
        }

        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + ScanInterval;
            ScanAndLock();
        }

        EnforceLocksWithoutFollowingHmd();

        if (Time.unscaledTime >= nextStatus)
        {
            nextStatus = Time.unscaledTime + StatusInterval;
            LogStatus();
        }
    }

    private void ScanAndLock()
    {
        var canvases = FindObjectsOfType<Canvas>();
        var found = 0;
        var newly = 0;

        foreach (var canvas in canvases)
        {
            if (!canvas || !canvas.isRootCanvas) continue;
            if (!canvas.isActiveAndEnabled) continue;
            if (ShouldSkip(canvas)) continue;

            found++;
            var id = canvas.GetInstanceID();
            if (!locked.TryGetValue(id, out var state))
            {
                state = new LockedCanvas
                {
                    Canvas = canvas,
                    Transform = canvas.transform,
                    Name = Describe(canvas)
                };
                locked[id] = state;
                newly++;
                ConvertAndAnchor(state, reason: "scan-new");
            }
            else
            {
                state.Canvas = canvas;
                state.Transform = canvas.transform;
            }
        }

        // Drop destroyed
        var dead = new List<int>();
        foreach (var kv in locked)
            if (!kv.Value.Canvas) dead.Add(kv.Key);
        foreach (var id in dead) locked.Remove(id);

        if (newly > 0 || found != lastLockedCount)
        {
            ModLog.Info($"[WorldLock][Menu] scan: activeRoots={found} tracked={locked.Count} newlyLocked={newly}");
            lastLockedCount = found;
        }
    }

    private void EnforceLocksWithoutFollowingHmd()
    {
        foreach (var kv in locked)
        {
            var state = kv.Value;
            var canvas = state.Canvas;
            if (!canvas) continue;

            // Kill HMD-glue modes without moving the anchor.
            if (canvas.renderMode != RenderMode.WorldSpace)
            {
                state.ModeResetCount++;
                ModLog.Warn(
                    $"[WorldLock][Menu] '{state.Name}' renderMode was {canvas.renderMode} — forcing WorldSpace (reset#{state.ModeResetCount}).");
                canvas.renderMode = RenderMode.WorldSpace;
            }

            // If something re-parented under a camera, detach — but keep world pose.
            if (state.Transform.parent != null)
            {
                var parentCam = state.Transform.GetComponentInParent<Camera>();
                ModLog.Warn(
                    $"[WorldLock][Menu] '{state.Name}' was parented under '{state.Transform.parent.name}' " +
                    $"(cameraInParents={parentCam != null}) — unparenting (keep world pose).");
                var pos = state.Transform.position;
                var rot = state.Transform.rotation;
                state.Transform.SetParent(null, true);
                state.Transform.position = pos;
                state.Transform.rotation = rot;
            }

            // Disable scalers that fight world-space.
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler && scaler.enabled) scaler.enabled = false;
            var improver = canvas.GetComponent<BBI.Unity.Game.CanvasScalerImprover>();
            if (improver && improver.enabled) improver.enabled = false;

            // Ensure event camera set, but do NOT copy camera transform onto the canvas.
            var cam = GetAnchorCamera();
            if (cam) canvas.worldCamera = cam;

            // If not yet anchored (or canvas just appeared), place once.
            if (!state.Anchored)
                ConvertAndAnchor(state, reason: "enforce-first-anchor");
            else
            {
                // Hard restore anchor if something moved it with the HMD this frame.
                // Detect: canvas nearly matching camera pose in front of eyes.
                if (cam && LooksGluedToHmd(state, cam))
                {
                    ModLog.Warn(
                        $"[WorldLock][Menu] '{state.Name}' looked HMD-glued — restoring world anchor.");
                    state.Transform.position = state.AnchorPos;
                    state.Transform.rotation = state.AnchorRot;
                }
            }
        }
    }

    private static bool LooksGluedToHmd(LockedCanvas state, Camera cam)
    {
        // If current pos is much closer to "in front of HMD now" than to our anchor, something re-glued it.
        var hmdFront = cam.transform.position + cam.transform.forward * ForwardOffset;
        var distToHmdFront = Vector3.Distance(state.Transform.position, hmdFront);
        var distToAnchor = Vector3.Distance(state.Transform.position, state.AnchorPos);
        return distToHmdFront < 0.35f && distToAnchor > 0.5f;
    }

    private void ConvertAndAnchor(LockedCanvas state, string reason)
    {
        var canvas = state.Canvas;
        if (!canvas) return;

        var cam = GetAnchorCamera();
        if (!cam)
        {
            ModLog.Warn($"[WorldLock][Menu] cannot anchor '{state.Name}' yet — no camera ({reason}).");
            return;
        }

        var t = state.Transform;
        t.SetParent(null, true);
        // Reasonable world-space scale for 1920-ish UI
        if (t.localScale.x > 0.5f || t.localScale.x < 0.0001f)
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
        t.position = state.AnchorPos;
        t.rotation = state.AnchorRot;
        state.Anchored = true;

        ModLog.Info(
            $"[WorldLock][Menu] LOCKED '{state.Name}' reason={reason} " +
            $"pos={state.AnchorPos} rotY={state.AnchorRot.eulerAngles.y:F0} " +
            $"scale={t.localScale} mode={canvas.renderMode} parent={(t.parent ? t.parent.name : "null")}");
    }

    private void RecenterAll()
    {
        foreach (var kv in locked)
        {
            kv.Value.Anchored = false;
            ConvertAndAnchor(kv.Value, reason: "F4-recenter");
        }
    }

    public void RecenterAllPublic() => RecenterAll();

    private void LogStatus()
    {
        if (locked.Count == 0)
        {
            ModLog.Info("[WorldLock][Menu] status: no menu canvases tracked yet (VR on).");
            return;
        }

        var sb = new StringBuilder();
        sb.Append($"[WorldLock][Menu] status: tracked={locked.Count}");
        var i = 0;
        foreach (var kv in locked)
        {
            var s = kv.Value;
            if (!s.Canvas) continue;
            sb.Append($" | {s.Name} mode={s.Canvas.renderMode} parent={(s.Transform.parent ? s.Transform.parent.name : "null")} anchored={s.Anchored}");
            if (++i >= 6) break;
        }

        ModLog.Info(sb.ToString());
    }

    private static Camera GetAnchorCamera()
    {
        // Prefer the *game* body camera (not the HMD-only child) for yaw placement,
        // fall back to whatever Camera.main is.
        if (VrCamera.MainCamera) return VrCamera.MainCamera;
        return Camera.main;
    }

    private static bool ShouldSkip(Canvas canvas)
    {
        var n = canvas.gameObject.name;
        foreach (var hint in SkipNameHints)
            if (n.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private static string Describe(Canvas canvas)
    {
        var path = canvas.gameObject.name;
        var p = canvas.transform.parent;
        if (p) path = p.name + "/" + path;
        return $"{path}#{canvas.GetInstanceID()}";
    }

    private static void EnsureCollider(Canvas canvas)
    {
        var rect = canvas.GetComponent<RectTransform>();
        if (!rect) return;
        var col = canvas.GetComponent<BoxCollider>();
        if (!col) col = canvas.gameObject.AddComponent<BoxCollider>();
        var size = rect.rect.size;
        col.size = new Vector3(Mathf.Max(Mathf.Abs(size.x), 100f), Mathf.Max(Mathf.Abs(size.y), 100f), 1f);
        col.center = new Vector3(rect.rect.center.x, rect.rect.center.y, 0f);
        col.isTrigger = true;
        col.enabled = true;
    }
}
