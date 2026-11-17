using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.Calendar;

[Serializable, NetSerializable]
public sealed partial class CalendarBroadcastNotificationEvent : EntityEventArgs
{
    public readonly ProtoId<CalendarEventPrototype> ProtoId;
    public readonly int DayNumber;
    public readonly CalendarDayType DayType;

    public CalendarBroadcastNotificationEvent(ProtoId<CalendarEventPrototype> protoId, int dayNumber, CalendarDayType dayType)
    {
        ProtoId = protoId;
        DayNumber = dayNumber;
        DayType = dayType;
    }
}
