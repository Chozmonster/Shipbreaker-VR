using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ShipbreakerVr;

/// <summary>
/// 0.4.6 — fix Jay's 0.4.5 log: eyeCam=null bodyCam=null → menus at (0,-0.2,-2.3)
/// while controllers tracked in real XR space → laser hover=none.
///
/// Place using XRNode.Head (or Lynx/VrCamera when available). Comfort scale.
/// LateUpdate snap kills HMD-follow. Do not rely on VrUi attach path.
/// </summary>
public class VrMenuWorldLock : MonoBehaviour
{
    private const float ScanInterval = 0.35f;
    private const float StatusInterval = 1.25f;
    private const float DriftEps = 0.04f;

    private static readonly string[] MenuAllowHints =
    {
        "MainMenu", "FrontEnd", "Frontend", "Front-End", "Pause", "FreePlay", "Free Mode",
        "Profile", "LevelSelect", "Weekly", "Options", "Settings", "HabScreen", "Hab",
        "Certificate", "Inventory", "UIView", "Doozy", "UIManager", "Popup", "Popups",
        "Dialog", "Menu", "Title", "Lobby", "Canvas - Front"
    };

    private static readonly string[] HardSkipHints =
    {
        "VrCamera", "OpenXR", "SteamVR", "reticule", "reticle", "crosshair",
        "HUD_Reticule", "HudReticule", "Marker", "fade", "Fade", "splash", "Splash",
        "loader", "Loader", "FPS", "Console", "UnityExplorer", "Tooltip", "Toast",
        "Notify", "Distortion", "Reboot", "Speech", "DamageVignette", "Vignette"
    };

    private float nextScan;
    private float nextStatus;
    private readonly Dictionary<int, LockedCanvas> locked = new();
    private int skippedProtected;
    private int skippedNonMenu;
    private int snapCount;
    private int deferredNoPose;
    private bool loggedEmptyScan;
    private string lastViewSource = "(none)";

