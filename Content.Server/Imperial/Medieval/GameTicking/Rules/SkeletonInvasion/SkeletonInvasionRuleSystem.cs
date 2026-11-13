using Content.Server.Antag;
using Content.Server.Antag.Components;
using Content.Server.Flash;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Systems;
using Content.Server.Imperial.Medieval.Boss;
using Content.Server.Imperial.Medieval.SkeletonInvasion;
using Content.Server.MagicBarrier.Components;
using Content.Shared.Chat;
using Content.Shared.GameTicking.Components;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Systems;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server.Imperial.Medieval.GameTicking.Rules;

public sealed class SkeletonInvasionRuleSystem : GameRuleSystem<SkeletonInvasionRuleComponent>
{
    [Dependency] private readonly AntagSelectionSystem _antagSelection = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly BossSystem _boss = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly FlashSystem _flash = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly AutoRoundExtendSystem _autoRoundExtend = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RulePlayerSpawningEvent>(OnPlayerSpawning,
            before: [typeof(AntagSelectionSystem)], after: [typeof(TDMRuleSystem)]);
        SubscribeLocalEvent<SkeletonInvasionRuleComponent, AntagSelectEntityEvent>(OnSelectSkeleton);
        SubscribeLocalEvent<SkullBossStandCompletedEvent>(OnSkullStandCompleted);
        SubscribeLocalEvent<BossDefeatedEvent>(OnBossDefeated);
        SubscribeLocalEvent<BossWonEvent>(OnBossWin);
    }

    protected override void Added(EntityUid uid, SkeletonInvasionRuleComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        base.Added(uid, component, gameRule, args);

        if (!_mapLoader.TryLoadMap(component.Arena, out var map, out _, new DeserializationOptions { InitializeMaps = true }))
        {
            Log.Error($"Failed to load skeleton invasion arena {component.Arena} for {ToPrettyString(uid)}.");
            return;
        }

        var query = EntityQueryEnumerator<BossComponent, TransformComponent>();
        while (query.MoveNext(out var boss, out _, out var xform))
        {
            if (xform.MapUid != map.Value.Owner)
                continue;

            component.Boss = boss;
            return;
        }

        Log.Error($"No boss found in skeleton invasion arena {component.Arena} for {ToPrettyString(uid)}.");
    }

    private void OnPlayerSpawning(RulePlayerSpawningEvent args)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out _, out var gameRule))
        {
            if (!TryComp<AntagSelectionComponent>(uid, out var selection) || selection.AssignmentComplete)
                continue;

            if (GetSpawnMarkers().Count == 0)
            {
                Log.Error($"Cannot spawn skeletons for {ToPrettyString(uid)}: no MagicBarrierCurseSpawn markers found.");
                GameTicker.EndGameRule(uid, gameRule);
                continue;
            }

            foreach (var definition in selection.Definitions)
            {
                var count = _antagSelection.GetTargetAntagCount((uid, selection), null, definition);
                var candidates = _antagSelection.GetPlayerPool((uid, selection), args.PlayerPool, definition);
                while (count > 0 && candidates.TryPickAndTake(RobustRandom, out var session))
                {
                    if (_antagSelection.TryMakeAntag((uid, selection), session, definition, onlyPreSelect: true))
                        count--;
                }
            }
        }
    }

    private void OnSelectSkeleton(Entity<SkeletonInvasionRuleComponent> ent, ref AntagSelectEntityEvent args)
    {
        if (args.Handled || args.Session == null)
            return;

        var markers = GetSpawnMarkers();
        if (markers.Count == 0)
        {
            Log.Error($"Cannot spawn skeleton for {ToPrettyString(ent)}: no MagicBarrierCurseSpawn markers found.");
            return;
        }

        args.Entity = Spawn(ent.Comp.SkeletonPrototype, Transform(RobustRandom.Pick(markers)).Coordinates);
    }

    private List<EntityUid> GetSpawnMarkers()
    {
        var markers = new List<EntityUid>();
        var query = EntityQueryEnumerator<MagicBarrierCurseSpawnComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid != null)
                markers.Add(uid);
        }

        return markers;
    }

    protected override void ActiveTick(EntityUid uid, SkeletonInvasionRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (component.EndTime == null || component.EndTime > Timing.CurTime)
            return;

        component.EndTime = null;
        GameTicker.EndRound();
        GameTicker.RestartRound();
    }

    protected override void AppendRoundEndText(EntityUid uid, SkeletonInvasionRuleComponent component, GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {
        base.AppendRoundEndText(uid, component, gameRule, ref args);

        var resultText = component.Result switch
        {
            SkeletonInvasionRoundResult.NoBoss => Loc.GetString("skeletron-roundend-no-boss"),
            SkeletonInvasionRoundResult.BossDefeated => Loc.GetString("skeletron-roundend-defeated"),
            SkeletonInvasionRoundResult.BossWon => Loc.GetString("skeletron-roundend-lost"),
            SkeletonInvasionRoundResult.SkeletonsWon => Loc.GetString("skeletron-roundend-skeletons"),
            _ => ""
        };

        args.AddLine(resultText);
    }

    protected override void Ended(EntityUid uid, SkeletonInvasionRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        component.EndTime = null;
    }

    private void OnSkullStandCompleted(SkullBossStandCompletedEvent args)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out _, out var component, out _))
        {
            if (args.PurifiedParts < args.Parts / 2)
            {
                var msg = Loc.GetString("morbius-skull-completed-announcement");
                ChatManager.ChatMessageToAll(ChatChannel.Radio, msg, msg, EntityUid.Invalid, false, true, colorOverride: Color.OrangeRed);
                _audio.PlayGlobal(new SoundPathSpecifier(new ResPath("/Audio/Imperial/Medieval/Effects/skull-announce.ogg")), Filter.Broadcast(), true);
                component.Result = SkeletonInvasionRoundResult.SkeletonsWon;
                component.EndTime = Timing.CurTime + component.VictoryRoundEndDelay;
                continue;
            }

            if (component.Boss is not { } boss || !Exists(boss))
                continue;

            var standMap = Transform(args.Stand).MapUid;
            var bossfightPlayers = new List<EntityUid>();
            var players = EntityQueryEnumerator<HumanoidAppearanceComponent, TransformComponent>();
            while (players.MoveNext(out var player, out _, out var xform))
            {
                if (HasComp<IgnoreBossStartComponent>(player) || xform.MapUid != standMap || !_mobState.IsAlive(player))
                    continue;

                bossfightPlayers.Add(player);
                _flash.Flash(player, null, null, TimeSpan.FromSeconds(5), 0.3f, false);
                _audio.PlayGlobal(new SoundPathSpecifier(new ResPath("/Audio/Imperial/Medieval/Effects/teleport.ogg")), player);
            }

            if (bossfightPlayers.Count == 0)
                continue;

            var msgBoss = "Древний череп собран. Его проклятие не сломлено, но искажено святым пламенем. Равновесие пало, и теперь судьба этого острова решится в битве!";
            ChatManager.ChatMessageToAll(ChatChannel.Radio, msgBoss, msgBoss, EntityUid.Invalid, false, true, colorOverride: Color.OrangeRed);
            _audio.PlayGlobal(new SoundPathSpecifier(new ResPath("/Audio/Imperial/Medieval/Effects/skull-announce.ogg")), Filter.Broadcast(), true);

            _boss.StartBossfight(bossfightPlayers, boss);
            component.Result = SkeletonInvasionRoundResult.BossWon;
            _autoRoundExtend.ForceExtendRound(component.BossfightRoundExtension);
        }
    }

    private void OnBossDefeated(ref BossDefeatedEvent args)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out _, out var component, out _))
        {
            if (component.Boss != args.Boss)
                continue;

            component.Result = SkeletonInvasionRoundResult.BossDefeated;
            if (TryComp<BossComponent>(args.Boss, out var bossComponent) && bossComponent.IsEndGame)
                component.EndTime = Timing.CurTime + component.DefeatRoundEndDelay;
        }
    }

    private void OnBossWin(ref BossWonEvent args)
    {
        if (!TryComp<BossComponent>(args.Boss, out var bossComponent) || !bossComponent.IsEndGame)
            return;

        var query = QueryActiveRules();
        while (query.MoveNext(out _, out var component, out _))
        {
            if (component.Boss == args.Boss)
                component.EndTime = Timing.CurTime + component.VictoryRoundEndDelay;
        }
    }
}
