using Content.Shared.Actions;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Imperial.Medieval.SkeletonInvasion;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Roles;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.SkeletonInvasion;

public sealed partial class SkeletonProgressionSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedRoleSystem _roles = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
        SubscribeLocalEvent<RoleAddedEvent>(OnRoleAdded);
        SubscribeLocalEvent<RoleRemovedEvent>(OnRoleRemoved);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<SkeletonProgressionComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SkeletonProgressionComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<SkeletonProgressionComponent, MindAddedMessage>(OnMindAdded);
        SubscribeLocalEvent<BodyPartComponent, MindAddedMessage>(OnSkullMindAdded);
        SubscribeLocalEvent<SkeletonProgressionComponent, MindRemovedMessage>(OnMindRemoved);
        SubscribeLocalEvent<SkeletonProgressionComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<SkeletonProgressionComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<SkeletonProgressionComponent, SkeletonBuyUpgradeMessage>(OnBuyUpgrade);
        InitializeRecruitment();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var state = GetRoundState();
        if (state == null || state.Resetting || state.PendingMinds.Count == 0)
            return;

        var pending = new List<EntityUid>(state.PendingMinds);
        state.PendingMinds.Clear();
        foreach (var mind in pending)
        {
            if (TerminatingOrDeleted(mind) ||
                !TryComp<MindComponent>(mind, out var component) ||
                !TryGetAccount((mind, component), false, out _, out var account) ||
                !account.IsAntagonist || _roles.MindHasRole<MorbiusFollowerRoleComponent>(mind))
                continue;

            _roles.MindAddRole(mind, state.MindRole, component);
        }
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent args)
    {
        if (GetRoundState() is not { } state)
            return;

        state.Resetting = true;
        state.Accounts.Clear();
        state.PendingMinds.Clear();
        state.PendingRecruitPoints.Clear();
        state.SharedPoints = 0;
        state.PurchasedSharedUpgrades.Clear();
    }

    private void OnRoleAdded(RoleAddedEvent args)
    {
        if (!_roles.MindHasRole<MorbiusFollowerRoleComponent>(args.MindId) ||
            !TryGetAccount((args.MindId, args.Mind), true, out _, out var account))
            return;

        account.IsAntagonist = true;
        account.Mind = args.MindId;
        RefreshMindConnection(args.MindId);
        if (args.Mind.OwnedEntity is { } skull &&
            TryComp<BodyPartComponent>(skull, out var part) && part.PartType == BodyPartType.Head)
            account.Skull = skull;

        if (args.Mind.OwnedEntity is { } body && TryComp<SkeletonProgressionComponent>(body, out var component))
            AttachMind((body, component), (args.MindId, args.Mind));
    }

    private void OnRoleRemoved(RoleRemovedEvent args)
    {
        QueueRoleRestore((args.MindId, args.Mind));
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        if (_mind.TryGetMind(args.Player, out var mind, out var component))
            QueueRoleRestore((mind, component));
    }

    private void QueueRoleRestore(Entity<MindComponent> mind)
    {
        if (!TryGetAccount(mind, false, out _, out var account) || !account.IsAntagonist ||
            GetRoundState() is not { Resetting: false } state)
            return;

        state.PendingMinds.Add(mind.Owner);
    }

    private void OnMapInit(Entity<SkeletonProgressionComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent.Owner, ref ent.Comp.MenuActionEntity, ent.Comp.MenuAction);
        if (_mind.TryGetMind(ent.Owner, out var mind, out var component))
            AttachMind(ent, (mind, component));
    }

    private void OnShutdown(Entity<SkeletonProgressionComponent> ent, ref ComponentShutdown args)
    {
        if (TryGetProgression(ent, out var account))
        {
            RefundUpgrades(account);
            account.Body = null;
        }

        ent.Comp.Upgrades.Clear();
        _movement.RefreshMovementSpeedModifiers(ent.Owner);
        _actions.RemoveAction(ent.Owner, ent.Comp.MenuActionEntity);
        _ui.CloseUi(ent.Owner, SkeletonProgressionUiKey.Key);
    }

    private void OnMindAdded(Entity<SkeletonProgressionComponent> ent, ref MindAddedMessage args)
    {
        AttachMind(ent, args.Mind);
    }

    private void OnSkullMindAdded(Entity<BodyPartComponent> ent, ref MindAddedMessage args)
    {
        if (ent.Comp.PartType == BodyPartType.Head &&
            TryGetAccount(args.Mind, false, out _, out var account) && account.IsAntagonist)
            account.Skull = ent.Owner;
    }

    private void AttachMind(Entity<SkeletonProgressionComponent> ent, Entity<MindComponent> mind)
    {
        if (!TryGetAccount(mind, true, out var user, out var account))
            return;

        if (ent.Comp.UserId != user && TryGetProgression(ent, out var previous))
        {
            RefundUpgrades(previous);
            previous.Body = null;
        }

        if (account.Body is { } oldBody && oldBody != ent.Owner)
        {
            if (TerminatingOrDeleted(oldBody) || _mobState.IsDead(oldBody))
                RefundUpgrades(account);

            if (TryComp<SkeletonProgressionComponent>(oldBody, out var oldComponent))
            {
                oldComponent.Upgrades.Clear();
                RefreshBody((oldBody, oldComponent));
                _ui.CloseUi(oldBody, SkeletonProgressionUiKey.Key);
            }
        }

        if (_roles.MindHasRole<MorbiusFollowerRoleComponent>(mind.Owner))
            account.IsAntagonist = true;

        account.Mind = mind.Owner;
        account.Body = ent.Owner;
        ent.Comp.UserId = user;
        if (account.IsAntagonist && !account.StartingPointsGranted)
        {
            account.StartingPointsGranted = true;
            if (ent.Comp.StartingPoints > 0)
                AddPoints(user, ent.Comp.StartingPoints);
        }

        if (_mobState.IsDead(ent.Owner))
            RefundUpgrades(account);

        ent.Comp.Upgrades = new(account.PurchasedUpgrades.Keys);
        QueueRoleRestore(mind);
        RefreshMindConnection(mind.Owner);
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
        if (args.NewMobState == MobState.Dead && TryGetProgression(ent, out var account))
        {
            RefundUpgrades(account);
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
            !TryGetProgression(ent, out var account) || !account.IsAntagonist ||
            !_prototypes.TryIndex(args.Upgrade, out var upgrade) ||
            upgrade.Cost < 0)
            return;

        if (upgrade.SharedEffect != SkeletonSharedUpgradeEffect.None)
        {
            BuySharedUpgrade(args.Upgrade, upgrade);
            return;
        }

        if (!ent.Comp.AvailableUpgrades.Contains(args.Upgrade) ||
            account.PurchasedUpgrades.ContainsKey(args.Upgrade) || account.Points < upgrade.Cost)
            return;

        foreach (var required in upgrade.Required)
        {
            if (!account.PurchasedUpgrades.ContainsKey(required))
                return;
        }

        account.Points -= upgrade.Cost;
        account.PurchasedUpgrades.Add(args.Upgrade, upgrade.Cost);
        ent.Comp.Upgrades.Add(args.Upgrade);
        RefreshBody(ent);
    }

    public bool IsAntagonist(EntityUid entity)
    {
        if (!_mind.TryGetMind(entity, out var mind, out var component))
            return false;

        return TryGetAccount((mind, component), false, out _, out var account)
            ? account.IsAntagonist
            : _roles.MindHasRole<MorbiusFollowerRoleComponent>(mind);
    }

    public List<Entity<MindComponent>> GetDeadAntagonistMinds()
    {
        var minds = new List<Entity<MindComponent>>();
        if (GetRoundState() is not { Resetting: false } state)
            return minds;

        foreach (var (user, account) in state.Accounts)
        {
            if (!account.IsAntagonist || !_mind.TryGetMind(user, out var mindId, out var mind))
                continue;

            var body = account.Body;
            if (mind.OwnedEntity is { } owned && !TerminatingOrDeleted(owned) &&
                !EntityManager.IsQueuedForDeletion(owned) && HasComp<MobStateComponent>(owned) &&
                !HasComp<GhostComponent>(owned) && !HasComp<BodyPartComponent>(owned) && !HasComp<OrganComponent>(owned))
                body = owned;

            if (body is { } existing && !TerminatingOrDeleted(existing) &&
                !EntityManager.IsQueuedForDeletion(existing) && !_mobState.IsDead(existing))
                continue;

            minds.Add((mindId.Value, mind));
        }

        return minds;
    }

    public bool TryResetUpgrades(Entity<MindComponent> mind)
    {
        if (!TryGetAccount(mind, false, out _, out var account) || !account.IsAntagonist)
            return false;

        RefundUpgrades(account);
        if (account.Body is { } body && !TerminatingOrDeleted(body) &&
            TryComp<SkeletonProgressionComponent>(body, out var component))
        {
            component.Upgrades.Clear();
            RefreshBody((body, component));
        }

        return true;
    }

    public void DeleteSkullAfterRevival(Entity<MindComponent> mind, EntityUid body)
    {
        if (mind.Comp.OwnedEntity != body || !TryGetAccount(mind, false, out _, out var account) ||
            !account.IsAntagonist || account.Skull is not { } skull)
            return;

        account.Skull = null;
        if (TerminatingOrDeleted(skull) || EntityManager.IsQueuedForDeletion(skull) ||
            !TryComp<BodyPartComponent>(skull, out var part) || part.PartType != BodyPartType.Head)
            return;

        QueueDel(skull);
    }

    public bool AddPoints(NetUserId user, int points)
    {
        var state = GetRoundState();
        if (points <= 0 || state == null || state.Resetting ||
            !state.Accounts.TryGetValue(user, out var account) || !account.IsAntagonist)
            return false;

        var total = (long) account.Points + points;
        foreach (var cost in account.PurchasedUpgrades.Values)
            total += cost;

        if (total > int.MaxValue || (long) state.SharedPoints + points > int.MaxValue)
            return false;

        account.Points += points;
        state.SharedPoints += points;
        UpdateAllUis();

        return true;
    }

    public bool AddPoints(EntityUid recipient, int points)
    {
        if (TryComp<MindComponent>(recipient, out var mind) ||
            _mind.TryGetMind(recipient, out _, out mind))
        {
            if ((mind.UserId ?? mind.OriginalOwnerUserId) is { } user)
                return AddPoints(user, points);
        }

        return TryComp<SkeletonProgressionComponent>(recipient, out var component) &&
            component.UserId is { } owner && AddPoints(owner, points);
    }

    public int AddPointsToAll(int points)
    {
        if (GetRoundState() is not { Resetting: false } state)
            return 0;

        var rewarded = 0;
        foreach (var user in state.Accounts.Keys)
        {
            if (AddPoints(user, points))
                rewarded++;
        }

        return rewarded;
    }

    private bool TryGetAccount(
        Entity<MindComponent> mind,
        bool create,
        out NetUserId user,
        out SkeletonProgressionAccount account)
    {
        user = default;
        account = default!;
        if ((mind.Comp.UserId ?? mind.Comp.OriginalOwnerUserId) is not { } owner ||
            GetRoundState(create) is not { Resetting: false } state)
            return false;

        user = owner;
        if (state.Accounts.TryGetValue(user, out var existing))
        {
            account = existing;
            return true;
        }

        if (!create)
            return false;

        account = new SkeletonProgressionAccount();
        state.Accounts.Add(user, account);
        return true;
    }

    private bool TryGetProgression(Entity<SkeletonProgressionComponent> ent, out SkeletonProgressionAccount account)
    {
        if (ent.Comp.UserId is { } user &&
            GetRoundState() is { Resetting: false } state &&
            state.Accounts.TryGetValue(user, out var existing) && existing.Body == ent.Owner)
        {
            account = existing;
            return true;
        }

        account = default!;
        return false;
    }

    private SkeletonProgressionRoundComponent? GetRoundState(bool create = false)
    {
        var query = EntityManager.AllEntityQueryEnumerator<SkeletonProgressionRoundComponent>();
        while (query.MoveNext(out var uid, out var state))
        {
            if (!TerminatingOrDeleted(uid))
                return state;
        }

        if (!create)
            return null;

        var round = Spawn(null, MapCoordinates.Nullspace);
        return AddComp<SkeletonProgressionRoundComponent>(round);
    }

    private void RefundUpgrades(SkeletonProgressionAccount account)
    {
        foreach (var cost in account.PurchasedUpgrades.Values)
            account.Points += cost;

        account.PurchasedUpgrades.Clear();
    }

    private void RefreshBody(Entity<SkeletonProgressionComponent> ent)
    {
        Dirty(ent);
        _movement.RefreshMovementSpeedModifiers(ent.Owner);
        UpdateUi(ent);
    }

    private void UpdateUi(Entity<SkeletonProgressionComponent> ent)
    {
        var hasAccount = TryGetProgression(ent, out var account);
        var state = GetRoundState();
        _ui.SetUiState(ent.Owner, SkeletonProgressionUiKey.Key, new SkeletonProgressionBuiState(
            hasAccount ? account.Points : 0,
            new(ent.Comp.AvailableUpgrades),
            hasAccount ? new(account.PurchasedUpgrades.Keys) : new(),
            state?.SharedPoints ?? 0,
            state == null ? new() : new(state.AvailableSharedUpgrades),
            state == null ? new() : new(state.PurchasedSharedUpgrades),
            hasAccount && account.IsAntagonist && !_mobState.IsDead(ent.Owner)));
    }

    private void UpdateAllUis()
    {
        var query = EntityQueryEnumerator<SkeletonProgressionComponent>();
        while (query.MoveNext(out var uid, out var component))
            UpdateUi((uid, component));
    }
}
