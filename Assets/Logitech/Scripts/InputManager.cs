using UnityEngine;

public class InputManager : MonoBehaviour
{
    [SerializeField] private StylusHandler _stylusHandler;

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
        return GetPressure() > 0f && _stylusHandler.CanDraw();
    }
}
