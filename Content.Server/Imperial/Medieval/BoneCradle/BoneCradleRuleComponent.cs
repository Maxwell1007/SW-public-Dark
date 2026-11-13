using Content.Shared.Imperial.Medieval.Calendar;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.BoneCradle;

[RegisterComponent, Access(typeof(BoneCradleSystem))]
public sealed partial class BoneCradleRuleComponent : Component
{
    [DataField]
    public EntProtoId CradlePrototype = "MedievalBoneCradle";

    [DataField]
    public Dictionary<ProtoId<CalendarEventPrototype>, string> Events = new();

    [DataField]
    public int FirstDay = 2;

    [DataField]
    public int DayInterval = 2;

    [DataField]
    public bool IsNight;
}
