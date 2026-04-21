using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class LineDrawing : MonoBehaviour
{
    private const float SnappedLineWidthMultiplier = 1.12f;
    private const float ArrowConeLength = 0.03f;
    private const float ArrowConeRadius = 0.009f;
    private const float DirectStylusGrabBackoff = 0.01f;
    private const float DirectStylusGrabMaxDistance = 8f;

    private List<GameObject> _lines = new List<GameObject>();
    private LineRenderer _currentLine;
    private List<float> _currentLineWidths = new List<float>(); //list to store line widths
    private readonly List<StrokePoint> _currentStrokePoints = new List<StrokePoint>();
    private readonly List<PressureSample> _currentPressureSamples = new List<PressureSample>();

    [SerializeField] float _maxLineWidth = 0.01f;
    [SerializeField] float _minLineWidth = 0.0005f;
    [SerializeField] private float springLineWidthMultiplier = 1.45f;
    [SerializeField] private float springReleaseDipWindowSeconds = 0.12f;
    [SerializeField] private float springReleaseDipPressureThreshold = 0.18f;
    [SerializeField] private float springReleaseDipRateThreshold = 4f;

    [SerializeField] Material _material;

    [SerializeField] private Color _currentColor;
    [SerializeField] private Color highlightColor;
    [SerializeField] private float highlightThreshold = 0.01f;
    private Color _cachedColor;
    private GameObject _highlightedLine;
    private Vector3 _grabStartPosition;
    private Quaternion _grabStartRotation;
    private Vector3[] _originalLinePositions;
    private bool _movingLine = false;
    private bool _directStylusPhysicsGrab;
    public Color CurrentColor
    {
        get { return _currentColor; }
        set
        {
            _currentColor = value;
            Debug.Log("LineDrawing color: " + _currentColor.ToString());
        }
    }

    public float MaxLineWidth
    {
        get { return _maxLineWidth; }
        set { _maxLineWidth = value; }
    }

    private bool _lineWidthIsFixed = false;
    public bool LineWidthIsFixed
    {
        get { return _lineWidthIsFixed; }
        set { _lineWidthIsFixed = value; }
    }

    private bool _isDrawing = false;
    private bool _doubleTapDetected = false;

    [SerializeField]
    private float longPressDuration = 1.0f;
    private float buttonPressedTimestamp = 0;

    [SerializeField]
    private StylusHandler _stylusHandler;
    [SerializeField] private GestureInterpreter _gestureInterpreter;
    [SerializeField] private XRContentDrawerController _controlModeSource;
    private Vector3 _previousLinePoint;
    private const float _minDistanceBetweenLinePoints = 0.0005f;
    private float _strokeStartTime;

    private struct PressureSample
    {
        public float Pressure;
        public float Timestamp;
    }

    private void Awake()
    {
        if (_gestureInterpreter == null)
        {
            _gestureInterpreter = FindFirstObjectByType<GestureInterpreter>();
        }

        ResolveControlModeSource();
    }

    private void OnDisable()
    {
        EndDirectStylusPhysicsGrab();
    }

    private void StartNewLine()
    {
        var gameObject = new GameObject("line");
        LineRenderer lineRenderer = gameObject.AddComponent<LineRenderer>();
        _currentLine = lineRenderer;
        _currentLine.positionCount = 0;
        _currentLine.material = _material;
        _currentLine.material.color = _currentColor;
        _currentLine.loop = false;
        _currentLine.startWidth = _minLineWidth;
        _currentLine.endWidth = _minLineWidth;
        _currentLine.useWorldSpace = true;
        _currentLine.alignment = LineAlignment.View;
        _currentLine.widthCurve = new AnimationCurve();
        _currentLineWidths = new List<float>();
        _currentLine.shadowCastingMode = ShadowCastingMode.Off;
        _currentLine.receiveShadows = false;
        _lines.Add(gameObject);
        _previousLinePoint = new Vector3(0, 0, 0);
        _currentStrokePoints.Clear();
        _currentPressureSamples.Clear();
        _strokeStartTime = Time.time;
    }

    private void AddPoint(Vector3 position, float width)
    {
        if (Vector3.Distance(position, _previousLinePoint) > _minDistanceBetweenLinePoints)
        {
            TriggerHaptics();
            _previousLinePoint = position;
            _currentLine.positionCount++;
            _currentLineWidths.Add(Math.Max(width * _maxLineWidth, _minLineWidth));
            _currentLine.SetPosition(_currentLine.positionCount - 1, position);
            _currentStrokePoints.Add(new StrokePoint
            {
                Position = position,
                Pressure = width,
                Timestamp = Time.time
            });

            ApplyWidthCurve(_currentLine, _currentLineWidths, _currentLineWidths.Count);
        }
    }

    private void FinalizeCurrentLine()
    {
        if (_currentLine == null || _currentStrokePoints.Count < 2 || _gestureInterpreter == null)
        {
            return;
        }

        var pressureTotal = 0f;
        for (var i = 0; i < _currentStrokePoints.Count; i++)
        {
            pressureTotal += _currentStrokePoints[i].Pressure;
        }

        var stroke = new StrokeData
        {
            Points = new List<StrokePoint>(_currentStrokePoints),
            Duration = Mathf.Max(Time.time - _strokeStartTime, 0.0001f),
            AveragePressure = pressureTotal / _currentStrokePoints.Count
        };

        var readout = _gestureInterpreter.BuildReadout(stroke);
        if (readout.DisplayPoints == null || readout.DisplayPoints.Count < 2)
        {
            return;
        }

        var pressureSettingValue = UsesPressureControlledSetting(readout)
            ? CalculateFinalPressureSettingValue()
            : 0f;

        _currentLine.positionCount = readout.DisplayPoints.Count;
        _currentLine.SetPositions(readout.DisplayPoints.ToArray());
        _currentLine.loop = ShouldRenderAsClosedLoop(readout);
        if (UsesFixedThickStroke(readout))
        {
            ApplyFixedWidthCurve(_currentLine, GetThickPhysicsLineWidth());
        }
        else
        {
            var widthMultiplier = GetWidthMultiplier(readout);
            ApplyWidthCurve(_currentLine, _currentLineWidths, readout.DisplayPoints.Count, widthMultiplier);
        }

        if (readout.PhysicsIntent != PhysicsIntentType.Spring)
        {
            SetLineColor(_currentLine, _currentColor);
        }

        UpdateArrowTipVisual(_currentLine.gameObject, readout);
        InitializePhysicsDrawing(_currentLine.gameObject, readout, pressureSettingValue);
        Debug.Log($"[LineDrawing] Snapped stroke to {readout.ShapeName} ({readout.Gesture.Confidence:0.00})");
    }

    private void InitializePhysicsDrawing(GameObject lineObject, PhysicsGestureReadoutResult readout, float pressureSettingValue)
    {
        if (lineObject == null || readout == null)
        {
            return;
        }

        lineObject.name = string.IsNullOrEmpty(readout.ShapeName) ? "PhysicsDrawing" : readout.ShapeName;
        var selectable = lineObject.GetComponent<PhysicsDrawingSelectable>();
        if (selectable == null)
        {
            selectable = lineObject.AddComponent<PhysicsDrawingSelectable>();
        }

        selectable.SetOwner(this);
        selectable.Initialize(readout, highlightColor, _currentColor);
        if (readout.PhysicsIntent == PhysicsIntentType.Spring)
        {
            selectable.SetSpringStiffness(pressureSettingValue);
        }
        else if (readout.PhysicsIntent == PhysicsIntentType.Hinge)
        {
            selectable.SetHingeTorque(pressureSettingValue);
        }
        else if (readout.PhysicsIntent == PhysicsIntentType.Impulse)
        {
            selectable.SetImpulseForce(pressureSettingValue);
        }

        if (selectable.CanAttachToPlaceable)
        {
            selectable.TryAttachToPlaceablesOnRelease();
        }
    }

    private void RecordPressureSample(float pressure)
    {
        _currentPressureSamples.Add(new PressureSample
        {
            Pressure = Mathf.Clamp01(pressure),
            Timestamp = Time.time
        });
    }

    private float CalculateFinalPressureSettingValue()
    {
        if (_currentPressureSamples.Count == 0)
        {
            return _currentStrokePoints.Count > 0
                ? Mathf.Clamp01(_currentStrokePoints[_currentStrokePoints.Count - 1].Pressure)
                : 0f;
        }

        var candidateIndex = _currentPressureSamples.Count - 1;
        var releaseTimestamp = _currentPressureSamples[candidateIndex].Timestamp;

        for (var i = candidateIndex - 1; i >= 0; i--)
        {
            var older = _currentPressureSamples[i];
            var newer = _currentPressureSamples[i + 1];
            var secondsFromRelease = releaseTimestamp - newer.Timestamp;
            var pressureDrop = older.Pressure - newer.Pressure;
            var sampleDeltaTime = Mathf.Max(newer.Timestamp - older.Timestamp, 0.0001f);
            var dropRate = pressureDrop / sampleDeltaTime;

            if (secondsFromRelease > springReleaseDipWindowSeconds
                || pressureDrop < springReleaseDipPressureThreshold
                || dropRate < springReleaseDipRateThreshold)
            {
                break;
            }

            candidateIndex = i;
        }

        return Mathf.Clamp01(_currentPressureSamples[candidateIndex].Pressure);
    }

    private void SetLineColor(LineRenderer lineRenderer, Color color)
    {
        if (lineRenderer == null)
        {
            return;
        }

        if (lineRenderer.material != null)
        {
            lineRenderer.material.color = color;
        }

        var arrowTip = lineRenderer.GetComponent<LineArrowTip>();
        if (arrowTip != null)
        {
            arrowTip.SetColor(color);
        }
    }

    private void ApplyWidthCurve(LineRenderer lineRenderer, IReadOnlyList<float> sourceWidths, int targetCount, float widthMultiplier = 1f)
    {
        if (lineRenderer == null || sourceWidths == null || sourceWidths.Count == 0)
        {
            return;
        }

        var curve = new AnimationCurve();
        if (targetCount <= 1 || sourceWidths.Count == 1)
        {
            var width = sourceWidths[0] * widthMultiplier;
            curve.AddKey(0f, width);
            curve.AddKey(1f, width);
            lineRenderer.widthCurve = curve;
            lineRenderer.startWidth = width;
            lineRenderer.endWidth = width;
            return;
        }

        for (var i = 0; i < targetCount; i++)
        {
            var t = i / (float)(targetCount - 1);
            curve.AddKey(t, SampleWidth(sourceWidths, t) * widthMultiplier);
        }

        lineRenderer.widthCurve = curve;
        lineRenderer.startWidth = sourceWidths[0] * widthMultiplier;
        lineRenderer.endWidth = sourceWidths[sourceWidths.Count - 1] * widthMultiplier;
    }

    private void ApplyFixedWidthCurve(LineRenderer lineRenderer, float width)
    {
        if (lineRenderer == null)
        {
            return;
        }

        width = Mathf.Max(width, _minLineWidth);
        var curve = new AnimationCurve();
        curve.AddKey(0f, width);
        curve.AddKey(1f, width);
        lineRenderer.widthCurve = curve;
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
    }

    private float SampleWidth(IReadOnlyList<float> sourceWidths, float normalizedT)
    {
        if (sourceWidths.Count == 1)
        {
            return sourceWidths[0];
        }

        var scaledIndex = normalizedT * (sourceWidths.Count - 1);
        var minIndex = Mathf.FloorToInt(scaledIndex);
        var maxIndex = Mathf.Min(minIndex + 1, sourceWidths.Count - 1);
        var blend = scaledIndex - minIndex;
        return Mathf.Lerp(sourceWidths[minIndex], sourceWidths[maxIndex], blend);
    }

    private float GetWidthMultiplier(PhysicsGestureReadoutResult readout)
    {
        return readout.ShapeName == "Flick" ? SnappedLineWidthMultiplier : 1f;
    }

    private bool UsesFixedThickStroke(PhysicsGestureReadoutResult readout)
    {
        return readout.PhysicsIntent == PhysicsIntentType.Spring
               || readout.PhysicsIntent == PhysicsIntentType.Impulse
               || readout.PhysicsIntent == PhysicsIntentType.Hinge;
    }

    private bool UsesPressureControlledSetting(PhysicsGestureReadoutResult readout)
    {
        return readout.PhysicsIntent == PhysicsIntentType.Spring
               || readout.PhysicsIntent == PhysicsIntentType.Impulse
               || readout.PhysicsIntent == PhysicsIntentType.Hinge;
    }

    private bool ShouldRenderAsClosedLoop(PhysicsGestureReadoutResult readout)
    {
        return readout.PhysicsIntent == PhysicsIntentType.Hinge;
    }

    private float GetThickPhysicsLineWidth()
    {
        return Mathf.Max(_minLineWidth, _maxLineWidth * springLineWidthMultiplier);
    }

    public void DeleteLine(GameObject lineObject)
    {
        if (lineObject == null)
        {
            return;
        }

        _lines.Remove(lineObject);
        if (_highlightedLine == lineObject)
        {
            _highlightedLine = null;
            _movingLine = false;
            EndDirectStylusPhysicsGrab();
        }

        Destroy(lineObject);
    }

    private void RemoveLastLine()
    {
        GameObject lastLine = _lines[_lines.Count - 1];
        _lines.RemoveAt(_lines.Count - 1);

        Destroy(lastLine);
    }

    private void ClearAllLines()
    {
        foreach (var line in _lines)
        {
            Destroy(line);
        }
        _lines.Clear();
        _highlightedLine = null;
        _movingLine = false;
        EndDirectStylusPhysicsGrab();
    }

    private void TriggerHaptics()
    {
        const float dampingFactor = 0.6f;
        const float duration = 0.01f;
        float middleButtonPressure = _stylusHandler.CurrentState.cluster_middle_value * dampingFactor;
        ((VrStylusHandler)_stylusHandler).TriggerHapticPulse(middleButtonPressure, duration);
    }

    void Update()
    {
        var endpointConsumesStylus = PhysicsDrawingEndpointHandle.HandleDirectStylus(_stylusHandler);
        if (endpointConsumesStylus)
        {
            SuspendDrawingForEndpointEdit();
            return;
        }

        if (MeshDrawingModeState.IsActive)
        {
            SuspendDrawingForEditMode();
            return;
        }

        float analogInput = Mathf.Max(_stylusHandler.CurrentState.tip_value, _stylusHandler.CurrentState.cluster_middle_value);
        var canDrawNow = analogInput > 0f && _stylusHandler.CanDraw();

        if (IsEditMode())
        {
            if (!canDrawNow)
            {
                SuspendDrawingForEditMode();
                return;
            }

            SwitchToDrawModeFromStylusInput();
        }

        if (canDrawNow)
        {
            if (_highlightedLine)
            {
                UnhighlightLine(_highlightedLine);
                _movingLine = false;
            }

            if (!_isDrawing)
            {
                StartNewLine();
                _isDrawing = true;
            }
            RecordPressureSample(analogInput);
            AddPoint(_stylusHandler.CurrentState.inkingPose.position, _lineWidthIsFixed ? 1.0f : analogInput);
            return;
        }
        else
        {
            if (_isDrawing)
            {
                RecordPressureSample(analogInput);
                FinalizeCurrentLine();
            }
            _isDrawing = false;
        }

        // Undo by double tapping or clicking on cluster_back button on stylus.
        // The rear button is routed to placeable/UI selection while the MX Ink ray has a target.
        if (!MXInkRayInteractorBinder.RearButtonSelectionTargetActive
            && (_stylusHandler.CurrentState.cluster_back_double_tap_value
                || _stylusHandler.CurrentState.cluster_back_value))
        {
            if (_lines.Count > 0 && !_doubleTapDetected)
            {
                _doubleTapDetected = true;
                buttonPressedTimestamp = Time.time;
                if (_highlightedLine)
                {
                    DeleteLine(_highlightedLine);
                    //haptic click when removing highlighted line
                    ((VrStylusHandler)_stylusHandler).TriggerHapticClick();
                    return;
                }
                else
                {
                    RemoveLastLine();
                    //haptic click when deleting last line
                    ((VrStylusHandler)_stylusHandler).TriggerHapticClick();
                    return;
                }
            }

            if (_lines.Count > 0 && Time.time >= (buttonPressedTimestamp + longPressDuration))
            {
                //haptic pulse when removing all lines
                ((VrStylusHandler)_stylusHandler).TriggerHapticPulse(1.0f, 0.1f);
                ClearAllLines();
                return;
            }
        }
        else
        {
            _doubleTapDetected = false;
        }

        if (_directStylusPhysicsGrab)
        {
            if (_stylusHandler.CurrentState.cluster_front_value)
            {
                UpdateDirectStylusPhysicsGrab();
            }
            else
            {
                EndDirectStylusPhysicsGrab();
                if (_highlightedLine != null)
                {
                    UnhighlightLine(_highlightedLine);
                }

                _movingLine = false;
            }

            return;
        }

        var mxShapeGrabOwnsFrontButton =
            MXInkRayInteractorBinder.FrontButtonShapeGrabTargetActive
            || PlaceableMultiGrabCoordinator.IsSourceGrabbing(PlaceableMultiGrabCoordinator.MXInkSourceId)
            || PhysicsDrawingEndpointHandle.IsSourceRayDragging(PlaceableMultiGrabCoordinator.MXInkSourceId);
        if (mxShapeGrabOwnsFrontButton && _stylusHandler.CurrentState.cluster_front_value)
        {
            if (_highlightedLine != null)
            {
                UnhighlightLine(_highlightedLine, false);
            }

            _movingLine = false;
            return;
        }

        // Look for closest Line
        if (!_movingLine)
        {
            var closestLine = FindClosestLine(_stylusHandler.CurrentState.inkingPose.position);
            if (closestLine)
            {
                if (_highlightedLine != closestLine)
                {
                    if (_highlightedLine)
                    {
                        UnhighlightLine(_highlightedLine);
                    }
                    HighlightLine(closestLine);
                    return;
                }
            }
            else if (_highlightedLine)
            {
                UnhighlightLine(_highlightedLine);
                return;
            }
        }
        if (_stylusHandler.CurrentState.cluster_front_value && !_movingLine)
        {
            if (TryStartDirectStylusPhysicsGrab())
            {
                return;
            }

            _movingLine = true;
            StartGrabbingLine();
        }
        else if (!_stylusHandler.CurrentState.cluster_front_value && _movingLine)
        {
            if (_highlightedLine)
            {
                UnhighlightLine(_highlightedLine);
            }
            _movingLine = false;
        }
        else if (_stylusHandler.CurrentState.cluster_front_value)
        {
            MoveHighlightedLine();
        }
    }

    private GameObject FindClosestLine(Vector3 position)
    {
        GameObject closestLine = null;
        var closestDistance = float.MaxValue;

        foreach (var line in _lines)
        {
            var lineRenderer = line.GetComponent<LineRenderer>();
            for (var i = 0; i < lineRenderer.positionCount - 1; i++)
            {
                var point = FindNearestPointOnLineSegment(lineRenderer.GetPosition(i),
                    lineRenderer.GetPosition(i + 1), position);
                var distance = Vector3.Distance(point, position);

                if (!(distance < closestDistance) || !(distance < highlightThreshold)) continue;
                closestDistance = distance;
                closestLine = line;
            }
        }

        return closestLine;
    }
    private Vector3 FindNearestPointOnLineSegment(Vector3 segStart, Vector3 segEnd, Vector3 point)
    {
        var segVec = segEnd - segStart;
        var segLen = segVec.magnitude;
        var segDir = segVec.normalized;

        var pointVec = point - segStart;
        var projLen = Vector3.Dot(pointVec, segDir);
        var clampedLen = Mathf.Clamp(projLen, 0f, segLen);

        return segStart + segDir * clampedLen;
    }

    private void HighlightLine(GameObject line)
    {
        _highlightedLine = line;
        var selectable = line.GetComponent<PhysicsDrawingSelectable>();
        if (selectable != null)
        {
            selectable.SetHovered(true);
        }
        else
        {
            var lineRenderer = line.GetComponent<LineRenderer>();
            _cachedColor = lineRenderer.material.color;
            lineRenderer.material.color = highlightColor;
            var arrowTip = line.GetComponent<LineArrowTip>();
            if (arrowTip != null)
            {
                arrowTip.SetColor(highlightColor);
            }
        }

        //haptic click when highlighting a line
        ((VrStylusHandler)_stylusHandler).TriggerHapticClick();
    }

    private void UnhighlightLine(GameObject line, bool triggerHaptic = true)
    {
        var selectable = line.GetComponent<PhysicsDrawingSelectable>();
        if (selectable != null)
        {
            selectable.SetHovered(false);
        }
        else
        {
            var lineRenderer = line.GetComponent<LineRenderer>();
            lineRenderer.material.color = _cachedColor;
            var arrowTip = line.GetComponent<LineArrowTip>();
            if (arrowTip != null)
            {
                arrowTip.SetColor(_cachedColor);
            }
        }

        _highlightedLine = null;
        if (triggerHaptic)
        {
            //haptic click when unhighlighting a line
            ((VrStylusHandler)_stylusHandler).TriggerHapticClick();
        }
    }

    private bool IsEditMode()
    {
        ResolveControlModeSource();
        return _controlModeSource != null && _controlModeSource.CurrentMode == XRControlMode.Edit;
    }

    private void ResolveControlModeSource()
    {
        if (_controlModeSource != null)
        {
            return;
        }

        _controlModeSource = FindFirstObjectByType<XRContentDrawerController>(FindObjectsInactive.Include);
    }

    private void SuspendDrawingForEditMode()
    {
        if (_isDrawing)
        {
            FinalizeCurrentLine();
            _isDrawing = false;
        }

        if (_highlightedLine != null)
        {
            UnhighlightLine(_highlightedLine, false);
        }

        EndDirectStylusPhysicsGrab();
        _movingLine = false;
        _doubleTapDetected = false;
    }

    private static void SwitchToDrawModeFromStylusInput()
    {
        SandboxEditorModeState.SetSessionMode(SandboxEditorSessionMode.Draw);

        var toolbar = FindFirstObjectByType<SandboxEditorToolbarFrame>(FindObjectsInactive.Include);
        if (toolbar != null)
            toolbar.SetToolbarVisible(true);
    }

    private void SuspendDrawingForEndpointEdit()
    {
        if (_isDrawing)
        {
            FinalizeCurrentLine();
            _isDrawing = false;
        }

        if (_highlightedLine != null)
        {
            UnhighlightLine(_highlightedLine, false);
        }

        EndDirectStylusPhysicsGrab();
        _movingLine = false;
        _doubleTapDetected = false;
    }

    private bool TryStartDirectStylusPhysicsGrab()
    {
        if (_highlightedLine == null)
        {
            return false;
        }

        var selectable = _highlightedLine.GetComponent<PhysicsDrawingSelectable>();
        if (selectable == null)
        {
            return false;
        }

        var pose = _stylusHandler.CurrentState.inkingPose;
        var direction = pose.rotation * Vector3.forward;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = Vector3.forward;
        }

        direction.Normalize();
        var grabStarted = PlaceableMultiGrabCoordinator.TryBeginGrab(
            PlaceableMultiGrabCoordinator.DirectStylusSourceId,
            selectable,
            pose.position - direction * DirectStylusGrabBackoff,
            direction,
            pose.rotation,
            DirectStylusGrabBackoff,
            DirectStylusGrabBackoff,
            DirectStylusGrabMaxDistance);

        if (!grabStarted)
        {
            return false;
        }

        _directStylusPhysicsGrab = true;
        _movingLine = true;
        ((VrStylusHandler)_stylusHandler).TriggerHapticPulse(1.0f, 0.03f);
        return true;
    }

    private void UpdateDirectStylusPhysicsGrab()
    {
        var pose = _stylusHandler.CurrentState.inkingPose;
        var direction = pose.rotation * Vector3.forward;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = Vector3.forward;
        }

        direction.Normalize();
        PlaceableMultiGrabCoordinator.UpdateGrab(
            PlaceableMultiGrabCoordinator.DirectStylusSourceId,
            pose.position - direction * DirectStylusGrabBackoff,
            direction,
            pose.rotation,
            0f,
            DirectStylusGrabBackoff,
            DirectStylusGrabMaxDistance);
    }

    private void EndDirectStylusPhysicsGrab()
    {
        if (!_directStylusPhysicsGrab)
        {
            return;
        }

        PlaceableMultiGrabCoordinator.EndGrab(PlaceableMultiGrabCoordinator.DirectStylusSourceId);
        _directStylusPhysicsGrab = false;
    }

    private void StartGrabbingLine()
    {
        if (!_highlightedLine) return;
        _grabStartPosition = _stylusHandler.CurrentState.inkingPose.position;
        _grabStartRotation = _stylusHandler.CurrentState.inkingPose.rotation;

        var lineRenderer = _highlightedLine.GetComponent<LineRenderer>();
        _originalLinePositions = new Vector3[lineRenderer.positionCount];
        lineRenderer.GetPositions(_originalLinePositions);
        //haptic pulse when start grabbing a line
        ((VrStylusHandler)_stylusHandler).TriggerHapticPulse(1.0f, 0.03f);
    }

    private void MoveHighlightedLine()
    {
        if (!_highlightedLine) return;
        var rotation = _stylusHandler.CurrentState.inkingPose.rotation * Quaternion.Inverse(_grabStartRotation);
        var lineRenderer = _highlightedLine.GetComponent<LineRenderer>();
        var newPositions = new Vector3[_originalLinePositions.Length];

        for (var i = 0; i < _originalLinePositions.Length; i++)
        {
            newPositions[i] = rotation * (_originalLinePositions[i] - _grabStartPosition) + _stylusHandler.CurrentState.inkingPose.position;
        }

        lineRenderer.SetPositions(newPositions);
        var arrowTip = _highlightedLine.GetComponent<LineArrowTip>();
        if (arrowTip != null)
        {
            arrowTip.UpdateFromLine(lineRenderer, ArrowConeLength, ArrowConeRadius);
        }

        var selectable = _highlightedLine.GetComponent<PhysicsDrawingSelectable>();
        if (selectable != null)
        {
            selectable.RebuildColliders();
        }
    }

    private void UpdateArrowTipVisual(GameObject lineObject, PhysicsGestureReadoutResult readout)
    {
        var lineRenderer = lineObject.GetComponent<LineRenderer>();
        if (lineRenderer == null)
        {
            return;
        }

        var arrowTip = lineObject.GetComponent<LineArrowTip>();
        if (readout.ShapeName != "Flick")
        {
            if (arrowTip != null)
            {
                Destroy(arrowTip);
            }

            return;
        }

        if (arrowTip == null)
        {
            arrowTip = lineObject.AddComponent<LineArrowTip>();
        }

        arrowTip.EnsureInitialized(_material, _currentColor);
        arrowTip.UpdateFromLine(lineRenderer, ArrowConeLength, ArrowConeRadius);
    }
}

