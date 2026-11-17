using Content.Shared.Imperial.Medieval.Calendar;

namespace Content.Server.Imperial.Medieval.DayNight;

public sealed class MedievalDayNightChangedEvent(int previousPhase, int phaseIndex) : EntityEventArgs
{
    public int PreviousPhase { get; } = previousPhase;
    public int PhaseIndex { get; } = phaseIndex;
    public int DayNumber => PhaseIndex / 2 + 1;
    public CalendarDayType DayType => PhaseIndex % 2 == 0 ? CalendarDayType.Day : CalendarDayType.Night;
}
