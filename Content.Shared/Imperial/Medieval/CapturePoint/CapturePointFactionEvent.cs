using Content.Shared.Imperial.Medieval.Factions.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Imperial.Medieval.CapturePoint;

[ByRefEvent]
public struct CapturePointFactionEvent
{
    public EntityUid Participant;
    public ProtoId<MedievalFactionPrototype>? Faction;
    public string? CannotStartReason;

    public CapturePointFactionEvent(EntityUid participant, ProtoId<MedievalFactionPrototype>? faction)
    {
        Participant = participant;
        Faction = faction;
        CannotStartReason = null;
    }
}