[DisallowMultipleComponent]
public sealed class LineArrowTip : MonoBehaviour
{
    private GameObject _coneObject;
    private GameObject _auraConeObject;
    private MeshFilter _meshFilter;
    private MeshFilter _auraMeshFilter;
    private MeshRenderer _meshRenderer;
    private MeshRenderer _auraMeshRenderer;
    private Mesh _coneMesh;
    private float _auraScaleMultiplier = 1.45f;
    private float _auraBaseOverlapFraction = 0.35f;

    public void EnsureInitialized(Material baseMaterial, Color color)
    {
        if (_coneObject == null)
        {
            _coneObject = new GameObject("ArrowTip");
            _coneObject.transform.SetParent(transform, false);
            _meshFilter = _coneObject.AddComponent<MeshFilter>();
            _meshRenderer = _coneObject.AddComponent<MeshRenderer>();
            _coneMesh = BuildConeMesh(16);
            _meshFilter.sharedMesh = _coneMesh;
        }

        if (_meshRenderer.sharedMaterial == null || _meshRenderer.sharedMaterial == baseMaterial)
        {
            _meshRenderer.material = new Material(baseMaterial);
        }

        _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _meshRenderer.receiveShadows = false;

        SetColor(color);
    }

    public void UpdateFromLine(LineRenderer lineRenderer, float coneLength, float coneRadius)
    {
        if (_coneObject == null || lineRenderer == null || lineRenderer.positionCount < 2)
        {
            return;
        }

        var tip = lineRenderer.GetPosition(1);
        var previous = lineRenderer.GetPosition(0);

        if (lineRenderer.positionCount >= 4)
        {
            tip = lineRenderer.GetPosition(1);
            previous = lineRenderer.GetPosition(0);
        }
        else
        {
            tip = lineRenderer.GetPosition(lineRenderer.positionCount - 1);
            previous = lineRenderer.GetPosition(lineRenderer.positionCount - 2);
        }

        var direction = tip - previous;
        if (direction.sqrMagnitude <= 0.000001f)
        {
            _coneObject.SetActive(false);
            if (_auraConeObject != null)
            {
                _auraConeObject.SetActive(false);
            }

            return;
        }

        _coneObject.SetActive(true);
        _coneObject.transform.position = tip;
        _coneObject.transform.rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
        _coneObject.transform.localScale = new Vector3(coneRadius * 2f, coneLength, coneRadius * 2f);
        RefreshAuraConeTransform();
    }

