using BBI.Unity.Game;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShipbreakerVr;

/// <summary>
/// World-space menus that stay put while the player looks around.
/// Stock ShipbreakerVR re-parented/followed the HMD every LateUpdate — that feels
/// glued to the headset. We place once (or on recenter) ahead of the player, yaw-locked.
/// </summary>
public class VrUi : MonoBehaviour
{
    private const float ForwardOffset = 1.75f;
    private const float HeightOffset = -0.15f;

    private Canvas canvas;
    private Vector3 initialPosition;
    private RenderMode initialRenderMode;
    private Quaternion initialRotation;
    private Vector3 initialScale;
    private Transform initialParent;
    private int initialSiblingIndex;
    private GraphicRaycaster graphicRaycaster;
    private BoxCollider uiCollider;
    private bool worldLocked;
    private bool wasActive = true;

    private void Awake()
    {
        canvas = GetComponent<Canvas>();
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        initialScale = transform.localScale;
        initialParent = transform.parent;
        initialSiblingIndex = transform.GetSiblingIndex();
        initialRenderMode = canvas.renderMode;
        graphicRaycaster = GetComponent<GraphicRaycaster>();
        if (!graphicRaycaster) graphicRaycaster = gameObject.AddComponent<GraphicRaycaster>();
    }

    private void OnEnable()
    {
        // Menu (re)opened — re-anchor in front of the player, then stay put.
        if (ModXrManager.IsVrEnabled) PlaceInFrontOfPlayer(force: true);
    }

    private void Update()
    {
        if (ModXrManager.IsVrEnabled)
        {
            if (canvas.renderMode != RenderMode.WorldSpace)
                SetUpCanvas();
            else if (!worldLocked)
                PlaceInFrontOfPlayer(force: true);

            // If a menu root was disabled then re-enabled while already world-space, re-anchor.
            var active = gameObject.activeInHierarchy && canvas.isActiveAndEnabled;
            if (active && !wasActive) PlaceInFrontOfPlayer(force: true);
            wasActive = active;

            // Optional manual recenter (does not require removing headset).
            if (Input.GetKeyDown(KeyCode.F4)) PlaceInFrontOfPlayer(force: true);
        }
        else if (canvas.renderMode == RenderMode.WorldSpace)
        {
            ResetCanvas();
        }
    }

    private void SetUpCanvas()
    {
        // Detach from any camera/UI hierarchy so we don't inherit HMD motion.
        transform.SetParent(null, true);
        transform.localScale = Vector3.one * 0.001f;
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = VrCamera.MainCamera ? VrCamera.MainCamera : Camera.main;
        SetBehaviourEnabled(canvas.GetComponent<CanvasScalerImprover>(), false);
        SetBehaviourEnabled(canvas.GetComponent<CanvasScaler>(), false);
        EnsureUiCollider();
        EnsureEventSystem();
        PlaceInFrontOfPlayer(force: true);
        Debug.Log($"[ShipbreakerVr] World-locked UI canvas '{name}'");
    }

    /// <summary>
    /// Place the canvas in world space ahead of the player (yaw-only forward), then leave it.
    /// Not called every frame — looking around must not move the menu.
    /// </summary>
    public void PlaceInFrontOfPlayer(bool force = false)
    {
        if (!ModXrManager.IsVrEnabled) return;
        if (worldLocked && !force) return;

        var cam = VrCamera.MainCamera ? VrCamera.MainCamera : Camera.main;
        if (!cam) return;

        var flatForward = cam.transform.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.ProjectOnPlane(cam.transform.rotation * Vector3.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;
        flatForward.Normalize();

        var eye = cam.transform.position;
        transform.position = eye + flatForward * ForwardOffset + Vector3.up * HeightOffset;
        transform.rotation = Quaternion.LookRotation(flatForward, Vector3.up);
        EnsureUiCollider();
        worldLocked = true;
    }

    private void EnsureUiCollider()
    {
        var rect = GetComponent<RectTransform>();
        if (!rect) return;

        uiCollider = GetComponent<BoxCollider>();
        if (!uiCollider) uiCollider = gameObject.AddComponent<BoxCollider>();

        var size = rect.rect.size;
        uiCollider.size = new Vector3(Mathf.Max(size.x, 1f), Mathf.Max(size.y, 1f), 1f);
        uiCollider.center = new Vector3(rect.rect.center.x, rect.rect.center.y, 0f);
        uiCollider.isTrigger = true;
        uiCollider.enabled = true;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("ShipbreakerVr_EventSystem");
        DontDestroyOnLoad(es);
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }

    private void ResetCanvas()
    {
        transform.SetParent(initialParent, false);
        transform.SetSiblingIndex(initialSiblingIndex);
        transform.position = initialPosition;
        transform.rotation = initialRotation;
        transform.localScale = initialScale;
        canvas.renderMode = initialRenderMode;
        canvas.worldCamera = null;
        SetBehaviourEnabled(canvas.GetComponent<CanvasScaler>(), true);
        SetBehaviourEnabled(canvas.GetComponent<CanvasScalerImprover>(), true);
        if (uiCollider) uiCollider.enabled = false;
        worldLocked = false;
    }

    private static void SetBehaviourEnabled(MonoBehaviour behaviour, bool enabled)
    {
        if (!behaviour) return;
        behaviour.enabled = enabled;
    }
}
