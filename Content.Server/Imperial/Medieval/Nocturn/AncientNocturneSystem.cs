using System.Linq;
using Content.Server.Chat.Managers;
using Content.Server.Destructible;
using Content.Server.Polymorph.Components;
using Content.Server.Polymorph.Systems;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Imperial.Medieval.Magic;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nocturn.Components;
using Content.Shared.Polymorph;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Server.Nocturn;

public sealed class AncientNocturneSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly PolymorphSystem _polymorph = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly NocturnBloodSpellSystem _bloodSpells = default!;
    [Dependency] private readonly NocturneConversionSystem _conversion = default!;
    [Dependency] private readonly NocturnBlockedActionSystem _blockedActions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AncientNocturneComponent, AncientNocturneBatActionEvent>(OnBatAction);
        SubscribeLocalEvent<AncientNocturneComponent, AncientNocturneConversionActionEvent>(OnConversionAction);
        SubscribeLocalEvent<AncientNocturneComponent, AncientNocturneConversionDoAfterEvent>(OnConversionDoAfter);
        SubscribeLocalEvent<AncientNocturneComponent, DoAfterAttemptEvent<AncientNocturneConversionDoAfterEvent>>(OnConversionAttempt);
        SubscribeLocalEvent<PolymorphedEntityComponent, PolymorphedEvent>(OnPolymorphed);
    }

    private void OnBatAction(Entity<AncientNocturneComponent> ent, ref AncientNocturneBatActionEvent args)
    {
        if (args.Handled)
            return;

        var action = args.Action.Owner;
        var beforeCast = new MedievalBeforeCastSpellEvent(ent.Owner, Transform(ent.Owner).Coordinates);
        RaiseLocalEvent(action, ref beforeCast);
        if (beforeCast.Cancelled)
            return;

        foreach (var held in _hands.EnumerateHeld(ent.Owner).ToArray())
        {
            if (!_hands.TryDrop(ent.Owner, held, checkActionBlocker: false))
            {
                _bloodSpells.ClearReservation(ent.Owner, action);
                return;
            }
        }

        if (_polymorph.PolymorphEntity(ent.Owner, ent.Comp.BatPolymorph) is not { } bat)
        {
            _bloodSpells.ClearReservation(ent.Owner, action);
            return;
        }

        RemComp<DestructibleComponent>(bat);
        CopyHealth(ent.Owner, bat);
        RaiseLocalEvent(action, new MedievalAfterCastSpellEvent
        {
            Action = action,
            Performer = ent.Owner
        });
        args.Handled = true;
    }

    private void OnConversionAction(
        Entity<AncientNocturneComponent> ent,
        ref AncientNocturneConversionActionEvent args)
    {
        if (args.Handled)
            return;

        if (!IsValidConversionTarget(args.Target, ent.Comp))
        {
            ShowInvalidConversionTarget(ent.Owner);
            args.Handled = true;
            return;
        }

        if (_blockedActions.TryBlock(ent.Owner, args.Action.Owner))
            return;

        if (!_hands.TryGetEmptyHand(ent.Owner, out _))
        {
            _popup.PopupEntity(Loc.GetString("medieval-magic-free-hand-required"), ent.Owner, ent.Owner);
            return;
        }

        var action = args.Action.Owner;
        var beforeCast = new MedievalBeforeCastSpellEvent(ent.Owner, Transform(args.Target).Coordinates);
        RaiseLocalEvent(action, ref beforeCast);
        if (beforeCast.Cancelled)
            return;

        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            ent.Owner,
            ent.Comp.ConversionDuration,
            new AncientNocturneConversionDoAfterEvent(GetNetEntity(action)),
            ent.Owner,
            target: args.Target)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            DamageThreshold = 0,
            NeedHand = false,
            DuplicateCondition = DuplicateConditions.SameEvent,
            CancelDuplicate = true,
            BlockDuplicate = false,
            AttemptFrequency = AttemptFrequency.StartAndEnd
        };

        if (!_doAfter.TryStartDoAfter(doAfterArgs))
        {
            _bloodSpells.ClearReservation(ent.Owner, action);
            return;
        }

        _audio.PlayPvs(ent.Comp.ConversionStartSound, ent.Owner);

        _popup.PopupEntity(
            Loc.GetString(
                "medieval-ancient-nocturne-conversion-start-user",
                ("target", Identity.Name(args.Target, EntityManager, ent.Owner))),
            args.Target,
            ent.Owner,
            PopupType.Medium);

        var targetMessage = Loc.GetString("medieval-ancient-nocturne-conversion-start-target");
        _popup.PopupEntity(
            targetMessage,
            args.Target,
            args.Target,
            PopupType.LargeCaution);

        if (TryComp<ActorComponent>(args.Target, out var actor))
            _chat.DispatchServerMessage(actor.PlayerSession, targetMessage);

        args.Handled = true;
    }

    private void OnConversionAttempt(
        Entity<AncientNocturneComponent> ent,
        ref DoAfterAttemptEvent<AncientNocturneConversionDoAfterEvent> args)
    {
        if (args.Cancelled)
            return;

        if (!EntityManager.TryGetEntity(args.Event.Action, out var action) || action is not { } actionUid)
        {
            args.Cancel();
            return;
        }

        if (_blockedActions.TryBlock(ent.Owner, actionUid))
            args.Cancel();
    }

    private void OnConversionDoAfter(
        Entity<AncientNocturneComponent> ent,
        ref AncientNocturneConversionDoAfterEvent args)
    {
        if (args.Handled ||
            !EntityManager.TryGetEntity(args.Action, out var action) ||
            action is not { } actionUid)
            return;

        if (args.Cancelled || args.Target is not { } target)
        {
            _bloodSpells.ClearReservation(ent.Owner, actionUid);
            return;
        }

        args.Handled = true;
        if (!IsValidConversionTarget(target, ent.Comp))
        {
            _bloodSpells.ClearReservation(ent.Owner, actionUid);
            ShowInvalidConversionTarget(ent.Owner);
            return;
        }

        if (!_conversion.TryConvertHumanToNocturne(target, ent.Comp))
        {
            _bloodSpells.ClearReservation(ent.Owner, actionUid);
            return;
        }

        var connection = EnsureComp<AncientNocturneMindConnectionComponent>(ent.Owner);
        var trall = EnsureComp<AncientNocturneTrallMindConnectionComponent>(target);
        EnsureComp<AncientNocturneMindChatComponent>(target);
        trall.Master = ent.Owner;
        connection.Tralls.Add(target);

        SendConversionNotification(target, AncientNocturneConversionNotification.Converted);
        if (!connection.HasConvertedTrall)
        {
            connection.HasConvertedTrall = true;
            SendConversionNotification(ent.Owner, AncientNocturneConversionNotification.FirstTrall);
        }

        _popup.PopupEntity(
            Loc.GetString("medieval-ancient-nocturne-conversion-success-user"),
            target,
            ent.Owner,
            PopupType.Medium);
        _popup.PopupEntity(
            Loc.GetString("medieval-ancient-nocturne-conversion-success-target"),
            target,
            target,
            PopupType.Large);

        RaiseLocalEvent(actionUid, new MedievalAfterCastSpellEvent
        {
            Action = actionUid,
            Performer = ent.Owner
        });
    }

    private void SendConversionNotification(
        EntityUid recipient,
        AncientNocturneConversionNotification notification)
    {
        if (!TryComp<ActorComponent>(recipient, out var actor))
            return;

        RaiseNetworkEvent(new AncientNocturneConversionNotificationEvent(notification), actor.PlayerSession);
    }

    private void OnPolymorphed(Entity<PolymorphedEntityComponent> ent, ref PolymorphedEvent args)
    {
        if (!args.IsRevert ||
            !TryComp<AncientNocturneComponent>(args.NewEntity, out var ancient) ||
            !TryComp<ActionGrantComponent>(args.NewEntity, out var actionGrant))
            return;

        foreach (var actionUid in actionGrant.ActionEntities)
        {
            if (!TryComp<MetaDataComponent>(actionUid, out var metadata) ||
                metadata.EntityPrototype?.ID != ancient.BatAction.Id)
                continue;

            _actions.SetCooldown(actionUid, ancient.BatActionCooldown);
            break;
        }
    }

    private void CopyHealth(EntityUid source, EntityUid target)
    {
        if (TryComp<MobThresholdsComponent>(source, out var sourceThresholds) &&
            TryComp<MobThresholdsComponent>(target, out var targetThresholds))
        {
            foreach (var (threshold, state) in sourceThresholds.Thresholds)
            {
                _mobThreshold.SetMobStateThreshold(target, threshold, state, targetThresholds);
            }
        }

        if (TryComp<DamageableComponent>(source, out var sourceDamage) &&
            TryComp<DamageableComponent>(target, out var targetDamage))
        {
            _damageable.SetDamage(target, targetDamage, new DamageSpecifier(sourceDamage.Damage));
        }
    }

    private bool IsValidConversionTarget(EntityUid target, AncientNocturneComponent component)
    {
        return !TerminatingOrDeleted(target) &&
               TryComp<HumanoidAppearanceComponent>(target, out var appearance) &&
               appearance.Species == component.ConversionTargetSpecies;
    }

    private void ShowInvalidConversionTarget(EntityUid user)
    {
        _popup.PopupEntity(
            Loc.GetString("medieval-ancient-nocturne-conversion-invalid-target"),
            user,
            user,
            PopupType.Medium);
    }
}
