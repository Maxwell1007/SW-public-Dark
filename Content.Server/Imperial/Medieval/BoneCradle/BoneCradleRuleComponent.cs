using Content.Shared.Imperial.Medieval.Calendar;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.BoneCradle;

[RegisterComponent, Access(typeof(BoneCradleSystem))]
public sealed partial class BoneCradleRuleComponent : Component
{
    [DataField]
    public EntProtoId CradlePrototype = "MedievalBoneCradle";

    [DataField]
    public ProtoId<CalendarEventPrototype> CalendarEvent = "CalendarEventBoneCradle";

    [DataField]
    public Dictionary<string, LocId> SectorLocations = new();

    [DataField]
    public LocId UnknownLocation = "bone-cradle-location-unknown";

    [DataField]
    public LocId SpawnAnnouncement = "bone-cradle-spawn-announcement";

    [DataField]
    public Color AnnouncementColor = Color.Green;
}
