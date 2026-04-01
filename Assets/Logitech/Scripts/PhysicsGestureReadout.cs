using UnityEngine;

public class PhysicsGestureReadout : MonoBehaviour
{
    [SerializeField] private InputManager _inputManager;
    [SerializeField] private StrokeRecorder _strokeRecorder;
    [SerializeField] private GestureInterpreter _gestureInterpreter;
    [SerializeField] private bool _showOnScreenReadout = true;
    [SerializeField] private bool _showLoopCloseIndicator = true;
    [SerializeField] private Color _loopCloseIndicatorColor = new Color(0.2f, 0.8f, 1f, 0.18f);
    [SerializeField] private int _loopCloseIndicatorSegments = 40;

    private bool _wasDrawing;
    private string _latestSummary = "Draw a stroke to classify its physics intent.";
    private GameObject _loopCloseIndicator;
    private LineRenderer _loopCloseIndicatorRenderer;

    public string LatestSummary => _latestSummary;

    private void Update()
    {
        var isDrawing = _inputManager.IsDrawing();

        if (isDrawing && !_wasDrawing)
        {
            _strokeRecorder.BeginStroke();
            SetLoopCloseIndicatorVisible(false);
        }
        else if (isDrawing)
        {
            _strokeRecorder.UpdateStroke();
            UpdateLoopCloseIndicator();
        }
        else if (!isDrawing && _wasDrawing)
        {
            var stroke = _strokeRecorder.EndStroke();
            var readout = _gestureInterpreter.BuildReadout(stroke);
            _latestSummary = readout.Summary;
            Debug.Log($"[PhysicsGestureReadout] {_latestSummary}");
            SetLoopCloseIndicatorVisible(false);
        }
        else
        {
            SetLoopCloseIndicatorVisible(false);
        }

        _wasDrawing = isDrawing;
    }

    private void OnGUI()
    {
        if (!_showOnScreenReadout)
        {
            return;
        }

        GUI.Box(new Rect(16f, 16f, 430f, 52f), _latestSummary);
    }

    private void UpdateLoopCloseIndicator()
    {
        if (!_showLoopCloseIndicator || _gestureInterpreter == null || _strokeRecorder == null)
        {
            SetLoopCloseIndicatorVisible(false);
            return;
        }

        var points = _strokeRecorder.CurrentPoints;
        var shouldShow = _strokeRecorder.IsRecording && _gestureInterpreter.WouldClassifyAsClosedLoop(points);
        if (!shouldShow)
        {
            SetLoopCloseIndicatorVisible(false);
            return;
        }

        EnsureLoopCloseIndicator();

        var startPoint = points[0].Position;
        _loopCloseIndicator.transform.position = startPoint;
        _loopCloseIndicator.transform.rotation = _inputManager != null ? _inputManager.GetStylusRotation() : Quaternion.identity;
        _loopCloseIndicatorRenderer.widthMultiplier = Mathf.Max(_gestureInterpreter.ClosedLoopThreshold * 0.06f, 0.001f);
        UpdateIndicatorGeometry(_gestureInterpreter.ClosedLoopThreshold);
        SetLoopCloseIndicatorVisible(true);
    }

    private void EnsureLoopCloseIndicator()
    {
        if (_loopCloseIndicator != null)
        {
            return;
        }

        _loopCloseIndicator = new GameObject("LoopCloseIndicator");
        _loopCloseIndicator.transform.SetParent(transform, false);

        _loopCloseIndicatorRenderer = _loopCloseIndicator.AddComponent<LineRenderer>();
        _loopCloseIndicatorRenderer.useWorldSpace = false;
        _loopCloseIndicatorRenderer.loop = true;
        _loopCloseIndicatorRenderer.alignment = LineAlignment.View;
        _loopCloseIndicatorRenderer.positionCount = Mathf.Max(_loopCloseIndicatorSegments, 3);
        _loopCloseIndicatorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _loopCloseIndicatorRenderer.receiveShadows = false;

        var material = new Material(Shader.Find("Sprites/Default"));
        material.color = _loopCloseIndicatorColor;
        _loopCloseIndicatorRenderer.material = material;
        _loopCloseIndicatorRenderer.startColor = _loopCloseIndicatorColor;
        _loopCloseIndicatorRenderer.endColor = _loopCloseIndicatorColor;
        _loopCloseIndicator.SetActive(false);
    }

    private void UpdateIndicatorGeometry(float radius)
    {
        if (_loopCloseIndicatorRenderer == null)
        {
            return;
        }

        var segmentCount = Mathf.Max(_loopCloseIndicatorRenderer.positionCount, 3);
        for (var i = 0; i < segmentCount; i++)
        {
            var angle = (Mathf.PI * 2f * i) / segmentCount;
            var localPoint = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
            _loopCloseIndicatorRenderer.SetPosition(i, localPoint);
        }
    }

    private void SetLoopCloseIndicatorVisible(bool visible)
    {
        if (_loopCloseIndicator == null)
        {
            return;
        }

        if (_loopCloseIndicator.activeSelf != visible)
        {
            _loopCloseIndicator.SetActive(visible);
        }
    }

    private void OnDestroy()
    {
        if (_loopCloseIndicatorRenderer != null && _loopCloseIndicatorRenderer.material != null)
        {
            Destroy(_loopCloseIndicatorRenderer.material);
        }
    }
}
