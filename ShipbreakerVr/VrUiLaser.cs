using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;

namespace ShipbreakerVr;

/// <summary>
/// Controller → UI pointer. Uses XRNode states first (works when InputDevices is empty),
/// then InputDevices. Feeds a virtual mouse into StandaloneInputModule so Doozy/uGUI
/// see controller aim as mouse. Gaze fallback only if no controller tracked.
/// </summary>
public class VrUiLaser : MonoBehaviour
{
    private const float MaxDistance = 10f;
    private const float StatusInterval = 1f;

    private LineRenderer line;
    private Camera eventCamera;
    private GameObject currentHover;
    private bool clickHeld;
    private float nextStatus;
    private PointerSource lastSource = PointerSource.None;
    private string lastDeviceName = "(none)";
    private Vector3 lastOrigin;
    private Vector3 lastDir;
    private VrVirtualMouseInput virtualMouse;
    private bool menuLikelyOpen;

    private enum PointerSource
    {
        None,
        XrNodeRight,
        XrNodeLeft,
        InputDevice,
        Gaze
    }

    private void Start()
    {
        var go = new GameObject("ShipbreakerVr_UiLaser");
        DontDestroyOnLoad(go);
        line = go.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.startWidth = 0.007f;
        line.endWidth = 0.002f;
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("UI/Default");
        if (shader != null)
        {
            line.material = new Material(shader) { color = new Color(0.2f, 0.9f, 1f, 1f) };
        }

        line.startColor = new Color(0.2f, 0.95f, 1f, 1f);
        line.endColor = new Color(0.2f, 0.95f, 1f, 0.2f);
        line.enabled = false;
        line.useWorldSpace = true;
        line.sortingOrder = 9000;

        EnsureVirtualMouse();
        ModLog.Info("[UiPointer] laser ready (XRNode → InputDevices → gaze).");
    }

