using Content.Shared.Imperial.Medieval.Factions;
using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.Calendar;

[Serializable, NetSerializable]
public enum CalendarBoardUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class CalendarBoardBoundUserInterfaceState : BoundUserInterfaceState
{
    public Dictionary<int, WantedData> Wanted;
    public List<string> DayDeck;
    public List<string> NightDeck;
    public int CurrentCycle;
    public CalendarDayType CurrentDayType;
    public List<AnnouncementData> Announcements;

    public CalendarBoardBoundUserInterfaceState(
        Dictionary<int, WantedData> wanted,
        List<string> dayDeck,
        List<string> nightDeck,
        int currentCycle,
        CalendarDayType currentDayType,
        List<AnnouncementData> announcements)
    {
        Wanted = wanted;
        DayDeck = dayDeck;
        NightDeck = nightDeck;
        CurrentCycle = currentCycle;
        CurrentDayType = currentDayType;
        Announcements = announcements;
    }
}