    public void SetColor(Color color)
    {
        if (_meshRenderer == null)
        {
            return;
        }

        _meshRenderer.material.color = color;
    }

    public void SetAuraVisible(
        bool visible,
        Material material,
        Color color,
        float scaleMultiplier,
        float baseOverlapFraction)
    {
        if (!visible)
        {
            if (_auraConeObject != null)
            {
                _auraConeObject.SetActive(false);
            }

            return;
        }

        if (material == null || _coneObject == null || !_coneObject.activeSelf)
        {
            return;
        }

        EnsureAuraCone(material);
        _auraScaleMultiplier = Mathf.Max(1f, scaleMultiplier);
        _auraBaseOverlapFraction = Mathf.Max(0f, baseOverlapFraction);
        if (_auraMeshRenderer != null)
        {
            _auraMeshRenderer.sharedMaterial = material;
            _auraMeshRenderer.sortingLayerID = _meshRenderer != null ? _meshRenderer.sortingLayerID : 0;
            _auraMeshRenderer.sortingOrder = (_meshRenderer != null ? _meshRenderer.sortingOrder : 0) - 1;
            _auraMeshRenderer.sharedMaterial.color = color;
            if (_auraMeshRenderer.sharedMaterial.HasProperty("_Color"))
            {
                _auraMeshRenderer.sharedMaterial.SetColor("_Color", color);
            }

            var mainRenderQueue = _meshRenderer != null
                                  && _meshRenderer.sharedMaterial != null
                                  && _meshRenderer.sharedMaterial.renderQueue >= 0
                ? _meshRenderer.sharedMaterial.renderQueue
                : 2000;
            _auraMeshRenderer.sharedMaterial.renderQueue =
                Mathf.Min(_auraMeshRenderer.sharedMaterial.renderQueue, mainRenderQueue - 1);
        }

        RefreshAuraConeTransform();
        _auraConeObject.SetActive(true);
    }