    private void EnsureVirtualMouse()
    {
        if (EventSystem.current == null)
        {
            var es = new GameObject("ShipbreakerVr_EventSystem");
            DontDestroyOnLoad(es);
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        var module = EventSystem.current.GetComponent<StandaloneInputModule>();
        if (!module) module = EventSystem.current.gameObject.AddComponent<StandaloneInputModule>();

        virtualMouse = EventSystem.current.GetComponent<VrVirtualMouseInput>();
        if (!virtualMouse) virtualMouse = EventSystem.current.gameObject.AddComponent<VrVirtualMouseInput>();
        // StandaloneInputModule uses its own input override via reflection in older Unity —
        // also assign through InputModule's m_InputOverride if present.
        TrySetInputOverride(module, virtualMouse);
        ModLog.Info("[UiPointer] virtual mouse bound to EventSystem/StandaloneInputModule.");
    }

    private static void TrySetInputOverride(StandaloneInputModule module, BaseInput input)
    {
        // Unity UI StandaloneInputModule has protected input override via `input` / m_InputOverride.
        var type = typeof(BaseInputModule);
        var field = type.GetField("m_InputOverride",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field != null)
        {
            field.SetValue(module, input);
            return;
        }

        var prop = type.GetProperty("inputOverride",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        prop?.SetValue(module, input, null);
    }

    private void Update()
    {
        if (!ModXrManager.IsVrEnabled)
        {
            line.enabled = false;
            if (virtualMouse) virtualMouse.Active = false;
            return;
        }

        if (EventSystem.current == null || virtualMouse == null) EnsureVirtualMouse();

        eventCamera = VrCamera.MainCamera ? VrCamera.MainCamera : Camera.main;
        menuLikelyOpen = IsAnyWorldMenuUp();

        if (!TryGetPointerRay(out var origin, out var direction, out var source, out var deviceName))
        {
            line.enabled = false;
            if (virtualMouse) virtualMouse.Active = false;
            return;
        }

        lastSource = source;
        lastDeviceName = deviceName;
        lastOrigin = origin;
        lastDir = direction;

        // Prefer controller; only use gaze if no controller (and log that clearly).
        line.enabled = true;
        line.SetPosition(0, origin);
        line.SetPosition(1, origin + direction * MaxDistance);

        var hitUi = RaycastUi(origin, direction, out var distance, out var hitObject, out var screenPoint);
        if (hitUi)
        {
            line.SetPosition(1, origin + direction * distance);
            UpdateHover(hitObject);
        }
        else
        {
            ClearHover();
            // Still project a mouse point along the ray for Doozy hover at distance
            if (eventCamera)
                screenPoint = eventCamera.WorldToScreenPoint(origin + direction * 2f);
        }

        var trigger = IsTriggerPressed();
        if (virtualMouse != null)
        {
            // Drive virtual mouse whenever we have a controller pointer (not gaze-only),
            // so menus receive controller-as-mouse even before a collider hit.
            var useVirtual = source != PointerSource.Gaze && source != PointerSource.None;
            virtualMouse.Active = useVirtual;
            if (useVirtual)
            {
                virtualMouse.ScreenPosition = new Vector2(screenPoint.x, screenPoint.y);
                virtualMouse.LeftPressed = trigger;
            }
        }

        if (trigger && !clickHeld && hitObject != null)
            ExecuteClick(hitObject);
        clickHeld = trigger;

        if (Time.unscaledTime >= nextStatus)
        {
            nextStatus = Time.unscaledTime + StatusInterval;
            LogPointerStatus(hitObject, trigger);
        }
    }

    private void LogPointerStatus(GameObject hover, bool trigger)
    {
        var sb = new StringBuilder();
        sb.Append("[UiPointer] ");
        sb.Append($"source={lastSource} device='{lastDeviceName}' ");
        sb.Append($"origin={lastOrigin} dir={lastDir} ");
        sb.Append($"menuOpen={menuLikelyOpen} trigger={trigger} ");
        sb.Append($"hover={(hover ? hover.name : "none")} ");
        sb.Append($"virtualMouse={(virtualMouse != null && virtualMouse.Active)}");
        // List XR nodes briefly
        var nodes = new List<XRNodeState>();
        InputTracking.GetNodeStates(nodes);
        sb.Append(" nodes=");
        foreach (var n in nodes)
        {
            if (n.nodeType is XRNode.RightHand or XRNode.LeftHand or XRNode.GameController)
            {
                n.TryGetPosition(out var p);
                sb.Append($"[{n.nodeType} tracked={n.tracked} pos={p}]");
            }
        }

        ModLog.Info(sb.ToString());
    }

    private static bool IsAnyWorldMenuUp()
    {
        foreach (var c in FindObjectsOfType<Canvas>())
        {
            if (c && c.isRootCanvas && c.isActiveAndEnabled && c.renderMode == RenderMode.WorldSpace)
                return true;
        }

        return false;
    }

    private bool TryGetPointerRay(out Vector3 origin, out Vector3 direction, out PointerSource source,
        out string deviceName)
    {
        origin = default;
        direction = Vector3.forward;
        source = PointerSource.None;
        deviceName = "(none)";

        if (TryGetXrNodeRay(XRNode.RightHand, out origin, out direction))
        {
            source = PointerSource.XrNodeRight;
            deviceName = "XRNode.RightHand";
            return true;
        }

        if (TryGetXrNodeRay(XRNode.LeftHand, out origin, out direction))
        {
            source = PointerSource.XrNodeLeft;
            deviceName = "XRNode.LeftHand";
            return true;
        }

        if (TryGetInputDeviceRay(out origin, out direction, out deviceName))
        {
            source = PointerSource.InputDevice;
            return true;
        }

        if (eventCamera)
        {
            origin = eventCamera.transform.position;
            direction = eventCamera.transform.forward;
            source = PointerSource.Gaze;
            deviceName = "Gaze/HMD";
            return true;
        }

        return false;
    }

    private static bool TryGetXrNodeRay(XRNode node, out Vector3 origin, out Vector3 direction)
    {
        origin = default;
        direction = Vector3.forward;
        var nodes = new List<XRNodeState>();
        InputTracking.GetNodeStates(nodes);
        foreach (var n in nodes)
        {
            if (n.nodeType != node) continue;
            if (!n.tracked) continue;
            if (!n.TryGetPosition(out origin)) continue;
            if (!n.TryGetRotation(out var rot)) continue;
            direction = (rot * Vector3.forward).normalized;
            return direction.sqrMagnitude > 1e-6f;
        }

        return false;
    }

    private static bool TryGetInputDeviceRay(out Vector3 origin, out Vector3 direction, out string deviceName)
    {
        origin = default;
        direction = Vector3.forward;
        deviceName = "(none)";

        var devices = new List<InputDevice>();
        InputDevices.GetDevices(devices);
        // Prefer right-ish controllers first
        devices.Sort((a, b) => Score(b).CompareTo(Score(a)));

        foreach (var d in devices)
        {
            if (!d.isValid) continue;
            var chars = d.characteristics;
            if ((chars & InputDeviceCharacteristics.HeadMounted) != 0) continue;
            if ((chars & InputDeviceCharacteristics.Controller) == 0 &&
                (chars & InputDeviceCharacteristics.HeldInHand) == 0)
                continue;

            if (!d.TryGetFeatureValue(CommonUsages.devicePosition, out var pos)) continue;
            if (!d.TryGetFeatureValue(CommonUsages.deviceRotation, out var rot)) continue;
            if (!d.TryGetFeatureValue(CommonUsages.isTracked, out var tracked)) tracked = true;
            if (!tracked) continue;

            origin = pos;
            direction = (rot * Vector3.forward).normalized;
            deviceName = $"{d.name}|{chars}";
            return true;
        }

        return false;

        static int Score(InputDevice d)
        {
            var c = d.characteristics;
            var s = 0;
            if ((c & InputDeviceCharacteristics.Right) != 0) s += 10;
            if ((c & InputDeviceCharacteristics.Controller) != 0) s += 5;
            if ((c & InputDeviceCharacteristics.HeldInHand) != 0) s += 3;
            return s;
        }
    }

    private bool RaycastUi(Vector3 origin, Vector3 direction, out float distance, out GameObject hitObject,
        out Vector3 screenPoint)
    {
        distance = MaxDistance;
        hitObject = null;
        screenPoint = Vector3.zero;

        if (Physics.Raycast(origin, direction, out var physHit, MaxDistance, ~0, QueryTriggerInteraction.Collide))
        {
            var canvas = physHit.collider.GetComponentInParent<Canvas>();
            if (canvas && canvas.renderMode == RenderMode.WorldSpace)
            {
                distance = physHit.distance;
                screenPoint = (canvas.worldCamera ? canvas.worldCamera : eventCamera)
                    ? (canvas.worldCamera ? canvas.worldCamera : eventCamera).WorldToScreenPoint(physHit.point)
                    : Vector3.zero;
                hitObject = FindSelectableAt(canvas, physHit.point) ?? physHit.collider.gameObject;
                return true;
            }
        }

        // Plane test against world-space canvases (works even if collider missing/wrong size)
        foreach (var canvas in FindObjectsOfType<Canvas>())
        {
            if (!canvas || !canvas.isRootCanvas || !canvas.isActiveAndEnabled) continue;
            if (canvas.renderMode != RenderMode.WorldSpace) continue;
            var rt = canvas.GetComponent<RectTransform>();
            if (!rt) continue;

            var plane = new Plane(canvas.transform.forward, canvas.transform.position);
            var ray = new Ray(origin, direction);
            if (!plane.Raycast(ray, out var enter) || enter < 0f || enter > MaxDistance) continue;
            var point = ray.GetPoint(enter);
            var cam = canvas.worldCamera ? canvas.worldCamera : eventCamera;
            if (!cam) continue;
            if (!RectTransformUtility.RectangleContainsScreenPoint(rt, cam.WorldToScreenPoint(point), cam))
                continue;

            distance = enter;
            screenPoint = cam.WorldToScreenPoint(point);
            hitObject = FindSelectableAt(canvas, point) ?? canvas.gameObject;
            return true;
        }

        if (eventCamera)
            screenPoint = eventCamera.WorldToScreenPoint(origin + direction * 2f);
        return false;
    }

    private static GameObject FindSelectableAt(Canvas canvas, Vector3 worldPoint)
    {
        var cam = canvas.worldCamera ? canvas.worldCamera : Camera.main;
        if (!cam) return null;
        var screen = cam.WorldToScreenPoint(worldPoint);
        var selectables = canvas.GetComponentsInChildren<Selectable>(false);
        foreach (var s in selectables)
        {
            if (!s.IsInteractable() || !s.gameObject.activeInHierarchy) continue;
            var rt = s.transform as RectTransform;
            if (!rt) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(rt, screen, cam))
                return s.gameObject;
        }

        return null;
    }

    private void UpdateHover(GameObject target)
    {
        if (currentHover == target) return;
        ClearHover();
        currentHover = target;
        if (!currentHover || EventSystem.current == null) return;
        ExecuteEvents.Execute(currentHover, new PointerEventData(EventSystem.current),
            ExecuteEvents.pointerEnterHandler);
    }

    private void ClearHover()
    {
        if (!currentHover) return;
        if (EventSystem.current != null)
            ExecuteEvents.Execute(currentHover, new PointerEventData(EventSystem.current),
                ExecuteEvents.pointerExitHandler);
        currentHover = null;
    }

    private void ExecuteClick(GameObject target)
    {
        if (!target || EventSystem.current == null) return;
        var pointer = new PointerEventData(EventSystem.current)
            { button = PointerEventData.InputButton.Left };
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.submitHandler);
        ModLog.Info($"[UiPointer] CLICK → {target.name}");
    }

