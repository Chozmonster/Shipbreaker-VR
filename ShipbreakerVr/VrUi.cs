using UnityEngine;

namespace ShipbreakerVr;

/// <summary>
/// Legacy attach target. 0.4.6 does not attach this — VrMenuWorldLock owns menus.
/// Kept so old scenes/components don't explode; immediately disables itself.
/// </summary>
public class VrUi : MonoBehaviour
{
    private void OnEnable()
    {
        ModLog.Info($"[WorldLock] VrUi on '{name}' disabled (0.4.6 WorldLock owns placement).");
        enabled = false;
    }
}
