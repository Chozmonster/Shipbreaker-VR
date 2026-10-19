using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;

namespace ShipbreakerVr;

/// <summary>
/// Controller → UI pointer. XRNode first. Gaze is failure mode (logged), not success.
/// </summary>
public class VrUiLaser : MonoBehaviour
{
    private const float MaxDistance = 12f;
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
    private int gazeFallbackLogs;

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
            line.material = new Material(shader) { color = new Color(0.2f, 0.9f, 1f, 1f) };

        line.startColor = new Color(0.2f, 0.95f, 1f, 1f);
        line.endColor = new Color(0.2f, 0.95f, 1f, 0.2f);
        line.enabled = false;
        line.useWorldSpace = true;
        line.sortingOrder = 9000;

        EnsureVirtualMouse();
        ModLog.Info("[UiPointer] laser ready — expect source=XrNodeRight/Left when hands tracked (gaze ≠ success).");
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
        TrySetInputOverride(module, virtualMouse);
    }

    private static void TrySetInputOverride(StandaloneInputModule module, BaseInput input)
    {
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

        eventCamera = MenuWorldPlacement.ResolveAnyCamera();
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

        if (source == PointerSource.Gaze)
        {
            gazeFallbackLogs++;
            if (gazeFallbackLogs <= 3 || gazeFallbackLogs % 30 == 0)
                ModLog.Warn(
                    "[UiPointer] GAZE FALLBACK (not success) — no tracked controller pose. " +
                    "Hands should yield source=XrNodeRight/Left.");
        }

        line.enabled = true;
        line.SetPosition(0, origin);
        line.SetPosition(1, origin + direction * MaxDistance);

        var hitUi = RaycastUi(origin, direction, out var distance, out var hitObject, out var screenPoint,
            out var selectableName);
        if (hitUi)
            line.SetPosition(1, origin + direction * distance);
        else
            ClearHover();

        if (hitUi) UpdateHover(hitObject);

        var trigger = IsTriggerPressed();
        if (virtualMouse != null)
        {
            var useVirtual = source is PointerSource.XrNodeRight or PointerSource.XrNodeLeft
                or PointerSource.InputDevice;
            virtualMouse.Active = useVirtual;
            if (useVirtual)
            {
                if (!hitUi && eventCamera)
                    screenPoint = eventCamera.WorldToScreenPoint(origin + direction * 2f);
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
            LogPointerStatus(hitObject, selectableName, trigger, hitUi, distance);
        }
    }

    private void LogPointerStatus(GameObject hover, string selectableName, bool trigger, bool hitUi,
        float distance)
    {
        var sb = new StringBuilder();
        sb.Append("[UiPointer] ");
        sb.Append($"source={lastSource} device='{lastDeviceName}' ");
        sb.Append($"origin={lastOrigin} dir={lastDir} ");
        sb.Append($"menuOpen={menuLikelyOpen} hitUi={hitUi} dist={distance:0.00} ");
        sb.Append($"trigger={trigger} ");
        sb.Append($"hover={(hover ? hover.name : "none")} ");
        sb.Append($"selectable={(string.IsNullOrEmpty(selectableName) ? "none" : selectableName)} ");
        sb.Append($"virtualMouse={(virtualMouse != null && virtualMouse.Active)} ");
        sb.Append(
            $"eventCam={(eventCamera ? eventCamera.name : "null")} ");

        var nodes = new List<XRNodeState>();
        InputTracking.GetNodeStates(nodes);
        sb.Append("nodes=");
        foreach (var n in nodes)
        {
            if (n.nodeType is XRNode.RightHand or XRNode.LeftHand or XRNode.Head)
            {
                n.TryGetPosition(out var p);
                sb.Append($"[{n.nodeType} tracked={n.tracked} pos={p}]");
            }
        }

        if (lastSource == PointerSource.Gaze)
            ModLog.Warn(sb.ToString());
        else
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

        // Gaze = last resort only
        if (MenuWorldPlacement.TryGetPlayerView(out var view))
        {
            origin = view.Position;
            direction = (view.Rotation * Vector3.forward).normalized;
            source = PointerSource.Gaze;
            deviceName = "Gaze/" + view.Source;
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
        out Vector3 screenPoint, out string selectableName)
    {
        distance = MaxDistance;
        hitObject = null;
        screenPoint = Vector3.zero;
        selectableName = null;

        if (Physics.Raycast(origin, direction, out var physHit, MaxDistance, ~0, QueryTriggerInteraction.Collide))
        {
            var canvas = physHit.collider.GetComponentInParent<Canvas>();
            if (canvas && canvas.renderMode == RenderMode.WorldSpace)
            {
                distance = physHit.distance;
                var cam = canvas.worldCamera ? canvas.worldCamera : eventCamera;
                screenPoint = cam ? cam.WorldToScreenPoint(physHit.point) : Vector3.zero;
                var sel = FindSelectableAt(canvas, physHit.point);
                hitObject = sel ?? physHit.collider.gameObject;
                selectableName = sel ? sel.name : null;
                return true;
            }
        }

        // Plane test — also try GraphicRaycaster via EventSystem
        Canvas bestCanvas = null;
        var bestEnter = MaxDistance;
        Vector3 bestPoint = default;

        foreach (var canvas in FindObjectsOfType<Canvas>())
        {
            if (!canvas || !canvas.isRootCanvas || !canvas.isActiveAndEnabled) continue;
            if (canvas.renderMode != RenderMode.WorldSpace) continue;
            var rt = canvas.GetComponent<RectTransform>();
            if (!rt) continue;

            // Canvas faces viewer with same rot as camera → plane normal is canvas.forward
            var plane = new Plane(canvas.transform.forward, canvas.transform.position);
            var ray = new Ray(origin, direction);
            if (!plane.Raycast(ray, out var enter) || enter < 0.05f || enter > MaxDistance) continue;

            var point = ray.GetPoint(enter);
            // Local-space rect test (more reliable than screen for wrong worldCamera)
            if (!PointInCanvasRect(rt, point)) continue;

            if (enter < bestEnter)
            {
                bestEnter = enter;
                bestPoint = point;
                bestCanvas = canvas;
            }
        }

        if (bestCanvas)
        {
            distance = bestEnter;
            var cam = bestCanvas.worldCamera ? bestCanvas.worldCamera : eventCamera;
            screenPoint = cam ? cam.WorldToScreenPoint(bestPoint) : Vector3.zero;
            var sel = FindSelectableAt(bestCanvas, bestPoint);
            hitObject = sel ?? bestCanvas.gameObject;
            selectableName = sel ? sel.name : null;
            return true;
        }

        if (eventCamera)
            screenPoint = eventCamera.WorldToScreenPoint(origin + direction * 2f);
        return false;
    }

    private static bool PointInCanvasRect(RectTransform rt, Vector3 worldPoint)
    {
        var local = rt.InverseTransformPoint(worldPoint);
        var rect = rt.rect;
        // Generous padding so slightly-off rays still count
        const float pad = 20f;
        return local.x >= rect.xMin - pad && local.x <= rect.xMax + pad &&
               local.y >= rect.yMin - pad && local.y <= rect.yMax + pad;
    }

    private static GameObject FindSelectableAt(Canvas canvas, Vector3 worldPoint)
    {
        var selectables = canvas.GetComponentsInChildren<Selectable>(false);
        GameObject best = null;
        var bestDist = float.MaxValue;
        foreach (var s in selectables)
        {
            if (!s.IsInteractable() || !s.gameObject.activeInHierarchy) continue;
            var rt = s.transform as RectTransform;
            if (!rt) continue;
            var local = rt.InverseTransformPoint(worldPoint);
            var rect = rt.rect;
            if (local.x < rect.xMin || local.x > rect.xMax || local.y < rect.yMin || local.y > rect.yMax)
                continue;
            var d = local.z * local.z;
            if (d < bestDist)
            {
                bestDist = d;
                best = s.gameObject;
            }
        }

        return best;
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
        if (Active && button == 0) return LeftPressed;
        return base.GetMouseButtonDown(button);
    }
}
