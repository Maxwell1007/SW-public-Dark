using Content.Server.Imperial.Medieval.SkeletonInvasion;
using Content.Server.Popups;
using Content.Shared.Interaction;

namespace Content.Server.Imperial.Medieval.Phylactery;

public sealed class MorbiusAltarSystem : EntitySystem
{
    [Dependency] private readonly SkeletonProgressionSystem _progression = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MorbiusAltarComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnInteractUsing(Entity<MorbiusAltarComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || TerminatingOrDeleted(ent.Owner) || EntityManager.IsQueuedForDeletion(ent.Owner) ||
            TerminatingOrDeleted(args.Used) || EntityManager.IsQueuedForDeletion(args.Used) ||
            !HasComp<PhylacteryShardComponent>(args.Used))
            return;

        args.Handled = true;
        if (!_progression.IsAntagonist(args.User))
        {
            _popup.PopupEntity(Loc.GetString("morbius-altar-not-follower"), ent.Owner, args.User);
            return;
        }

        if (!_progression.AddPoints(args.User, ent.Comp.PointsPerShard))
            return;

        QueueDel(args.Used);
        _popup.PopupEntity(Loc.GetString("morbius-altar-shard-offered", ("points", ent.Comp.PointsPerShard)),
            ent.Owner, args.User);
    }
}
