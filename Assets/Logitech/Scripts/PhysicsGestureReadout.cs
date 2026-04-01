using UnityEngine;

public class PhysicsGestureReadout : MonoBehaviour
{
    [SerializeField] private InputManager _inputManager;
    [SerializeField] private StrokeRecorder _strokeRecorder;
    [SerializeField] private GestureInterpreter _gestureInterpreter;
    [SerializeField] private bool _showOnScreenReadout = true;

    private bool _wasDrawing;
    private string _latestSummary = "Draw a stroke to classify its physics intent.";

    public string LatestSummary => _latestSummary;

    private void Update()
    {
        var isDrawing = _inputManager.IsDrawing();

        if (isDrawing && !_wasDrawing)
        {
            _strokeRecorder.BeginStroke();
        }
        else if (isDrawing)
        {
            _strokeRecorder.UpdateStroke();
        }
        else if (!isDrawing && _wasDrawing)
        {
            var stroke = _strokeRecorder.EndStroke();
            var readout = _gestureInterpreter.BuildReadout(stroke);
            _latestSummary = readout.Summary;
            Debug.Log($"[PhysicsGestureReadout] {_latestSummary}");
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
}
