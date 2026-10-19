using BBI.Unity.Game;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShipbreakerVr;

/// <summary>
/// Per-canvas world-lock for known menu canvases (CameraScaler path).
/// Place once ahead of the player — never LateUpdate-follow the HMD (stock did that).
/// Camera-safe: does not touch game/XR cameras.
/// </summary>
public class VrUi : MonoBehaviour
{
    private const float ForwardOffset = 1.75f;
    private const float HeightOffset = -0.15f;

    private Canvas canvas;
    private bool worldLocked;
    private bool wasActive = true;

    private void Awake()
    {
        canvas = GetComponent<Canvas>();
    }

    private void OnEnable()
    {
        if (ModXrManager.IsVrEnabled) PlaceInFrontOfPlayer(force: true);
    }

    private void Update()
    {
        if (!ModXrManager.IsVrEnabled)
        {
            worldLocked = false;
            return;
        }

        if (canvas.renderMode != RenderMode.WorldSpace)
            SetUpCanvas();
        else if (!worldLocked)
            PlaceInFrontOfPlayer(force: true);

        var active = gameObject.activeInHierarchy && canvas.isActiveAndEnabled;
        if (active && !wasActive) PlaceInFrontOfPlayer(force: true);
        wasActive = active;

        if (Input.GetKeyDown(KeyCode.F4)) PlaceInFrontOfPlayer(force: true);
    }

    private void SetUpCanvas()
    {
        // Detach from camera hierarchy so we don't inherit HMD motion — canvas only, never cameras.
        if (GetComponentInParent<Camera>() != null || transform.parent != null)
            transform.SetParent(null, true);

        if (transform.localScale.x > 0.1f || transform.localScale.x < 1e-5f)
            transform.localScale = Vector3.one * 0.001f;

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = VrCamera.ActiveEyeCamera ? VrCamera.ActiveEyeCamera :
            (VrCamera.MainCamera ? VrCamera.MainCamera : Camera.main);

        var improver = canvas.GetComponent<CanvasScalerImprover>();
        if (improver) improver.enabled = false;
        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler) scaler.enabled = false;

        EnsureUiCollider();
        EnsureEventSystem();
        PlaceInFrontOfPlayer(force: true);
        ModLog.Info($"[WorldLock][Menu] VrUi world-locked '{name}' (CanvasScaler path)");
    }

    public void PlaceInFrontOfPlayer(bool force = false)
    {
        if (!ModXrManager.IsVrEnabled) return;
        if (worldLocked && !force) return;

        var cam = VrCamera.ActiveEyeCamera ? VrCamera.ActiveEyeCamera :
            (VrCamera.MainCamera ? VrCamera.MainCamera : Camera.main);
        if (!cam) return;

        // Anchor using yaw-only forward from body/eye — then stay put.
        var flatForward = cam.transform.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-4f) flatForward = Vector3.forward;
        flatForward.Normalize();

        transform.position = cam.transform.position + flatForward * ForwardOffset + Vector3.up * HeightOffset;
        transform.rotation = Quaternion.LookRotation(flatForward, Vector3.up);
        EnsureUiCollider();
        worldLocked = true;
        ModLog.Info($"[WorldLock][Menu] anchored '{name}' at {transform.position}");
    }

    private void EnsureUiCollider()
    {
        var rect = GetComponent<RectTransform>();
        if (!rect) return;
        var col = GetComponent<BoxCollider>() ?? gameObject.AddComponent<BoxCollider>();
        var size = rect.rect.size;
        col.size = new Vector3(Mathf.Max(Mathf.Abs(size.x), 100f), Mathf.Max(Mathf.Abs(size.y), 100f), 1f);
        col.center = new Vector3(rect.rect.center.x, rect.rect.center.y, 0f);
        col.isTrigger = true;
        col.enabled = true;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("ShipbreakerVr_EventSystem");
        DontDestroyOnLoad(es);
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }
}
