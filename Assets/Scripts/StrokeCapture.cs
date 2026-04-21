using System.Collections.Generic;
using UnityEngine;

public sealed class StrokeCapture
{
    private readonly List<Vector3> _rawPoints = new();
    private readonly List<Vector3> _smoothed = new();
    private readonly List<Vector3> _resampled = new();

    public IReadOnlyList<Vector3> RawPoints => _rawPoints;
    public int RawPointCount => _rawPoints.Count;

    public void Begin(Vector3 start)
    {
        _rawPoints.Clear();
        _rawPoints.Add(start);
    }

    public bool Append(Vector3 point, MeshSketchSettings settings)
    {
        settings?.Clamp();
        var spacing = settings != null ? settings.pointSampleSpacing : 0.01f;
        if (_rawPoints.Count > 0
            && Vector3.Distance(_rawPoints[_rawPoints.Count - 1], point) < spacing * 0.5f)
        {
            return false;
        }

        _rawPoints.Add(point);
        return true;
    }

    public List<Vector3> BuildProcessed(MeshSketchSettings settings)
    {
        _resampled.Clear();
        if (_rawPoints.Count == 0)
        {
            return _resampled;
        }

        settings?.Clamp();
        var spacing = settings != null ? settings.pointSampleSpacing : 0.01f;
        var smoothing = settings != null ? settings.smoothingStrength : 0f;

        Smooth(smoothing);
        Resample(spacing);
        return _resampled;
    }

    public void Clear()
    {
        _rawPoints.Clear();
        _smoothed.Clear();
        _resampled.Clear();
    }

    private void Smooth(float smoothing)
    {
        _smoothed.Clear();
        if (_rawPoints.Count <= 2 || smoothing <= 0f)
        {
            _smoothed.AddRange(_rawPoints);
            return;
        }

        _smoothed.Add(_rawPoints[0]);
        for (var i = 1; i < _rawPoints.Count - 1; i++)
        {
            var neighborAverage = (_rawPoints[i - 1] + _rawPoints[i] + _rawPoints[i + 1]) / 3f;
            _smoothed.Add(Vector3.Lerp(_rawPoints[i], neighborAverage, smoothing));
        }

        _smoothed.Add(_rawPoints[_rawPoints.Count - 1]);
    }

    private void Resample(float spacing)
    {
        spacing = Mathf.Max(0.0001f, spacing);
        _resampled.Clear();
        if (_smoothed.Count == 0)
        {
            return;
        }

        _resampled.Add(_smoothed[0]);
        var carry = 0f;
        var previous = _smoothed[0];

        for (var i = 1; i < _smoothed.Count; i++)
        {
            var current = _smoothed[i];
            var segment = current - previous;
            var segmentLength = segment.magnitude;
            if (segmentLength <= 0.000001f)
            {
                previous = current;
                continue;
            }

            var direction = segment / segmentLength;
            var distance = spacing - carry;
            while (distance <= segmentLength)
            {
                var sample = previous + direction * distance;
                if (Vector3.Distance(_resampled[_resampled.Count - 1], sample) > 0.000001f)
                {
                    _resampled.Add(sample);
                }

                distance += spacing;
            }

            carry = segmentLength - (distance - spacing);
            if (carry < 0f || carry >= spacing)
            {
                carry = 0f;
            }

            previous = current;
        }

        var last = _smoothed[_smoothed.Count - 1];
        if (Vector3.Distance(_resampled[_resampled.Count - 1], last) > spacing * 0.25f)
        {
            _resampled.Add(last);
        }
    }
}
