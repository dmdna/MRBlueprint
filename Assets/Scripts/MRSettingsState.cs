using System;

[Serializable]
public struct MRSettingsState
{
    public bool PassthroughEnabled;
    public bool MRRoomEnabled;
    public bool UseRoomSetup;
    public bool BlueprintVisible;

    public static MRSettingsState RealRoomDefaults()
    {
        return new MRSettingsState
        {
            PassthroughEnabled = true,
            MRRoomEnabled = true,
            UseRoomSetup = true,
            BlueprintVisible = true
        };
    }

    public static MRSettingsState FloorOnlyFallbackDefaults()
    {
        return new MRSettingsState
        {
            PassthroughEnabled = true,
            MRRoomEnabled = false,
            UseRoomSetup = false,
            BlueprintVisible = true
        };
    }
}

public enum MRSettingsRoomMode
{
    RoomSetup,
    RandomizedRoom,
    FloorOnly
}
