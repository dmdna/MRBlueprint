using System;

public static class MeshDrawingModeState
{
    public static bool IsActive { get; private set; }

    public static event Action<bool> ActiveChanged;

    public static void SetActive(bool active)
    {
        if (IsActive == active)
        {
            return;
        }

        IsActive = active;
        ActiveChanged?.Invoke(IsActive);
    }

    public static void Toggle()
    {
        SetActive(!IsActive);
    }
}
