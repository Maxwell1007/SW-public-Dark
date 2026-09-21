using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.Nocturn.Components;
using Content.Shared.Popups;

namespace Content.Server.Nocturn;

public sealed class NocturnBlockedActionSystem : EntitySystem
{
    [Dependency] private readonly RaceSystem _race = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NocturnBlockedActionComponent, ActionAttemptEvent>(OnActionAttempt);
    }

    private void OnActionAttempt(Entity<NocturnBlockedActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (args.Cancelled || !ent.Comp.CheckOnAttempt)
            return;

        args.Cancelled = TryBlock(args.User, ent.Owner, ent.Comp);
    }

    public bool TryBlock(EntityUid user, EntityUid action, NocturnBlockedActionComponent? component = null)
    {
        if (!Resolve(action, ref component) || _race.CanBite(user))
            return false;

        _actions.SetCooldown(action, component.BlockedCooldown);
        _popup.PopupEntity(Loc.GetString(component.BlockedMessage), user, user, PopupType.LargeCaution);
        return true;
    }
}
