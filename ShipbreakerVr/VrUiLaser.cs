using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;

namespace ShipbreakerVr;

/// <summary>
/// Controller laser → UI pointer (prefer right/dominant hand) + trigger click.
/// Falls back to gaze laser, then mouse/keyboard. Not full motion gameplay tools.
/// </summary>
public class VrUiLaser : MonoBehaviour
{
    private const float MaxDistance = 8f;
    private LineRenderer line;
    private Camera eventCamera;
    private GameObject currentHover;
    private bool clickHeld;
    private bool loggedControllerOnce;

    private void Start()
    {
        var go = new GameObject("ShipbreakerVr_UiLaser");
        go.transform.SetParent(transform, false);
        line = go.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.startWidth = 0.006f;
        line.endWidth = 0.0015f;
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("UI/Default");
        if (shader != null) line.material = new Material(shader);
        line.startColor = new Color(0.4f, 0.85f, 1f, 0.95f);
        line.endColor = new Color(0.4f, 0.85f, 1f, 0.15f);
        line.enabled = false;
        line.useWorldSpace = true;
        // Draw over UI / HDRP quirks
        line.sortingOrder = 5000;
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

        if (!TryGetPointerRay(out var origin, out var direction, out var source))
        {
            line.enabled = false;
            ClearHover();
            return;
        }

        if (!loggedControllerOnce && source == PointerSource.Controller)
        {
            Debug.Log("[ShipbreakerVr] UI laser using XR controller pose (trigger to click).");
            loggedControllerOnce = true;
        }

        line.enabled = true;
        line.SetPosition(0, origin);
        line.SetPosition(1, origin + direction * MaxDistance);

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

    private enum PointerSource
    {
        Controller,
        Gaze,
        Mouse
    }

    private bool TryGetPointerRay(out Vector3 origin, out Vector3 direction, out PointerSource source)
    {
        origin = default;
        direction = Vector3.forward;
        source = PointerSource.Gaze;

        // 1) Prefer tracked controller (right, then left).
        if (TryGetControllerRay(preferRight: true, out origin, out direction) ||
            TryGetControllerRay(preferRight: false, out origin, out direction))
        {
            source = PointerSource.Controller;
            return true;
        }

        // 2) Gaze fallback from HMD / main camera.
        if (eventCamera)
        {
            origin = eventCamera.transform.position;
            direction = eventCamera.transform.forward;
            source = PointerSource.Gaze;
            return true;
        }

        // 3) Desktop mouse ray if a camera exists (flat debug).
        if (Camera.main && Input.mousePresent)
        {
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            origin = ray.origin;
            direction = ray.direction;
            source = PointerSource.Mouse;
            return true;
        }

        return false;
    }

    private static bool TryGetControllerRay(bool preferRight, out Vector3 origin, out Vector3 direction)
    {
        origin = default;
        direction = Vector3.forward;

        var characteristics = InputDeviceCharacteristics.Controller |
                              InputDeviceCharacteristics.HeldInHand |
                              (preferRight ? InputDeviceCharacteristics.Right : InputDeviceCharacteristics.Left);

        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(characteristics, devices);

        foreach (var device in devices)
        {
            if (!device.isValid) continue;
            if (!device.TryGetFeatureValue(CommonUsages.devicePosition, out var pos)) continue;
            if (!device.TryGetFeatureValue(CommonUsages.deviceRotation, out var rot)) continue;

            // Reject unset poses (common before tracking warms up).
            if (pos == Vector3.zero && rot == Quaternion.identity) continue;

            origin = pos;
            direction = rot * Vector3.forward;
            if (direction.sqrMagnitude < 0.0001f) continue;
            direction.Normalize();
            return true;
        }

        return false;
    }

    private bool RaycastUi(Vector3 origin, Vector3 direction, out float distance, out GameObject hitObject)
    {
        distance = MaxDistance;
        hitObject = null;

        // Physics against world-space canvas colliders (VrUi adds these).
        if (Physics.Raycast(origin, direction, out var physHit, MaxDistance, ~0, QueryTriggerInteraction.Collide))
        {
            var canvas = physHit.collider.GetComponentInParent<Canvas>();
            if (canvas && canvas.renderMode == RenderMode.WorldSpace)
            {
                distance = physHit.distance;
                hitObject = ResolveUiTarget(canvas, physHit.point, origin, direction) ??
                            physHit.collider.gameObject;
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

        // GraphicRaycaster fallback: project a point along the laser into the event camera.
        if (EventSystem.current == null || eventCamera == null) return false;

        var samplePoint = origin + direction * 2f;
        var screenPoint = eventCamera.WorldToScreenPoint(samplePoint);
        if (screenPoint.z < 0f) return false;

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

    private static GameObject ResolveUiTarget(Canvas canvas, Vector3 worldPoint, Vector3 rayOrigin,
        Vector3 rayDirection)
    {
        var cam = canvas.worldCamera ? canvas.worldCamera : Camera.main;
        if (!cam || EventSystem.current == null) return null;

        // Prefer intersecting the canvas plane then converting that point to screen for GraphicRaycaster.
        var screenPoint = cam.WorldToScreenPoint(worldPoint);
        var pointer = new PointerEventData(EventSystem.current)
        {
            position = new Vector2(screenPoint.x, screenPoint.y)
        };

        var results = new List<RaycastResult>();
        var raycaster = canvas.GetComponent<GraphicRaycaster>();
        if (raycaster != null)
            raycaster.Raycast(pointer, results);
        else
            EventSystem.current.RaycastAll(pointer, results);

        foreach (var result in results)
        {
            if (!result.gameObject) continue;
            var selectable = result.gameObject.GetComponentInParent<Selectable>();
            if (selectable) return selectable.gameObject;
        }

        if (results.Count > 0) return results[0].gameObject;

        // Last resort: raycast all selectables under this canvas with Physics-less rect checks.
        var selectables = canvas.GetComponentsInChildren<Selectable>(false);
        Selectable best = null;
        var bestDist = float.MaxValue;
        foreach (var s in selectables)
        {
            if (!s.IsInteractable() || !s.gameObject.activeInHierarchy) continue;
            var rt = s.transform as RectTransform;
            if (!rt) continue;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var plane = new Plane(canvas.transform.forward, corners[0]);
            var ray = new Ray(rayOrigin, rayDirection);
            if (!plane.Raycast(ray, out var enter) || enter < 0f || enter > MaxDistance) continue;
            var hit = ray.GetPoint(enter);
            if (!RectTransformUtility.RectangleContainsScreenPoint(rt, cam.WorldToScreenPoint(hit), cam))
                continue;
            if (enter < bestDist)
            {
                bestDist = enter;
                best = s;
            }
        }

        return best ? best.gameObject : null;
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
        if (Input.GetMouseButton(0) || Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.Space))
            return true;

        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.HeldInHand, devices);
        foreach (var device in devices)
        {
            if (!device.isValid) continue;
            if (device.TryGetFeatureValue(CommonUsages.triggerButton, out var trigger) && trigger)
                return true;
            if (device.TryGetFeatureValue(CommonUsages.trigger, out var triggerAxis) && triggerAxis > 0.75f)
                return true;
            if (device.TryGetFeatureValue(CommonUsages.primaryButton, out var primary) && primary)
                return true;
        }

        return false;
    }
}
