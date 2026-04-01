using System.Collections.Generic;
using UnityEngine;

public class StrokeRecorder : MonoBehaviour
{
    [SerializeField] private InputManager _inputManager;
    [SerializeField] private float _minimumPointDistance = 0.0005f;

    private readonly List<StrokePoint> _points = new List<StrokePoint>();
    private bool _recording;
    private float _startTimestamp;
    private float _pressureTotal;
    private Vector3 _lastPoint;

    public void BeginStroke()
    {
        _points.Clear();
        _recording = true;
        _startTimestamp = Time.time;
        _pressureTotal = 0f;
        _lastPoint = _inputManager.GetStylusPosition();
        AppendCurrentPoint(forceAdd: true);
    }

    public void UpdateStroke()
    {
        if (!_recording)
        {
            return;
        }

        AppendCurrentPoint(forceAdd: false);
    }

    public StrokeData EndStroke()
    {
        if (_recording)
        {
            AppendCurrentPoint(forceAdd: false);
        }

        _recording = false;

        var stroke = new StrokeData
        {
            Points = new List<StrokePoint>(_points),
            Duration = Mathf.Max(Time.time - _startTimestamp, 0f),
            AveragePressure = _points.Count > 0 ? _pressureTotal / _points.Count : 0f
        };

        _points.Clear();
        return stroke;
    }

    private void AppendCurrentPoint(bool forceAdd)
    {
        var currentPosition = _inputManager.GetStylusPosition();
        if (!forceAdd && Vector3.Distance(currentPosition, _lastPoint) < _minimumPointDistance)
        {
            return;
        }

        var point = new StrokePoint
        {
            Position = currentPosition,
            Pressure = _inputManager.GetPressure(),
            Timestamp = Time.time
        };

        _points.Add(point);
        _pressureTotal += point.Pressure;
        _lastPoint = currentPosition;
    }
}
