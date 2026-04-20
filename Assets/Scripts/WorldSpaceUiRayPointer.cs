using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class WorldSpaceUiRayPointer
{
    private static readonly List<Canvas> UiCanvases = new();
    private static readonly List<RaycastResult> UiRaycastResults = new();

    public static bool TryGetHit(
        bool isEnabled,
        string canvasName,
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        out Hit uiHit)
    {
        uiHit = default;
        if (!isEnabled || EventSystem.current == null || direction.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        RefreshUiCanvases(canvasName);
        if (UiCanvases.Count == 0)
        {
            return false;
        }

        direction.Normalize();
        var ray = new Ray(origin, direction);
        var bestDistance = Mathf.Max(0.01f, maxDistance);
        var hasHit = false;

        foreach (var canvas in UiCanvases)
        {
            if (canvas == null
                || !canvas.isActiveAndEnabled
                || canvas.renderMode != RenderMode.WorldSpace)
            {
                continue;
            }

            var rect = canvas.transform as RectTransform;
            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            if (rect == null || raycaster == null || !raycaster.isActiveAndEnabled)
            {
                continue;
            }

            var plane = new Plane(rect.forward, rect.position);
            if (!plane.Raycast(ray, out var distance)
                || distance < 0f
                || distance > bestDistance)
            {
                continue;
            }

            var worldPoint = ray.GetPoint(distance);
            var screenPoint = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, worldPoint);
            var eventData = new PointerEventData(EventSystem.current)
            {
                position = screenPoint
            };

            UiRaycastResults.Clear();
            raycaster.Raycast(eventData, UiRaycastResults);
            if (UiRaycastResults.Count == 0)
            {
                continue;
            }

            var result = UiRaycastResults[0];
            ResolvePointerRayHitPoint(ray, origin, direction, distance, result, out var hitPoint, out var hitDistance);
            result.worldPosition = hitPoint;
            result.distance = hitDistance;
            result.screenPosition = screenPoint;
            if (hitDistance > bestDistance + 0.0001f)
            {
                continue;
            }

            if (hasHit
                && Mathf.Abs(hitDistance - bestDistance) <= 0.0001f
                && uiHit.Canvas != null
                && canvas.sortingOrder <= uiHit.Canvas.sortingOrder)
            {
                continue;
            }

            bestDistance = hitDistance;
            hasHit = true;
            uiHit = new Hit
            {
                Target = result.gameObject,
                Canvas = canvas,
                WorldPoint = hitPoint,
                ScreenPosition = screenPoint,
                Distance = hitDistance,
                RaycastResult = result
            };
        }

        return hasHit;
    }

    private static void ResolvePointerRayHitPoint(
        Ray ray,
        Vector3 origin,
        Vector3 direction,
        float fallbackDistance,
        RaycastResult result,
        out Vector3 point,
        out float distance)
    {
        distance = Mathf.Max(0f, fallbackDistance);
        point = ray.GetPoint(distance);

        var candidate = result.worldPosition;
        if (!IsFinite(candidate))
        {
            return;
        }

        var projectedDistance = Vector3.Dot(candidate - origin, direction);
        if (projectedDistance <= 0f)
        {
            return;
        }

        var projectedPoint = ray.GetPoint(projectedDistance);
        if (!IsFinite(projectedPoint))
        {
            return;
        }

        distance = projectedDistance;
        point = projectedPoint;
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x)
               && float.IsFinite(value.y)
               && float.IsFinite(value.z);
    }

    public static bool Handle(
        bool isEnabled,
        bool hasUiHit,
        Hit uiHit,
        bool pressPressed,
        ref State state,
        int pointerId)
    {
        if (!isEnabled || EventSystem.current == null)
        {
            return false;
        }

        hasUiHit = hasUiHit && uiHit.Target != null;
        if (!hasUiHit && (state == null || (!state.IsPressed && state.HoveredObject == null)))
        {
            return false;
        }

        EnsurePointerData(ref state, pointerId);
        var eventData = state.EventData;
        eventData.Reset();
        eventData.pointerId = pointerId;
        eventData.button = PointerEventData.InputButton.Left;
        eventData.position = hasUiHit ? uiHit.ScreenPosition : state.LastPosition;
        eventData.delta = state.HasLastPosition ? eventData.position - state.LastPosition : Vector2.zero;
        eventData.pointerCurrentRaycast = hasUiHit ? uiHit.RaycastResult : default;
        eventData.useDragThreshold = false;

        var hoverTarget = hasUiHit
            ? ExecuteEvents.GetEventHandler<IPointerEnterHandler>(uiHit.Target)
            : null;
        eventData.pointerEnter = hoverTarget;
        UpdateHover(state.HoveredObject, hoverTarget, eventData);
        state.HoveredObject = hoverTarget;

        if (hasUiHit && hoverTarget != null)
        {
            ExecuteEvents.Execute(hoverTarget, eventData, ExecuteEvents.pointerMoveHandler);
        }

        if (pressPressed && !state.IsPressed && hasUiHit)
        {
            BeginPress(ref state, uiHit.Target, eventData);
        }

        if (pressPressed && state.IsPressed)
        {
            ContinuePress(ref state, eventData);
        }

        if (!pressPressed && state.IsPressed)
        {
            EndPress(ref state, hasUiHit ? uiHit.Target : null, eventData);
        }

        state.LastPosition = eventData.position;
        state.HasLastPosition = true;
        return hasUiHit || state.IsPressed;
    }

    private static void RefreshUiCanvases(string canvasName)
    {
        UiCanvases.Clear();
        var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var canvas in canvases)
        {
            if (canvas == null)
            {
                continue;
            }

            if (!CanvasMatchesFilter(canvas, canvasName))
            {
                continue;
            }

            UiCanvases.Add(canvas);
        }
    }

    private static bool CanvasMatchesFilter(Canvas canvas, string canvasName)
    {
        if (canvas == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(canvasName))
        {
            return true;
        }

        var names = canvasName.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i].Trim();
            if (name == "*" || string.Equals(canvas.name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsurePointerData(ref State state, int pointerId)
    {
        if (state == null)
        {
            state = new State();
        }

        if (state.EventData == null)
        {
            state.EventData = new PointerEventData(EventSystem.current)
            {
                pointerId = pointerId
            };
        }
    }

    private static void UpdateHover(GameObject oldHover, GameObject newHover, PointerEventData eventData)
    {
        if (oldHover == newHover)
        {
            return;
        }

        if (oldHover != null)
        {
            ExecuteEvents.Execute(oldHover, eventData, ExecuteEvents.pointerExitHandler);
        }

        if (newHover != null)
        {
            ExecuteEvents.Execute(newHover, eventData, ExecuteEvents.pointerEnterHandler);
        }
    }

    private static void BeginPress(ref State state, GameObject target, PointerEventData eventData)
    {
        eventData.pressPosition = eventData.position;
        eventData.pointerPressRaycast = eventData.pointerCurrentRaycast;
        eventData.eligibleForClick = true;
        eventData.clickCount = 1;
        eventData.clickTime = Time.unscaledTime;

        var pressTarget = ExecuteEvents.ExecuteHierarchy(target, eventData, ExecuteEvents.pointerDownHandler)
                          ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
        var dragTarget = ExecuteEvents.GetEventHandler<IDragHandler>(target);

        state.IsPressed = true;
        state.PointerPress = pressTarget;
        state.PointerDrag = dragTarget;
        state.RawPointerPress = target;
        state.PointerPressRaycast = eventData.pointerPressRaycast;
        state.PressPosition = eventData.pressPosition;
        state.Dragging = false;

        eventData.pointerPress = pressTarget;
        eventData.rawPointerPress = target;
        eventData.pointerDrag = dragTarget;

        if (dragTarget != null)
        {
            ExecuteEvents.Execute(dragTarget, eventData, ExecuteEvents.initializePotentialDrag);
        }

        if (pressTarget != null)
        {
            EventSystem.current.SetSelectedGameObject(pressTarget, eventData);
        }
    }

    private static void ContinuePress(ref State state, PointerEventData eventData)
    {
        eventData.pointerPress = state.PointerPress;
        eventData.rawPointerPress = state.RawPointerPress;
        eventData.pointerDrag = state.PointerDrag;
        eventData.pointerPressRaycast = state.PointerPressRaycast;
        eventData.pressPosition = state.PressPosition;
        eventData.eligibleForClick = !state.Dragging;
        eventData.dragging = state.Dragging;

        if (state.PointerDrag == null)
        {
            return;
        }

        if (!state.Dragging)
        {
            ExecuteEvents.Execute(state.PointerDrag, eventData, ExecuteEvents.beginDragHandler);
            state.Dragging = true;
        }

        eventData.dragging = true;
        ExecuteEvents.Execute(state.PointerDrag, eventData, ExecuteEvents.dragHandler);
    }

    private static void EndPress(ref State state, GameObject releaseTarget, PointerEventData eventData)
    {
        eventData.pointerPress = state.PointerPress;
        eventData.rawPointerPress = state.RawPointerPress;
        eventData.pointerDrag = state.PointerDrag;
        eventData.pointerPressRaycast = state.PointerPressRaycast;
        eventData.pressPosition = state.PressPosition;
        eventData.eligibleForClick = !state.Dragging;
        eventData.dragging = state.Dragging;

        if (state.PointerPress != null)
        {
            ExecuteEvents.Execute(state.PointerPress, eventData, ExecuteEvents.pointerUpHandler);
        }

        var clickTarget = releaseTarget != null
            ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(releaseTarget)
            : null;
        if (state.PointerPress != null && state.PointerPress == clickTarget && eventData.eligibleForClick)
        {
            ExecuteEvents.Execute(state.PointerPress, eventData, ExecuteEvents.pointerClickHandler);
        }

        if (state.Dragging && state.PointerDrag != null)
        {
            ExecuteEvents.Execute(state.PointerDrag, eventData, ExecuteEvents.endDragHandler);
        }

        state.IsPressed = false;
        state.PointerPress = null;
        state.RawPointerPress = null;
        state.PointerDrag = null;
        state.PointerPressRaycast = default;
        state.PressPosition = Vector2.zero;
        state.Dragging = false;
        eventData.eligibleForClick = false;
        eventData.dragging = false;
    }

    public struct Hit
    {
        public GameObject Target;
        public Canvas Canvas;
        public Vector3 WorldPoint;
        public Vector2 ScreenPosition;
        public float Distance;
        public RaycastResult RaycastResult;
    }

    public sealed class State
    {
        public PointerEventData EventData;
        public bool HasLastPosition;
        public Vector2 LastPosition;
        public GameObject HoveredObject;
        public bool IsPressed;
        public bool Dragging;
        public GameObject PointerPress;
        public GameObject RawPointerPress;
        public GameObject PointerDrag;
        public RaycastResult PointerPressRaycast;
        public Vector2 PressPosition;
    }
}
