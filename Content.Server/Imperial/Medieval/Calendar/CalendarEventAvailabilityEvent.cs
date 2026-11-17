namespace Content.Shared.Imperial.Medieval.Calendar;

public sealed class CalendarEventAvailabilityEvent(CalendarEventPrototype prototype) : EntityEventArgs
{
    public CalendarEventPrototype Prototype { get; } = prototype;
    public bool Available = true;
}