    private static bool IsTriggerPressed()
    {
        if (Input.GetMouseButton(0)) return true;

        var nodes = new List<XRNodeState>();
        InputTracking.GetNodeStates(nodes);
        // Triggers come from InputDevices more reliably than XRNodeState
        var devices = new List<InputDevice>();
        InputDevices.GetDevices(devices);
        foreach (var d in devices)
        {
            if (!d.isValid) continue;
            if (d.TryGetFeatureValue(CommonUsages.triggerButton, out var b) && b) return true;
            if (d.TryGetFeatureValue(CommonUsages.trigger, out var a) && a > 0.7f) return true;
            if (d.TryGetFeatureValue(CommonUsages.primaryButton, out var p) && p) return true;
        }

        return false;
    }
}

/// <summary>Feeds controller aim/trigger into Unity UI as a mouse.</summary>
public class VrVirtualMouseInput : BaseInput
{
    public bool Active;
    public Vector2 ScreenPosition;
    public bool LeftPressed;

    public override Vector2 mousePosition => Active ? ScreenPosition : base.mousePosition;

    public override bool GetMouseButton(int button)
    {
        if (Active && button == 0) return LeftPressed;
        return base.GetMouseButton(button);
    }

    public override bool GetMouseButtonDown(int button)
    {
        // Edge detection handled by input module via GetMouseButton; keep simple.
        if (Active && button == 0) return LeftPressed;
        return base.GetMouseButtonDown(button);
    }
}
