using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace ShipbreakerVr;

/// <summary>
/// Comfortable world-plane placement for menus.
/// Prefer XR HMD pose (FrontEnd often has eyeCam/bodyCam null — LynxCamera not started yet).
/// Target: ~1.3m ahead (Jay: 1.2–1.5m), panel scaled to stay fully visible.
/// </summary>
public static class MenuWorldPlacement
{
    /// <summary>Meters ahead of player yaw-forward. F4 recenters to this distance.</summary>
    public const float TargetDistance = 1.3f;

    public const float HeightOffset = -0.12f;

    /// <summary>World-space panel width at TargetDistance — full UI readable, not tiny.</summary>
    public const float TargetWidthMeters = 1.2f;

    public const float MinScale = 0.00035f;
    public const float MaxScale = 0.0035f;

    public struct PlayerView
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public string Source;
        public Camera Camera; // may be null when using XRNode.Head only
    }

    /// <summary>
    /// Resolve where the player is looking.
    /// FrontEnd: XRNode.Head (cameras often still at origin).
    /// After VrCamera: prefer the render camera so menus share space with the HMD view.
    /// </summary>
    public static bool TryGetPlayerView(out PlayerView view)
    {
        view = default;
        var hasHead = TryGetXrHead(out var headPos, out var headRot);

        // 1) VrCamera eye/body when hooked and not stuck at origin — aligns with what HMD shows.
        if (VrCamera.ActiveEyeCamera && VrCamera.ActiveEyeCamera.transform.position.sqrMagnitude > 0.01f)
        {
            var c = VrCamera.ActiveEyeCamera;
            view.Position = c.transform.position;
            view.Rotation = c.transform.rotation;
            view.Source = "VrCamera.ActiveEyeCamera/" + c.name;
            view.Camera = c;
            return true;
        }

        if (VrCamera.MainCamera && VrCamera.MainCamera.transform.position.sqrMagnitude > 0.01f)
        {
            var c = VrCamera.MainCamera;
            view.Position = c.transform.position;
            view.Rotation = c.transform.rotation;
            view.Source = "VrCamera.MainCamera/" + c.name;
            view.Camera = c;
            return true;
        }

        // 2) XR HMD — correct during FrontEnd before LynxCameraController.Start.
        if (hasHead)
        {
            view.Position = headPos;
            view.Rotation = headRot;
            view.Source = "XRNode.Head";
            view.Camera = ResolveAnyCamera();
            return true;
        }

        // 3) LynxCameraController if present but VrCamera not hooked yet.
        var lynx = Object.FindObjectOfType<LynxCameraController>();
        if (lynx)
        {
            var c = lynx.GetComponent<Camera>();
            if (c && c.transform.position.sqrMagnitude > 0.01f)
            {
                view.Position = c.transform.position;
                view.Rotation = c.transform.rotation;
                view.Source = "LynxCameraController/" + c.name;
                view.Camera = c;
                return true;
            }
        }

        // 4) Camera.main — reject origin while XR up (0.4.5 bug).
        var cam = ResolveAnyCamera();
        if (cam)
        {
            var p = cam.transform.position;
            if (p.sqrMagnitude < 0.01f && XrLikelyActive())
            {
                view.Source = "defer:Camera.atOriginWhileXrUp/" + cam.name;
                return false;
            }

            view.Position = p;
            view.Rotation = cam.transform.rotation;
            view.Source = "Camera/" + cam.name;
            view.Camera = cam;
            return true;
        }

        view.Source = "none";
        return false;
    }

    public static Camera ResolveAnyCamera()
    {
        if (VrCamera.ActiveEyeCamera) return VrCamera.ActiveEyeCamera;
        if (VrCamera.MainCamera) return VrCamera.MainCamera;
        if (Camera.main) return Camera.main;
        foreach (var c in Object.FindObjectsOfType<Camera>())
            if (c && c.enabled && c.gameObject.activeInHierarchy)
                return c;
        return null;
    }

    public static void ComputeAnchor(PlayerView view, out Vector3 pos, out Quaternion rot)
    {
        var flatForward = view.Rotation * Vector3.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-4f) flatForward = Vector3.forward;
        flatForward.Normalize();

        pos = view.Position + flatForward * TargetDistance + Vector3.up * HeightOffset;
        rot = Quaternion.LookRotation(flatForward, Vector3.up);
    }

    public static float ComfortScale(RectTransform rect)
    {
        if (!rect) return 0.001f;
        var w = Mathf.Abs(rect.rect.width);
        if (w < 10f) w = 1920f;
        var scale = TargetWidthMeters / w;
        return Mathf.Clamp(scale, MinScale, MaxScale);
    }

    public static void ApplyComfortScale(Transform t, RectTransform rect)
    {
        var s = ComfortScale(rect);
        t.localScale = new Vector3(s, s, s);
    }

    public static void EnsureCollider(Canvas canvas)
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

    public static void EnsureGraphicRaycaster(Canvas canvas)
    {
        if (!canvas.GetComponent<GraphicRaycaster>())
            canvas.gameObject.AddComponent<GraphicRaycaster>();
    }

    public static void DisableScalers(Canvas canvas)
    {
        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler) scaler.enabled = false;
        var improver = canvas.GetComponent<BBI.Unity.Game.CanvasScalerImprover>();
        if (improver) improver.enabled = false;
    }

    public static bool DetachFromCameraHierarchy(Transform t, out string detail)
    {
        detail = null;
        if (!t || t.parent == null) return false;
        detail = t.parent.name;
        t.SetParent(null, true);
        return true;
    }

    private static bool TryGetXrHead(out Vector3 pos, out Quaternion rot)
    {
        if (XrTracking.TryGetNodePose(XRNode.Head, out pos, out rot))
            return true;

        // InputDevices HMD fallback
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.HeadMounted | InputDeviceCharacteristics.TrackedDevice, devices);
        foreach (var d in devices)
        {
            if (!d.isValid) continue;
            if (!d.TryGetFeatureValue(CommonUsages.devicePosition, out pos)) continue;
            if (!d.TryGetFeatureValue(CommonUsages.deviceRotation, out rot)) continue;
            return true;
        }

        pos = default;
        rot = Quaternion.identity;
        return false;
    }

    private static bool XrLikelyActive()
    {
        if (ModXrManager.IsVrEnabled) return true;
        var nodes = new List<XRNodeState>();
        InputTracking.GetNodeStates(nodes);
        foreach (var n in nodes)
        {
            if (n.nodeType is not (XRNode.Head or XRNode.RightHand or XRNode.LeftHand)) continue;
            if (n.tracked) return true;
        }

        return false;
    }
}
