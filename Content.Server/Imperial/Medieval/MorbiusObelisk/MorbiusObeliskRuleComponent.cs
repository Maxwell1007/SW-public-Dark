using Content.Shared.Imperial.Medieval.Calendar;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.MorbiusObelisk;

[RegisterComponent, Access(typeof(MorbiusObeliskSystem))]
public sealed partial class MorbiusObeliskRuleComponent : Component
{
    [DataField]
    public EntProtoId ObeliskPrototype = "MedievalMorbiusObelisk";

    [DataField]
    public ProtoId<CalendarEventPrototype> CalendarEvent = "CalendarEventMorbiusObelisk";

    [DataField]
    public Dictionary<string, LocId> SectorLocations = new();

    [DataField]
    public LocId UnknownLocation = "bone-cradle-location-unknown";

    [DataField]
    public LocId SpawnAnnouncement = "morbius-obelisk-spawn-announcement";

    [DataField]
    public Color AnnouncementColor = Color.Green;
}