    private class LockedCanvas
    {
        public Canvas Canvas;
        public Transform Transform;
        public Vector3 AnchorPos;
        public Quaternion AnchorRot;
        public bool Anchored;
        public string Name;
        public int DriftFixes;
        public string ViewSource;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        ModLog.Info(
            "[WorldLock] 0.4.6: XR head / Lynx pose → world plane " +
            $"~{MenuWorldPlacement.TargetDistance:0.0}m + comfort scale. F4 recenter. Camera untouched.");
    }

    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ModLog.Info($"[WorldLock] SceneLoaded '{scene.name}' — clear anchors & rescan.");
        locked.Clear();
        nextScan = 0f;
        loggedEmptyScan = false;
        deferredNoPose = 0;
    }

    private void Update()
    {
        if (!ModXrManager.IsVrEnabled)
        {
            if (locked.Count > 0) locked.Clear();
            return;
        }

        // Neutralize conflicting VrUi on menus we own (0.4.5 attached both).
        NeutralizeStockVrUi();

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
    }

    private void LateUpdate()
    {
        if (!ModXrManager.IsVrEnabled) return;
        EnforceWorldAnchors();

        if (Time.unscaledTime >= nextStatus)
        {
            nextStatus = Time.unscaledTime + StatusInterval;
            LogStatus();
        }
    }

    private void NeutralizeStockVrUi()
    {
        foreach (var ui in FindObjectsOfType<VrUi>())
        {
            if (!ui || !ui.enabled) continue;
            // VrUi in 0.4.6 no longer LateUpdate-follows HMD, but dual systems still race.
            // Disable it — WorldLock owns FrontEnd canvases.
            ui.enabled = false;
            ModLog.Info($"[WorldLock] disabled VrUi on '{ui.name}' (WorldLock owns placement).");
        }
    }

    private void ScanAndLock()
    {
        if (!MenuWorldPlacement.TryGetPlayerView(out var view))
        {
            deferredNoPose++;
            if (deferredNoPose <= 3 || deferredNoPose % 10 == 0)
                ModLog.Warn(
                    $"[WorldLock] defer lock — no player pose yet (source={view.Source}). " +
                    $"eyeCam={(VrCamera.ActiveEyeCamera ? VrCamera.ActiveEyeCamera.name : "null")} " +
                    $"bodyCam={(VrCamera.MainCamera ? VrCamera.MainCamera.name : "null")} " +
                    $"deferred={deferredNoPose}");
            return;
        }

        lastViewSource = view.Source;
        skippedProtected = 0;
        skippedNonMenu = 0;
        var newly = 0;
        var seen = 0;

        foreach (var canvas in FindObjectsOfType<Canvas>())
        {
            if (!canvas || !canvas.isRootCanvas || !canvas.isActiveAndEnabled) continue;
            seen++;

            if (IsProtected(canvas))
            {
                skippedProtected++;
                continue;
            }

            if (!IsMenuCandidate(canvas, out var why))
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
                ConvertAndAnchor(state, view, "scan:" + why);
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
        {
            ModLog.Info(
                $"[WorldLock][Menu] newlyLocked={newly} tracked={locked.Count} view={view.Source} " +
                $"headPos={view.Position} seenRoot={seen} skippedProtected={skippedProtected} " +
                $"skippedNonMenu={skippedNonMenu}");
            loggedEmptyScan = false;
        }
        else if (locked.Count == 0 && seen > 0 && !loggedEmptyScan)
        {
            ModLog.Warn(
                $"[WorldLock] no menus locked (seenRoot={seen} skippedProtected={skippedProtected} " +
                $"skippedNonMenu={skippedNonMenu} view={view.Source}). Dump:");
            DumpCanvases();
            loggedEmptyScan = true;
        }
    }

    private void DumpCanvases()
    {
        var n = 0;
        foreach (var canvas in FindObjectsOfType<Canvas>())
        {
            if (!canvas || !canvas.isRootCanvas || !canvas.isActiveAndEnabled) continue;
            var sel = canvas.GetComponentsInChildren<Selectable>(true).Length;
            ModLog.Info(
                $"[WorldLock][Dump] '{Describe(canvas)}' mode={canvas.renderMode} selectables={sel} " +
                $"parent={(canvas.transform.parent ? canvas.transform.parent.name : "null")} " +
                $"protected={IsProtected(canvas)} candidate={IsMenuCandidate(canvas, out var why)} why={why}");
            if (++n >= 12) break;
        }
    }

    private void EnforceWorldAnchors()
    {
        if (!MenuWorldPlacement.TryGetPlayerView(out var view) && locked.Count == 0)
            return;

        var drop = new List<int>();
        foreach (var kv in locked)
        {
            var state = kv.Value;
            var canvas = state.Canvas;
            if (!canvas)
            {
                drop.Add(kv.Key);
                continue;
            }

            if (IsProtected(canvas))
            {
                drop.Add(kv.Key);
                continue;
            }

            if (canvas.renderMode != RenderMode.WorldSpace)
            {
                if (!MenuWorldPlacement.TryGetPlayerView(out view)) continue;
                ModLog.Warn($"[WorldLock][Menu] '{state.Name}' mode→{canvas.renderMode} — re-locking.");
                ConvertAndAnchor(state, view, "mode-reset");
                continue;
            }

            if (MenuWorldPlacement.DetachFromCameraHierarchy(state.Transform, out var parentName))
            {
                ModLog.Warn($"[WorldLock][Menu] '{state.Name}' re-parented under '{parentName}' — unparented.");
                state.Anchored = false;
            }

            if (!state.Anchored)
            {
                if (!MenuWorldPlacement.TryGetPlayerView(out view)) continue;
                ConvertAndAnchor(state, view, "enforce-anchor");
                continue;
            }

            var drifted = (state.Transform.position - state.AnchorPos).sqrMagnitude > DriftEps * DriftEps;
            if (drifted || Quaternion.Angle(state.Transform.rotation, state.AnchorRot) > 2f)
            {
                state.Transform.SetPositionAndRotation(state.AnchorPos, state.AnchorRot);
                state.DriftFixes++;
                snapCount++;
                if (state.DriftFixes == 1 || state.DriftFixes % 60 == 0)
                    ModLog.Warn(
                        $"[WorldLock][Menu] '{state.Name}' drifted (HMD-follow fight) — snapped " +
                        $"fixes={state.DriftFixes} anchor={state.AnchorPos}");
            }
        }

        foreach (var id in drop) locked.Remove(id);
    }

    private void ConvertAndAnchor(LockedCanvas state, MenuWorldPlacement.PlayerView view, string reason)
    {
        var canvas = state.Canvas;
        if (!canvas || IsProtected(canvas)) return;
        if (canvas.GetComponent<Camera>() != null) return;

        var t = state.Transform;
        MenuWorldPlacement.DetachFromCameraHierarchy(t, out _);

        var rect = canvas.GetComponent<RectTransform>();
        MenuWorldPlacement.ApplyComfortScale(t, rect);

        canvas.renderMode = RenderMode.WorldSpace;
        var eventCam = view.Camera ? view.Camera : MenuWorldPlacement.ResolveAnyCamera();
        if (eventCam) canvas.worldCamera = eventCam;
        canvas.planeDistance = MenuWorldPlacement.TargetDistance;

        MenuWorldPlacement.DisableScalers(canvas);
        MenuWorldPlacement.EnsureCollider(canvas);
        MenuWorldPlacement.EnsureGraphicRaycaster(canvas);
        KillFollowBehaviours(canvas);

        // Strip VrUi so it cannot fight us.
        var vrUi = canvas.GetComponent<VrUi>();
        if (vrUi)
        {
            vrUi.enabled = false;
            Object.Destroy(vrUi);
            ModLog.Info($"[WorldLock] removed VrUi from '{state.Name}'");
        }

        MenuWorldPlacement.ComputeAnchor(view, out state.AnchorPos, out state.AnchorRot);
        t.SetPositionAndRotation(state.AnchorPos, state.AnchorRot);
        state.Anchored = true;
        state.ViewSource = view.Source;

        var dist = Vector3.Distance(state.AnchorPos, view.Position);
        ModLog.Info(
            $"[WorldLock][Menu] LOCKED '{state.Name}' reason={reason} view={view.Source} " +
            $"head={view.Position} pos={state.AnchorPos} dist={dist:0.00}m " +
            $"scale={t.localScale.x:0.######} " +
            $"eyeCam={(VrCamera.ActiveEyeCamera ? VrCamera.ActiveEyeCamera.name : "null")} " +
            $"bodyCam={(VrCamera.MainCamera ? VrCamera.MainCamera.name : "null")}");
    }

    private static void KillFollowBehaviours(Canvas canvas)
    {
        foreach (var mb in canvas.GetComponents<MonoBehaviour>())
        {
            if (!mb || mb is VrMenuWorldLock) continue;
            var n = mb.GetType().Name;
            if (n.IndexOf("Follow", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Billboard", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("LookAt", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("HeadLock", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mb.enabled = false;
                ModLog.Info($"[WorldLock] disabled '{n}' on '{canvas.name}'");
            }
        }
    }

    private void RecenterAll()
    {
        if (!MenuWorldPlacement.TryGetPlayerView(out var view))
        {
            ModLog.Warn("[WorldLock] F4 failed — no player pose yet.");
            return;
        }

        foreach (var kv in locked)
        {
            kv.Value.Anchored = false;
            ConvertAndAnchor(kv.Value, view, "F4");
        }
    }

    public void RecenterAllPublic() => RecenterAll();

    private void LogStatus()
    {
        MenuWorldPlacement.TryGetPlayerView(out var view);
        lastViewSource = view.Source ?? lastViewSource;

        ModLog.Info(
            $"[WorldLock] status trackedMenus={locked.Count} snaps={snapCount} " +
            $"view={lastViewSource} head={view.Position} " +
            $"eyeCam={(VrCamera.ActiveEyeCamera ? VrCamera.ActiveEyeCamera.name : "null")} " +
            $"bodyCam={(VrCamera.MainCamera ? VrCamera.MainCamera.name : "null")} " +
            $"skippedProtected={skippedProtected} skippedNonMenu={skippedNonMenu}");

        var sb = new StringBuilder();
        var i = 0;
        foreach (var kv in locked)
        {
            var s = kv.Value;
            if (!s.Canvas) continue;
            var d = Vector3.Distance(s.AnchorPos, view.Position);
            sb.Append($" | {s.Name} dist={d:0.00}m scale={s.Transform.localScale.x:0.######} via={s.ViewSource}");
            if (++i >= 5) break;
        }

        if (i > 0) ModLog.Info("[WorldLock][Menu] " + sb);
    }

    private static bool IsProtected(Canvas canvas)
    {
        if (!canvas) return true;
        if (canvas.GetComponent<Camera>() != null) return true;
        if (canvas.GetComponentInParent<VrCamera>() != null) return true;

        var name = canvas.gameObject.name;
        var path = Describe(canvas);
        foreach (var hint in HardSkipHints)
        {
            if (name.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (path.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }

        if ((name.IndexOf("HUD", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
             path.IndexOf("/HUD", System.StringComparison.OrdinalIgnoreCase) >= 0) &&
            !NameLooksLikeMenu(name) && !NameLooksLikeMenu(path))
            return true;

        return false;
    }

    private static bool IsMenuCandidate(Canvas canvas, out string why)
    {
        why = "skip";
        var name = canvas.gameObject.name;
        var path = Describe(canvas);
        var selectables = canvas.GetComponentsInChildren<Selectable>(true).Length;

        // Jay's canvases: "Canvas - FrontEnd", "Canvas - Frontend Popups"
        if (name.IndexOf("FrontEnd", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Frontend", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.IndexOf("FrontEnd", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            why = "frontend";
            return true;
        }

        if (canvas.renderMode == RenderMode.ScreenSpaceCamera && selectables > 0)
        {
            why = "ss-camera+selectables";
            return true;
        }

        if (NameLooksLikeMenu(name) || NameLooksLikeMenu(path))
        {
            why = "name-hint";
            return true;
        }

        if (selectables >= 2)
        {
            why = "selectables=" + selectables;
            return true;
        }

        if (canvas.transform.GetComponentInParent<Camera>() != null && selectables > 0)
        {
            why = "under-camera";
            return true;
        }

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
        while (p != null && depth < 5)
        {
            path = p.name + "/" + path;
            p = p.parent;
            depth++;
        }

        return path;
    }
}
