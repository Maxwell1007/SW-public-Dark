using Content.Shared.Imperial.Medieval.Calendar;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.Calendar;

[RegisterComponent, Access(typeof(CalendarSystem))]
public sealed partial class CalendarStateComponent : Component
{
    [DataField]
    public TimeSpan CycleDuration = TimeSpan.FromMinutes(24);

    [DataField]
    public TimeSpan DayDuration = TimeSpan.FromMinutes(12);

    [DataField]
    public int DaysCount = 12;

    [DataField]
    public int DayStageNumber = 1;

    [DataField]
    public int NightStageNumber = 6;

    [ViewVariables]
    public int CurrentCycle;

    [ViewVariables]
    public CalendarDayType CurrentDayType;

    [ViewVariables]
    public TimeSpan NextTransition;

    [ViewVariables]
    public bool UsesExternalClock;

    [ViewVariables]
    public bool Initialized;

    [ViewVariables]
    public List<ProtoId<CalendarEventPrototype>> DayDeck = new();

    [ViewVariables]
    public List<ProtoId<CalendarEventPrototype>> NightDeck = new();
}
