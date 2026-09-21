using Content.Server.DoAfter;
using Content.Shared.DoAfter;
using Content.Shared.Imperial.Medieval.ChargeableAnnounce;
using Content.Shared.Imperial.Medieval.CommsCharger;
using Content.Shared.Interaction;

namespace Content.Server.Imperial.Medieval.CommsCharger;

public sealed class CommsCrystalChargerSystem : EntitySystem
{
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CommsCrystalChargerComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<CommsCrystalChargerComponent, CommsCrystalChargerDoAfterEvent>(OnDoAfter);
    }

    private void OnAfterInteract(EntityUid uid, CommsCrystalChargerComponent component, AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (!TryComp<ChargeableAnnounceComponent>(target, out var crystal) || crystal.IsCharged)
            return;

        var doAfter = new DoAfterArgs(EntityManager, args.User, component.Delay,
            new CommsCrystalChargerDoAfterEvent(), uid, target: target, used: uid)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            DuplicateCondition = DuplicateConditions.SameTool,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            args.Handled = true;
    }

    private void OnDoAfter(EntityUid uid, CommsCrystalChargerComponent component, ref CommsCrystalChargerDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target)
            return;

        if (!TryComp<ChargeableAnnounceComponent>(target, out var crystal) || crystal.IsCharged)
            return;

        args.Handled = true;
        crystal.IsCharged = true;
        Dirty(target, crystal);

        _appearance.SetData(uid, CommsCrystalChargerVisuals.Spent, true);
        RemComp<CommsCrystalChargerComponent>(uid);
    }
}
