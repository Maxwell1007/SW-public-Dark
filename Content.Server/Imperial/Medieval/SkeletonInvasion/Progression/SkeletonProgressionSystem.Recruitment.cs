using Content.Server.GameTicking;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.MagicBarrier.Components;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Imperial.Medieval.SkeletonInvasion;
using Content.Shared.Mind;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Imperial.Medieval.SkeletonInvasion;

public sealed partial class SkeletonProgressionSystem
{
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly GhostRoleSystem _ghostRoles = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private void InitializeRecruitment()
    {
        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnJoinedLobby);
        SubscribeLocalEvent<MorbiusRecruitRoleComponent, TakeGhostRoleEvent>(OnTakeRecruitRole,
            after: [typeof(GhostRoleSystem)]);
        SubscribeLocalEvent<MagicBarrierCurseSpawnComponent, MapInitEvent>(OnRecruitMarkerMapInit);
    }

    private void OnJoinedLobby(PlayerJoinedLobbyEvent args)
    {
        UnregisterFollower(args.PlayerSession.UserId);
    }

    public bool IsAntagonist(NetUserId user)
    {
        return GetRoundState() is { Resetting: false } state &&
            state.Accounts.TryGetValue(user, out var account) && account.IsAntagonist;
    }

    public bool HasSharedUpgrade(SkeletonSharedUpgradeEffect effect)
    {
        if (GetRoundState() is not { Resetting: false } state)
            return false;

        foreach (var id in state.PurchasedSharedUpgrades)
        {
            if (_prototypes.TryIndex(id, out var prototype) && prototype.SharedEffect == effect)
                return true;
        }

        return false;
    }

    private void BuySharedUpgrade(ProtoId<SkeletonUpgradePrototype> id, SkeletonUpgradePrototype upgrade)
    {
        if (_ticker.RunLevel != GameRunLevel.InRound || GetRoundState() is not { Resetting: false } state ||
            !state.AvailableSharedUpgrades.Contains(id) || state.PurchasedSharedUpgrades.Contains(id) ||
            state.SharedPoints < upgrade.Cost)
            return;

        foreach (var required in upgrade.Required)
        {
            if (!state.PurchasedSharedUpgrades.Contains(required))
                return;
        }

        switch (upgrade.SharedEffect)
        {
            case SkeletonSharedUpgradeEffect.RecruitFollower:
                CreateRecruitRole(state, 0);
                break;
            case SkeletonSharedUpgradeEffect.MindConnection:
                break;
            default:
                return;
        }

        state.SharedPoints -= upgrade.Cost;
        state.PurchasedSharedUpgrades.Add(id);
        if (upgrade.SharedEffect == SkeletonSharedUpgradeEffect.MindConnection)
        {
            foreach (var account in state.Accounts.Values)
            {
                if (account.IsAntagonist && account.Mind is { } mind)
                    RefreshMindConnection(mind);
            }
        }

        UpdateAllUis();
    }

    private void RefreshMindConnection(EntityUid mind)
    {
        if (!TerminatingOrDeleted(mind) && HasSharedUpgrade(SkeletonSharedUpgradeEffect.MindConnection) &&
            TryComp<MindComponent>(mind, out var component) &&
            TryGetAccount((mind, component), false, out _, out var account) && account.IsAntagonist)
            EnsureComp<MorbiusMindChatComponent>(mind);
    }

    public void UnregisterFollower(NetUserId user)
    {
        if (_ticker.RunLevel != GameRunLevel.InRound || GetRoundState() is not { Resetting: false } state ||
            !state.Accounts.TryGetValue(user, out var account) || !account.IsAntagonist)
            return;

        account.IsAntagonist = false;
        RefundUpgrades(account);
        var points = account.Points;
        account.Points = 0;

        if (account.Body is { } body && !TerminatingOrDeleted(body) &&
            TryComp<SkeletonProgressionComponent>(body, out var progression))
        {
            progression.Upgrades.Clear();
            RefreshBody((body, progression));
            _ui.CloseUi(body, SkeletonProgressionUiKey.Key);
        }

        account.Body = null;
        account.Skull = null;
        if (account.Mind is { } mind && !TerminatingOrDeleted(mind))
        {
            state.PendingMinds.Remove(mind);
            RemComp<MorbiusMindChatComponent>(mind);
            if (_roles.MindHasRole<MorbiusFollowerRoleComponent>(mind))
                _roles.MindRemoveRole<MorbiusFollowerRoleComponent>(mind);
        }

        account.Mind = null;
        CreateRecruitRole(state, points);
        UpdateAllUis();
    }

    private void CreateRecruitRole(SkeletonProgressionRoundComponent state, int points)
    {
        var markers = GetRecruitSpawnMarkers();
        if (markers.Count == 0)
        {
            state.PendingRecruitPoints.Enqueue(points);
            Log.Error("Cannot place a Morbius recruit role: no MagicBarrierCurseSpawn markers found. The role is waiting for a spawn marker.");
            return;
        }

        SpawnRecruitRole(state, points, _random.Pick(markers));
    }

    private void SpawnRecruitRole(SkeletonProgressionRoundComponent state, int points, EntityUid marker)
    {
        var spawner = Spawn(state.RecruitSpawnerPrototype, _transform.GetMapCoordinates(marker));
        _transform.AttachToGridOrMap(spawner);
        Comp<MorbiusRecruitRoleComponent>(spawner).Points = points;
    }

    private void OnRecruitMarkerMapInit(Entity<MagicBarrierCurseSpawnComponent> ent, ref MapInitEvent args)
    {
        if (GetRoundState() is not { Resetting: false } state || state.PendingRecruitPoints.Count == 0)
            return;

        var markers = GetRecruitSpawnMarkers();
        if (markers.Count == 0)
            return;

        while (state.PendingRecruitPoints.TryDequeue(out var points))
            SpawnRecruitRole(state, points, _random.Pick(markers));
    }

    private List<EntityUid> GetRecruitSpawnMarkers()
    {
        var markers = new List<EntityUid>();
        var query = EntityManager.AllEntityQueryEnumerator<MagicBarrierCurseSpawnComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transform))
        {
            if (!TerminatingOrDeleted(uid) && !EntityManager.IsQueuedForDeletion(uid) &&
                transform.MapUid is { } map && !TerminatingOrDeleted(map) && !EntityManager.IsQueuedForDeletion(map))
                markers.Add(uid);
        }

        return markers;
    }

    private void OnTakeRecruitRole(Entity<MorbiusRecruitRoleComponent> ent, ref TakeGhostRoleEvent args)
    {
        if (_ticker.RunLevel != GameRunLevel.InRound || args.TookRole || ent.Comp.Claimed || TerminatingOrDeleted(ent.Owner) ||
            EntityManager.IsQueuedForDeletion(ent.Owner) || MetaData(ent.Owner).EntityPaused ||
            GetRoundState() is not { Resetting: false } || IsAntagonist(args.Player.UserId) ||
            !TryComp<GhostRoleComponent>(ent.Owner, out var role) || role.Taken ||
            args.Player.AttachedEntity is not { } ghost || !HasComp<GhostComponent>(ghost))
            return;

        var transform = Transform(ent.Owner);
        if (transform.MapUid is not { } map || TerminatingOrDeleted(map) || EntityManager.IsQueuedForDeletion(map))
        {
            Log.Error($"Cannot take Morbius recruit role {ToPrettyString(ent.Owner)}: its spawn location is unavailable.");
            return;
        }

        var body = Spawn(ent.Comp.SkeletonPrototype, transform.Coordinates);
        _transform.AttachToGridOrMap(body);
        if (role.MakeSentient)
            _mind.MakeSentient(body, role.AllowMovement, role.AllowSpeech);

        ent.Comp.Claimed = true;
        _ghostRoles.GhostRoleInternalCreateMindAndTransfer(args.Player, ent.Owner, body, role);
        if (_mind.TryGetMind(body, out var mind, out var component) &&
            TryGetAccount((mind, component), false, out _, out var account))
        {
            account.Points = ent.Comp.Points;
            UpdateAllUis();
        }

        _ghostRoles.UnregisterGhostRole((ent.Owner, role));
        QueueDel(ent.Owner);
        args.TookRole = true;
    }
}
