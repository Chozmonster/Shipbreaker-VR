using BBI.Unity.Game;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShipbreakerVr;

/// <summary>
/// Moves screen-space UI canvases into world space while VR is active so menus are visible in the HMD.
/// Interaction is handled by <see cref="VrUiLaser"/> (gaze/laser + click).
/// </summary>
public class VrUi : MonoBehaviour
{
    private const float ForwardOffset = 1.5f;
    private Canvas canvas;
    private Vector3 initialPosition;
    private RenderMode initialRenderMode;
    private Quaternion initialRotation;
    private Vector3 initialScale;
    private Transform target;
    private GraphicRaycaster graphicRaycaster;
    private BoxCollider uiCollider;

    private void Awake()
    {
        canvas = GetComponent<Canvas>();
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        initialScale = transform.localScale;
        initialRenderMode = canvas.renderMode;
        graphicRaycaster = GetComponent<GraphicRaycaster>();
        if (!graphicRaycaster) graphicRaycaster = gameObject.AddComponent<GraphicRaycaster>();
    }

    private void Update()
    {
        if (ModXrManager.IsVrEnabled && canvas.renderMode != RenderMode.WorldSpace) SetUpCanvas();
        else if (!ModXrManager.IsVrEnabled && canvas.renderMode == RenderMode.WorldSpace) ResetCanvas();
    }

    private void LateUpdate()
    {
        if (!target) return;

        transform.position = target.position + target.forward * ForwardOffset;
        transform.rotation = target.rotation;
    }

    private void SetUpCanvas()
    {
        target = (VrCamera.MainCamera ? VrCamera.MainCamera : Camera.main)?.transform;
        transform.localScale = Vector3.one * 0.001f;
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = VrCamera.MainCamera ? VrCamera.MainCamera : Camera.main;
        SetBehaviourEnabled(canvas.GetComponent<CanvasScalerImprover>(), false);
        SetBehaviourEnabled(canvas.GetComponent<CanvasScaler>(), false);
        EnsureUiCollider();

        // Ensure EventSystem can see this canvas for laser / mouse fallback.
        if (EventSystem.current == null)
        {
            var es = new GameObject("ShipbreakerVr_EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
    }

    private void EnsureUiCollider()
    {
        var rect = GetComponent<RectTransform>();
        if (!rect) return;

        uiCollider = GetComponent<BoxCollider>();
        if (!uiCollider) uiCollider = gameObject.AddComponent<BoxCollider>();

        // Rect size is in local canvas units; collider uses local scale separately.
        var size = rect.rect.size;
        uiCollider.size = new Vector3(Mathf.Max(size.x, 1f), Mathf.Max(size.y, 1f), 1f);
        uiCollider.center = rect.rect.center;
        uiCollider.isTrigger = true;
    }

    private void ResetCanvas()
    {
        transform.position = initialPosition;
        transform.rotation = initialRotation;
        transform.localScale = initialScale;
        canvas.renderMode = initialRenderMode;
        canvas.worldCamera = null;
        SetBehaviourEnabled(canvas.GetComponent<CanvasScaler>(), true);
        SetBehaviourEnabled(canvas.GetComponent<CanvasScalerImprover>(), true);
        if (uiCollider) uiCollider.enabled = false;
        target = null;
    }

    private static void SetBehaviourEnabled(MonoBehaviour behaviour, bool enabled)
    {
        if (!behaviour) return;
        behaviour.enabled = enabled;
    }
}
