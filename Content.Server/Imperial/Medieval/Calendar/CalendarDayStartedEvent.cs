using Robust.Shared.Prototypes;

namespace Content.Shared.Imperial.Medieval.Calendar;

public sealed class CalendarEventStartedEvent : EntityEventArgs
{
    public int DayNumber { get; }
    public ProtoId<CalendarEventPrototype> EventId { get; }
    public CalendarEventPrototype Prototype { get; }
    public CalendarDayType DayType { get; }

    public CalendarEventStartedEvent(int dayNumber, ProtoId<CalendarEventPrototype> eventId, CalendarEventPrototype prototype, CalendarDayType dayType)
    {
        DayNumber = dayNumber;
        EventId = eventId;
        Prototype = prototype;
        DayType = dayType;
    }
}

public sealed class CalendarDecksGeneratedEvent : EntityEventArgs
{
}

public sealed class CalendarUpdatedEvent : EntityEventArgs
{
}
