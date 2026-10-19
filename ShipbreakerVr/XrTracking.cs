using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace ShipbreakerVr;

/// <summary>
/// XR node poses. Laser aim uses the same tracking data as the physical controller,
/// expressed in the space the HMD is rendering (avoids “laser origin meters away”).
/// </summary>
public static class XrTracking
{
    /// <summary>Aim tip along controller forward (grip → tip), centimeters only.</summary>
    public const float AimTipOffsetMeters = 0.04f;

    public static bool TryGetNodePose(XRNode node, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;

        var nodes = new List<XRNodeState>();
        InputTracking.GetNodeStates(nodes);
        foreach (var n in nodes)
        {
            if (n.nodeType != node) continue;
            if (!n.tracked) continue;
            if (!n.TryGetPosition(out position)) continue;
            if (!n.TryGetRotation(out rotation)) continue;
            return true;
        }

        try
        {
            position = InputTracking.GetLocalPosition(node);
            rotation = InputTracking.GetLocalRotation(node);
            if (node != XRNode.Head && position.sqrMagnitude < 1e-8f)
                return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Controller aim ray in the same space as SteamVR hand visuals / menu hits.
    /// Uses XRNode grip pose; only offset is AimTipOffsetMeters along forward.
    /// When a render camera exists, re-expresses head-relative hand offset onto that camera
    /// (RotationOnly body cam otherwise leaves raw tracking coords meters from the seen hand).
    /// </summary>
    public static bool TryGetHandAim(XRNode hand, out Vector3 origin, out Vector3 direction, out Vector3 gripPos,
        out string space)
    {
        origin = default;
        direction = Vector3.forward;
        gripPos = default;
        space = "none";

        if (!TryGetNodePose(hand, out var handPos, out var handRot))
            return false;

        gripPos = handPos;
        var cam = MenuWorldPlacement.ResolveAnyCamera();
        var hasHead = TryGetNodePose(XRNode.Head, out var headPos, out var headRot);

        // If render cam and XR head diverge, raw tracking coords won't match the seen controller.
        // Rebase: keep XR head→hand offset, apply to camera pose (HMD view space).
        if (cam && hasHead &&
            (cam.transform.position - headPos).sqrMagnitude > 0.09f) // >30cm
        {
            var localPos = Quaternion.Inverse(headRot) * (handPos - headPos);
            var localRot = Quaternion.Inverse(headRot) * handRot;
            var worldRot = cam.transform.rotation * localRot;
            gripPos = cam.transform.position + cam.transform.rotation * localPos;
            direction = (worldRot * Vector3.forward).normalized;
            origin = gripPos + direction * AimTipOffsetMeters;
            space = "cam-rebase/" + cam.name;
            return direction.sqrMagnitude > 1e-6f;
        }

        // Same space as XRNode world pose (FrontEnd / cam≈head).
        direction = (handRot * Vector3.forward).normalized;
        if (direction.sqrMagnitude < 1e-6f) direction = Vector3.forward;
        origin = handPos + direction * AimTipOffsetMeters;
        gripPos = handPos;
        space = "xr-world";
        return true;
    }
}
