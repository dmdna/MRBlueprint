using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class LineDrawing : MonoBehaviour
{
    private const float SnappedLineWidthMultiplier = 1.12f;
    private const float ArrowConeLength = 0.03f;
    private const float ArrowConeRadius = 0.009f;

    private List<GameObject> _lines = new List<GameObject>();
    private LineRenderer _currentLine;
    private List<float> _currentLineWidths = new List<float>(); //list to store line widths
    private readonly List<StrokePoint> _currentStrokePoints = new List<StrokePoint>();

    [SerializeField] float _maxLineWidth = 0.01f;
    [SerializeField] float _minLineWidth = 0.0005f;

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

    private void Awake()
    {
        if (_gestureInterpreter == null)
        {
            _gestureInterpreter = FindFirstObjectByType<GestureInterpreter>();
        }

        ResolveControlModeSource();
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

        _currentLine.positionCount = readout.DisplayPoints.Count;
        _currentLine.SetPositions(readout.DisplayPoints.ToArray());
        var widthMultiplier = GetWidthMultiplier(readout.ShapeName);
        ApplyWidthCurve(_currentLine, _currentLineWidths, readout.DisplayPoints.Count, widthMultiplier);
        UpdateArrowTipVisual(_currentLine.gameObject, readout);
        InitializePhysicsDrawing(_currentLine.gameObject, readout);
        Debug.Log($"[LineDrawing] Snapped stroke to {readout.ShapeName} ({readout.Gesture.Confidence:0.00})");
    }

    private void InitializePhysicsDrawing(GameObject lineObject, PhysicsGestureReadoutResult readout)
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

        selectable.Initialize(readout, highlightColor);
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

    private float GetWidthMultiplier(string shapeName)
    {
        return shapeName == "Line" || shapeName == "Flick" ? SnappedLineWidthMultiplier : 1f;
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
        if (IsSelectionMode())
        {
            SuspendDrawingForSelectionMode();
            return;
        }

        float analogInput = Mathf.Max(_stylusHandler.CurrentState.tip_value, _stylusHandler.CurrentState.cluster_middle_value);

        if (analogInput > 0 && _stylusHandler.CanDraw())
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
            AddPoint(_stylusHandler.CurrentState.inkingPose.position, _lineWidthIsFixed ? 1.0f : analogInput);
            return;
        }
        else
        {
            if (_isDrawing)
            {
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
                    _lines.Remove(_highlightedLine);
                    Destroy(_highlightedLine);
                    _highlightedLine = null;
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

        var mxShapeGrabOwnsFrontButton =
            MXInkRayInteractorBinder.FrontButtonShapeGrabTargetActive
            || PlaceableMultiGrabCoordinator.IsSourceGrabbing(PlaceableMultiGrabCoordinator.MXInkSourceId);
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

    private bool IsSelectionMode()
    {
        ResolveControlModeSource();
        return _controlModeSource != null && _controlModeSource.CurrentMode == XRControlMode.Selection;
    }

    private void ResolveControlModeSource()
    {
        if (_controlModeSource != null)
        {
            return;
        }

        _controlModeSource = FindFirstObjectByType<XRContentDrawerController>(FindObjectsInactive.Include);
    }

    private void SuspendDrawingForSelectionMode()
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

        _movingLine = false;
        _doubleTapDetected = false;
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
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Mesh _coneMesh;

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
            return;
        }

        _coneObject.SetActive(true);
        _coneObject.transform.position = tip;
        _coneObject.transform.rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
        _coneObject.transform.localScale = new Vector3(coneRadius * 2f, coneLength, coneRadius * 2f);
    }

    public void SetColor(Color color)
    {
        if (_meshRenderer == null)
        {
            return;
        }

        _meshRenderer.material.color = color;
    }

    private void OnDestroy()
    {
        if (_coneObject != null)
        {
            Destroy(_coneObject);
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
