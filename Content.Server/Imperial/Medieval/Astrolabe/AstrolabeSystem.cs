using System.Numerics;
using Content.Server.Chat.Managers;
using Content.Shared.DoAfter;
using Content.Shared.Imperial.Medieval.Astrolabe;
using Content.Shared.Imperial.Medieval.Ships.Islands;
using Content.Shared.Imperial.Medieval.Skills;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server.Imperial.Medieval.Astrolabe;

public sealed class AstrolabeSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AstrolabeComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<AstrolabeComponent, AstrolabeDoAfterEvent>(OnDoAfter);
    }

    private void OnUseInHand(Entity<AstrolabeComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        var intelligence = GetIntelligence(args.User);
        if (intelligence <= ent.Comp.IntelligenceMinToUse)
        {
            _popup.PopupEntity(Loc.GetString("astrolabe-low-intelligence"), args.User, args.User, PopupType.Small);
            return;
        }

        var mapId = Transform(args.User).MapID;
        if (!HasIslandOnMap(mapId))
        {
            _popup.PopupEntity(Loc.GetString("astrolabe-no-islands"), args.User, args.User, PopupType.Small);
            return;
        }

        var duration = MathF.Max(
            ent.Comp.MinimumDoAfterSeconds,
            ent.Comp.BaseDoAfterSeconds + (ent.Comp.IntelligenceMinToUse - intelligence) * ent.Comp.IntelligenceModifier);

        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            args.User,
            duration,
            new AstrolabeDoAfterEvent(),
            ent.Owner,
            target: ent.Owner,
            used: ent.Owner)
        {
            NeedHand = true,
            BreakOnHandChange = true,
            BreakOnDropItem = true,
            BreakOnMove = true,
            BreakOnDamage = true,
            DuplicateCondition = DuplicateConditions.SameTool | DuplicateConditions.SameEvent,
            CancelDuplicate = true,
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnDoAfter(Entity<AstrolabeComponent> ent, ref AstrolabeDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        if (!TryFindNearestIsland(args.User, out var distance, out var direction))
        {
            _popup.PopupEntity(Loc.GetString("astrolabe-no-islands"), args.User, args.User, PopupType.Small);
            return;
        }

        var message = Loc.GetString(
            "astrolabe-nearest-island",
            ("distance", (int) MathF.Round(distance)),
            ("direction", Loc.GetString(GetDirectionLocId(direction))));

        _popup.PopupEntity(message, args.User, args.User, PopupType.Small);

        if (TryComp<ActorComponent>(args.User, out var actor))
            _chat.DispatchServerMessage(actor.PlayerSession, message);
    }

    private int GetIntelligence(EntityUid user)
    {
        if (!TryComp<SkillsComponent>(user, out var skills))
            return 10;

        return skills.Levels.GetValueOrDefault(SharedSkillsSystem.IntelligenceId, 10);
    }

    private bool HasIslandOnMap(MapId mapId)
    {
        var query = EntityQueryEnumerator<IslandComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out _, out var transform))
        {
            if (transform.MapID == mapId)
                return true;
        }

        return false;
    }

    private bool TryFindNearestIsland(EntityUid user, out float distance, out Direction direction)
    {
        distance = 0f;
        direction = Direction.North;

        var userTransform = Transform(user);
        var userPosition = _transform.GetWorldPosition(userTransform);
        var nearestDistanceSquared = float.MaxValue;
        var nearestOffset = Vector2.Zero;
        var found = false;

        var query = EntityQueryEnumerator<IslandComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var grid, out var islandTransform))
        {
            if (islandTransform.MapID != userTransform.MapID)
                continue;

            var islandCenter = Vector2.Transform(
                grid.LocalAABB.Center,
                _transform.GetWorldMatrix(islandTransform));
            var offset = islandCenter - userPosition;
            var distanceSquared = offset.LengthSquared();

            if (distanceSquared >= nearestDistanceSquared)
                continue;

            nearestDistanceSquared = distanceSquared;
            nearestOffset = offset;
            found = true;
        }

        if (!found)
            return false;

        distance = MathF.Sqrt(nearestDistanceSquared);
        direction = nearestOffset == Vector2.Zero ? Direction.North : nearestOffset.GetDir();
        return true;
    }

    private static string GetDirectionLocId(Direction direction)
    {
        return direction switch
        {
            Direction.North => "astrolabe-direction-north",
            Direction.NorthEast => "astrolabe-direction-north-east",
            Direction.East => "astrolabe-direction-east",
            Direction.SouthEast => "astrolabe-direction-south-east",
            Direction.South => "astrolabe-direction-south",
            Direction.SouthWest => "astrolabe-direction-south-west",
            Direction.West => "astrolabe-direction-west",
            Direction.NorthWest => "astrolabe-direction-north-west",
            _ => "astrolabe-direction-north",
        };
    }
}
