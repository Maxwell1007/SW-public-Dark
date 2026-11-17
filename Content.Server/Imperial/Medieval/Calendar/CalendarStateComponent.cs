using Content.Shared.Imperial.Medieval.Calendar;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.Calendar;

[RegisterComponent, Access(typeof(CalendarSystem))]
public sealed partial class CalendarStateComponent : Component
{
    [DataField]
    public int PhasesCount = 24;

    [DataField]
    public int GenerationSearchLimit = 100000;

    [DataField]
    public ProtoId<CalendarEventPrototype> DayFallback = "DefaultCalendarEvent";

    [DataField]
    public ProtoId<CalendarEventPrototype> NightFallback = "DefaultCalendarNightEvent";

    [ViewVariables]
    public int DeckStartPhase;

    [ViewVariables]
    public bool ScheduleReady;

    [ViewVariables]
    public bool RebuildRequested;

    [ViewVariables]
    public List<ProtoId<CalendarEventPrototype>> Deck = new();

    [ViewVariables]
    public List<ProtoId<CalendarEventPrototype>> DisplayDeck = new();

    [ViewVariables]
    public List<ProtoId<CalendarEventPrototype>> DayDeck = new();

    [ViewVariables]
    public List<ProtoId<CalendarEventPrototype>> NightDeck = new();

    [ViewVariables]
    public Dictionary<string, int> LastOccurrence = new();
}
