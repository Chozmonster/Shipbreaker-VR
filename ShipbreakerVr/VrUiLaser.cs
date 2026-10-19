using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;

namespace ShipbreakerVr;

/// <summary>
/// Week-1 menu interaction: head-gaze laser + click (mouse left / controller trigger).
/// Full controller-hand lasers and motion tools are deferred — not claimed done.
/// Goal: main menu → Free Play without removing the headset.
/// </summary>
public class VrUiLaser : MonoBehaviour
{
    private const float MaxDistance = 8f;
    private LineRenderer line;
    private Camera eventCamera;
    private GameObject currentHover;
    private bool clickHeld;

    private void Start()
    {
        var go = new GameObject("ShipbreakerVr_UiLaser");
        go.transform.SetParent(transform, false);
        line = go.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.startWidth = 0.005f;
        line.endWidth = 0.001f;
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("UI/Default");
        if (shader != null) line.material = new Material(shader);
        line.startColor = new Color(0.85f, 0.95f, 1f, 0.9f);
        line.endColor = new Color(0.85f, 0.95f, 1f, 0.2f);
        line.enabled = false;
        line.useWorldSpace = true;
    }

    private void Update()
    {
        if (!ModXrManager.IsVrEnabled)
        {
            line.enabled = false;
            ClearHover();
            return;
        }

        eventCamera = VrCamera.MainCamera ? VrCamera.MainCamera : Camera.main;
        if (!eventCamera)
        {
            line.enabled = false;
            return;
        }

        var origin = eventCamera.transform.position;
        var direction = eventCamera.transform.forward;
        var end = origin + direction * MaxDistance;

        line.enabled = true;
        line.SetPosition(0, origin);
        line.SetPosition(1, end);

        var hit = RaycastUi(origin, direction, out var distance, out var hitObject);
        if (hit)
        {
            line.SetPosition(1, origin + direction * distance);
            UpdateHover(hitObject);
        }
        else
        {
            ClearHover();
        }

        var pressed = IsClickPressed();
        if (pressed && !clickHeld && hitObject != null)
            ExecuteClick(hitObject);
        clickHeld = pressed;
    }

    private bool RaycastUi(Vector3 origin, Vector3 direction, out float distance, out GameObject hitObject)
    {
        distance = MaxDistance;
        hitObject = null;

        // Prefer physics hit against world-space canvas colliders (added by VrUi).
        if (Physics.Raycast(origin, direction, out var physHit, MaxDistance, ~0, QueryTriggerInteraction.Collide))
        {
            var canvas = physHit.collider.GetComponentInParent<Canvas>();
            if (canvas && canvas.renderMode == RenderMode.WorldSpace)
            {
                distance = physHit.distance;
                hitObject = ResolveUiTarget(canvas, physHit.point) ?? physHit.collider.gameObject;
                return hitObject != null;
            }

            var selectable = physHit.collider.GetComponentInParent<Selectable>();
            if (selectable)
            {
                hitObject = selectable.gameObject;
                distance = physHit.distance;
                return true;
            }
        }

        // Fallback: GraphicRaycaster via EventSystem using gaze projected to screen.
        if (EventSystem.current == null || eventCamera == null) return false;

        var screenPoint = eventCamera.WorldToScreenPoint(origin + direction * 2f);
        var pointerData = new PointerEventData(EventSystem.current)
        {
            position = new Vector2(screenPoint.x, screenPoint.y)
        };

        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);
        foreach (var result in results)
        {
            if (!result.gameObject) continue;
            var selectable = result.gameObject.GetComponentInParent<Selectable>();
            hitObject = selectable ? selectable.gameObject : result.gameObject;
            distance = result.distance > 0 ? result.distance : 2f;
            return true;
        }

        return false;
    }

    private static GameObject ResolveUiTarget(Canvas canvas, Vector3 worldPoint)
    {
        var cam = canvas.worldCamera ? canvas.worldCamera : Camera.main;
        if (!cam) return null;

        var screenPoint = cam.WorldToScreenPoint(worldPoint);
        var pointer = new PointerEventData(EventSystem.current)
        {
            position = screenPoint
        };

        var results = new List<RaycastResult>();
        var raycaster = canvas.GetComponent<GraphicRaycaster>();
        if (raycaster != null)
            raycaster.Raycast(pointer, results);
        else if (EventSystem.current != null)
            EventSystem.current.RaycastAll(pointer, results);

        foreach (var result in results)
        {
            if (!result.gameObject) continue;
            var selectable = result.gameObject.GetComponentInParent<Selectable>();
            if (selectable) return selectable.gameObject;
        }

        return results.Count > 0 ? results[0].gameObject : null;
    }

    private void UpdateHover(GameObject target)
    {
        if (currentHover == target) return;
        ClearHover();
        currentHover = target;
        if (currentHover == null || EventSystem.current == null) return;

        var pointer = new PointerEventData(EventSystem.current);
        ExecuteEvents.Execute(currentHover, pointer, ExecuteEvents.pointerEnterHandler);
    }

    private void ClearHover()
    {
        if (currentHover == null) return;
        if (EventSystem.current != null)
        {
            var pointer = new PointerEventData(EventSystem.current);
            ExecuteEvents.Execute(currentHover, pointer, ExecuteEvents.pointerExitHandler);
        }

        currentHover = null;
    }

    private void ExecuteClick(GameObject target)
    {
        if (EventSystem.current == null || target == null) return;

        var pointer = new PointerEventData(EventSystem.current)
        {
            button = PointerEventData.InputButton.Left
        };

        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.submitHandler);

        Debug.Log($"[ShipbreakerVr] UI click → {target.name}");
    }

    private static bool IsClickPressed()
    {
        if (Input.GetMouseButton(0) || Input.GetKey(KeyCode.JoystickButton0) || Input.GetKey(KeyCode.Return) ||
            Input.GetKey(KeyCode.Space))
            return true;

        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.HeldInHand, devices);
        foreach (var device in devices)
        {
            if (device.TryGetFeatureValue(CommonUsages.triggerButton, out var trigger) && trigger)
                return true;
            if (device.TryGetFeatureValue(CommonUsages.primaryButton, out var primary) && primary)
                return true;
        }

        return false;
    }
}
