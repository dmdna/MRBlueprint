using UnityEngine;

public class InputManager : MonoBehaviour
{
    [SerializeField] private StylusHandler _stylusHandler;
    [SerializeField] private XRContentDrawerController _controlModeSource;

    private void Awake()
    {
        ResolveControlModeSource();
    }

    public Vector3 GetStylusPosition()
    {
        return _stylusHandler.CurrentState.inkingPose.position;
    }

    public Quaternion GetStylusRotation()
    {
        return _stylusHandler.CurrentState.inkingPose.rotation;
    }

    public float GetPressure()
    {
        return Mathf.Max(_stylusHandler.CurrentState.tip_value, _stylusHandler.CurrentState.cluster_middle_value);
    }

    public bool IsDrawing()
    {
        return AllowsDrawingMode() && GetPressure() > 0f && _stylusHandler.CanDraw();
    }

    private bool AllowsDrawingMode()
    {
        ResolveControlModeSource();
        return _controlModeSource == null || _controlModeSource.CurrentMode == XRControlMode.Drawing;
    }

    private void ResolveControlModeSource()
    {
        if (_controlModeSource != null)
        {
            return;
        }

        _controlModeSource = FindFirstObjectByType<XRContentDrawerController>(FindObjectsInactive.Include);
    }
}
