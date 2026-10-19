using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace ShipbreakerVr;

/// <summary>
/// XR node poses. Laser origin = grip (+ tip cm). Aim = grip rotated +90° pitch (Jay 0.4.7:
/// raw forward pointed straight up the handle).
/// </summary>
public static class XrTracking
{
    /// <summary>Aim tip along aim direction, centimeters only.</summary>
    public const float AimTipOffsetMeters = 0.04f;

    /// <summary>
    /// OpenXR grip +Z reads as "up the handle" in-headset. Pitch +90° local X so the
    /// laser points out the front of the controller (Jay: "rotate it forward 90 degrees").
    /// </summary>
    public static readonly Quaternion GripToAimPitch = Quaternion.Euler(90f, 0f, 0f);

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
#pragma warning disable CS0618
            position = InputTracking.GetLocalPosition(node);
            rotation = InputTracking.GetLocalRotation(node);
#pragma warning restore CS0618
            if (node != XRNode.Head && position.sqrMagnitude < 1e-8f)
                return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static Vector3 AimDirectionFromGripRotation(Quaternion gripRot)
    {
        return (gripRot * GripToAimPitch * Vector3.forward).normalized;
    }

    /// <summary>
    /// Controller aim ray. Origin stays on the grip (tip offset only). Direction pitched +90°.
    /// Cam-rebase only for position when render cam diverges from XR head — does not change aim pitch.
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

        Quaternion aimRot;
        if (cam && hasHead && (cam.transform.position - headPos).sqrMagnitude > 0.09f)
        {
            var localPos = Quaternion.Inverse(headRot) * (handPos - headPos);
            var localRot = Quaternion.Inverse(headRot) * handRot;
            aimRot = cam.transform.rotation * localRot;
            gripPos = cam.transform.position + cam.transform.rotation * localPos;
            space = "cam-rebase/" + cam.name;
        }
        else
        {
            aimRot = handRot;
            gripPos = handPos;
            space = "xr-world";
        }

        direction = AimDirectionFromGripRotation(aimRot);
        if (direction.sqrMagnitude < 1e-6f) direction = Vector3.forward;
        origin = gripPos + direction * AimTipOffsetMeters;
        return true;
    }
}
