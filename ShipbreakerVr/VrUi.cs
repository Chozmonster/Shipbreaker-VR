using UnityEngine;

namespace ShipbreakerVr;

/// <summary>
/// Legacy per-canvas marker from earlier drops. World-lock is owned by
/// <see cref="VrMenuWorldLock"/> (0.4.4+) so we never HMD-follow from here.
/// Kept so old Harmony attachments don't NRE if a canvas still has this component.
/// </summary>
public class VrUi : MonoBehaviour
{
    public void PlaceInFrontOfPlayer(bool force = false)
    {
        // No-op: VrMenuWorldLock owns anchoring. F4 is handled there.
        ModLog.Info($"[WorldLock] VrUi.PlaceInFrontOfPlayer ignored on '{name}' (use VrMenuWorldLock/F4).");
    }
}
