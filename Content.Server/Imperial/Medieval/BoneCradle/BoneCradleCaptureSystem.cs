using Content.Server.Imperial.Medieval.SkeletonInvasion;
using Content.Shared.Imperial.Medieval.CapturePoint;
using Content.Shared.Imperial.Medieval.CapturePoint.Components;
using Robust.Shared.Player;

namespace Content.Server.Imperial.Medieval.BoneCradle;

public sealed class BoneCradleCaptureSystem : EntitySystem
{
    [Dependency] private readonly SkeletonProgressionSystem _progression = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BoneCradleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<BoneCradleComponent, CapturePointFactionEvent>(OnGetFaction);
        SubscribeLocalEvent<CapturePointResultEvent>(OnCaptureResult);
    }

    private void OnMapInit(Entity<BoneCradleComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<CapturePointComponent>(ent.Owner, out var capture))
            return;

        capture.PointName = Name(ent.Owner);
        Dirty(ent.Owner, capture);
    }

    private void OnGetFaction(Entity<BoneCradleComponent> ent, ref CapturePointFactionEvent args)
    {
        if (!HasComp<ActorComponent>(args.Participant))
        {
            args.Faction = null;
            return;
        }

        var follower = _progression.IsAntagonist(args.Participant);
        args.Faction = follower ? ent.Comp.FollowerFaction : ent.Comp.OpponentFaction;
        args.CannotStartReason = follower ? Loc.GetString("bone-cradle-capture-followers-cannot-start") : null;
    }

    private void OnCaptureResult(CapturePointResultEvent args)
    {
        var uid = GetEntity(args.Point);
        if (!TryComp<BoneCradleComponent>(uid, out var cradle) || args.WinnerFaction != cradle.OpponentFaction)
            return;

        cradle.RewardsEnabled = false;
        QueueDel(uid);
    }
}
