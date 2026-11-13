using Content.Shared.Actions;
using Content.Shared.Imperial.Medieval.SkeletonInvasion;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.SkeletonInvasion;

public sealed class SkeletonProgressionSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SkeletonProgressionComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SkeletonProgressionComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<SkeletonProgressionComponent, MindAddedMessage>(OnMindAdded);
        SubscribeLocalEvent<SkeletonProgressionComponent, MindRemovedMessage>(OnMindRemoved);
        SubscribeLocalEvent<SkeletonProgressionComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<SkeletonProgressionComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<SkeletonProgressionComponent, SkeletonBuyUpgradeMessage>(OnBuyUpgrade);
    }

    private void OnMapInit(Entity<SkeletonProgressionComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent.Owner, ref ent.Comp.MenuActionEntity, ent.Comp.MenuAction);
        if (_mind.TryGetMind(ent.Owner, out var mind, out _))
            AttachMind(ent, mind);
    }

    private void OnShutdown(Entity<SkeletonProgressionComponent> ent, ref ComponentShutdown args)
    {
        if (TryGetProgression(ent, out var progression))
        {
            RefundUpgrades(progression);
            progression.Body = null;
        }

        ent.Comp.Upgrades.Clear();
        _movement.RefreshMovementSpeedModifiers(ent.Owner);
        _actions.RemoveAction(ent.Owner, ent.Comp.MenuActionEntity);
        _ui.CloseUi(ent.Owner, SkeletonProgressionUiKey.Key);
    }

    private void OnMindAdded(Entity<SkeletonProgressionComponent> ent, ref MindAddedMessage args)
    {
        AttachMind(ent, args.Mind.Owner);
    }

    private void AttachMind(Entity<SkeletonProgressionComponent> ent, EntityUid mind)
    {
        if (!TryComp<SkeletonProgressionMindComponent>(mind, out var progression))
        {
            progression = AddComp<SkeletonProgressionMindComponent>(mind);
            progression.Points = Math.Max(0, ent.Comp.StartingPoints);
        }

        if (progression.Body is { } oldBody && oldBody != ent.Owner &&
            TryComp<SkeletonProgressionComponent>(oldBody, out var oldComponent))
        {
            oldComponent.Upgrades.Clear();
            RefreshBody((oldBody, oldComponent));
        }

        progression.Body = ent.Owner;
        ent.Comp.Mind = mind;
        if (_mobState.IsDead(ent.Owner))
            RefundUpgrades(progression);

        ent.Comp.Upgrades = new(progression.PurchasedUpgrades.Keys);
        RefreshBody(ent);
    }

    private void OnMindRemoved(Entity<SkeletonProgressionComponent> ent, ref MindRemovedMessage args)
    {
        ent.Comp.Upgrades.Clear();
        RefreshBody(ent);
        _ui.CloseUi(ent.Owner, SkeletonProgressionUiKey.Key);
    }

    private void OnMobStateChanged(Entity<SkeletonProgressionComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead && TryGetProgression(ent, out var progression))
        {
            RefundUpgrades(progression);
            ent.Comp.Upgrades.Clear();
            RefreshBody(ent);
        }
        else
        {
            UpdateUi(ent);
        }
    }

    private void OnUiOpened(Entity<SkeletonProgressionComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!args.UiKey.Equals(SkeletonProgressionUiKey.Key))
            return;

        if (args.Actor != ent.Owner)
        {
            _ui.CloseUi(ent.Owner, SkeletonProgressionUiKey.Key, args.Actor);
            return;
        }

        UpdateUi(ent);
    }

    private void OnBuyUpgrade(Entity<SkeletonProgressionComponent> ent, ref SkeletonBuyUpgradeMessage args)
    {
        if (args.Actor != ent.Owner || _mobState.IsDead(ent.Owner) ||
            !TryGetProgression(ent, out var progression) ||
            !ent.Comp.AvailableUpgrades.Contains(args.Upgrade) ||
            progression.PurchasedUpgrades.ContainsKey(args.Upgrade) ||
            !_prototypes.TryIndex(args.Upgrade, out var upgrade) ||
            upgrade.Cost < 0 || progression.Points < upgrade.Cost)
            return;

        foreach (var required in upgrade.Required)
        {
            if (!progression.PurchasedUpgrades.ContainsKey(required))
                return;
        }

        progression.Points -= upgrade.Cost;
        progression.PurchasedUpgrades.Add(args.Upgrade, upgrade.Cost);
        ent.Comp.Upgrades.Add(args.Upgrade);
        RefreshBody(ent);
    }

    public bool AddPoints(EntityUid skeleton, int points)
    {
        if (points <= 0 || !TryComp<SkeletonProgressionComponent>(skeleton, out var component) ||
            !TryGetProgression((skeleton, component), out var progression))
            return false;

        var total = (long) progression.Points + points;
        foreach (var cost in progression.PurchasedUpgrades.Values)
            total += cost;

        if (total > int.MaxValue)
            return false;

        progression.Points += points;
        UpdateUi((skeleton, component));
        return true;
    }

    private bool TryGetProgression(Entity<SkeletonProgressionComponent> ent, out SkeletonProgressionMindComponent progression)
    {
        if (ent.Comp.Mind is { } mind &&
            TryComp<SkeletonProgressionMindComponent>(mind, out var component) && component.Body == ent.Owner)
        {
            progression = component;
            return true;
        }

        progression = default!;
        return false;
    }

    private void RefundUpgrades(SkeletonProgressionMindComponent progression)
    {
        foreach (var cost in progression.PurchasedUpgrades.Values)
            progression.Points += cost;

        progression.PurchasedUpgrades.Clear();
    }

    private void RefreshBody(Entity<SkeletonProgressionComponent> ent)
    {
        Dirty(ent);
        _movement.RefreshMovementSpeedModifiers(ent.Owner);
        UpdateUi(ent);
    }

    private void UpdateUi(Entity<SkeletonProgressionComponent> ent)
    {
        var hasProgression = TryGetProgression(ent, out var progression);
        _ui.SetUiState(ent.Owner, SkeletonProgressionUiKey.Key, new SkeletonProgressionBuiState(
            hasProgression ? progression.Points : 0,
            new(ent.Comp.AvailableUpgrades),
            hasProgression ? new(progression.PurchasedUpgrades.Keys) : new(),
            hasProgression && !_mobState.IsDead(ent.Owner)));
    }
}
