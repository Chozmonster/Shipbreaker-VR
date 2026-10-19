using UnityEngine;
using UnityEngine.SpatialTracking;

namespace ShipbreakerVr;

/// <summary>
/// Same approach as stock / 0.4.2–0.4.3. Do not let menu code touch these cameras.
/// </summary>
public class VrCamera : MonoBehaviour
{
    private Camera vrCamera;

    /// <summary>Game body camera (Lynx) — disabled while VR is on.</summary>
    public static Camera MainCamera { get; private set; }

    /// <summary>Active HMD camera used for rendering while VR is on.</summary>
    public static Camera ActiveEyeCamera { get; private set; }

    public static void Create(Camera mainCamera)
    {
        // Avoid duplicate hooks if Start runs again.
        if (MainCamera == mainCamera && ActiveEyeCamera != null)
        {
            ModLog.Info("[VrCamera] already hooked — skipping duplicate Create.");
            return;
        }

        var instance = new GameObject("VrCamera").AddComponent<VrCamera>();
        instance.transform.SetParent(mainCamera.transform, false);
        MainCamera = mainCamera;

        instance.vrCamera = instance.gameObject.AddComponent<Camera>();
        instance.vrCamera.farClipPlane = mainCamera.farClipPlane;
        instance.vrCamera.nearClipPlane = mainCamera.nearClipPlane;
        instance.vrCamera.cullingMask = mainCamera.cullingMask;
        instance.vrCamera.transform.localPosition = Vector3.forward * 0.2f;
        instance.vrCamera.tag = "MainCamera";

        var poseDriver = instance.gameObject.AddComponent<TrackedPoseDriver>();
        poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationOnly;

        ActiveEyeCamera = instance.vrCamera;
        ModLog.Info(
            $"[VrCamera] created under '{mainCamera.name}' eyeCam='{instance.vrCamera.name}' " +
            $"(menu code must not disable/reparent these).");
    }

    private void Update()
    {
        if (!MainCamera || !vrCamera) return;
        MainCamera.enabled = !ModXrManager.IsVrEnabled;
        vrCamera.enabled = ModXrManager.IsVrEnabled;
        if (ModXrManager.IsVrEnabled) ActiveEyeCamera = vrCamera;
    }

    private void OnDestroy()
    {
        if (ActiveEyeCamera == vrCamera) ActiveEyeCamera = null;
        ModLog.Warn("[VrCamera] destroyed — stereo may stop until re-hooked.");
    }
}
