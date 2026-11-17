using Content.Shared.Imperial.Medieval.AreaMarker;
using Content.Shared.Imperial.Medieval.Calendar;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client.Imperial.Medieval.Calendar;

public sealed class CalendarNotificationUiController : UIController
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;

    public event Action<string, string>? BroadcastReceived;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<CalendarBroadcastNotificationEvent>(OnBroadcastNotification);
    }

    private void OnBroadcastNotification(CalendarBroadcastNotificationEvent ev, EntitySessionEventArgs args)
    {
        if (!_prototype.TryIndex(ev.ProtoId, out CalendarEventPrototype? proto))
            return;

        var announcement = Loc.GetString(
            ev.DayType == CalendarDayType.Day ? "calendar-event-day-announcement" : "calendar-event-night-announcement",
            ("day", ev.DayNumber),
            ("event", Loc.GetString(proto.Name)));
        var text = Loc.GetString("wrapped-area-marker-message", ("area", announcement), ("fontSize", 24));

        var announceEvent = new AreaMarkerAnnounceEvent(text);
        _entManager.EventBus.RaiseEvent(EventSource.Local, ref announceEvent);

        BroadcastReceived?.Invoke(proto.Texture, text);

        if (proto.Sound != null)
        {
            var audio = _entManager.System<SharedAudioSystem>();
            audio.PlayGlobal(proto.Sound, Filter.Local(), false);
        }
    }
}