    public bool TryGetAuraBasePosition(float baseOverlapFraction, out Vector3 position)
    {
        position = Vector3.zero;
        if (_coneObject == null || !_coneObject.activeSelf)
        {
            return false;
        }

        var overlapDistance = _coneObject.transform.localScale.y * Mathf.Max(0f, baseOverlapFraction);
        position = _coneObject.transform.position - _coneObject.transform.up * overlapDistance;
        return true;
    }

    private void EnsureAuraCone(Material material)
    {
        if (_auraConeObject != null)
        {
            return;
        }

        if (_coneMesh == null)
        {
            _coneMesh = BuildConeMesh(16);
        }

        _auraConeObject = new GameObject("ArrowTipAura");
        _auraConeObject.transform.SetParent(transform, false);
        _auraMeshFilter = _auraConeObject.AddComponent<MeshFilter>();
        _auraMeshRenderer = _auraConeObject.AddComponent<MeshRenderer>();
        _auraMeshFilter.sharedMesh = _coneMesh;
        _auraMeshRenderer.sharedMaterial = material;
        _auraMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _auraMeshRenderer.receiveShadows = false;
        _auraConeObject.SetActive(false);
    }

    private void RefreshAuraConeTransform()
    {
        if (_auraConeObject == null || _coneObject == null)
        {
            return;
        }

        var overlapDistance = _coneObject.transform.localScale.y * _auraBaseOverlapFraction;
        _auraConeObject.transform.position = _coneObject.transform.position - _coneObject.transform.up * overlapDistance;
        _auraConeObject.transform.rotation = _coneObject.transform.rotation;
        var coneScale = _coneObject.transform.localScale;
        _auraConeObject.transform.localScale = new Vector3(
            coneScale.x * _auraScaleMultiplier,
            coneScale.y * (_auraScaleMultiplier + _auraBaseOverlapFraction),
            coneScale.z * _auraScaleMultiplier);
    }

    private void OnDestroy()
    {
        if (_coneObject != null)
        {
            Destroy(_coneObject);
        }

        if (_auraConeObject != null)
        {
            Destroy(_auraConeObject);
        }

        if (_meshRenderer != null && _meshRenderer.material != null)
        {
            Destroy(_meshRenderer.material);
        }

        if (_coneMesh != null)
        {
            Destroy(_coneMesh);
        }
    }

    private Mesh BuildConeMesh(int sides)
    {
        var mesh = new Mesh
        {
            name = "ArrowTipCone"
        };

        var vertices = new List<Vector3> { new Vector3(0f, 1f, 0f) };
        var triangles = new List<int>();

        for (var i = 0; i < sides; i++)
        {
            var angle = (Mathf.PI * 2f * i) / sides;
            vertices.Add(new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
        }

        vertices.Add(Vector3.zero);
        var baseCenterIndex = vertices.Count - 1;

        for (var i = 0; i < sides; i++)
        {
            var current = i + 1;
            var next = ((i + 1) % sides) + 1;

            triangles.Add(0);
            triangles.Add(current);
            triangles.Add(next);

            triangles.Add(baseCenterIndex);
            triangles.Add(next);
            triangles.Add(current);
        }

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
