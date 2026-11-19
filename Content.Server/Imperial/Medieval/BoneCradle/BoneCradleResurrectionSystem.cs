using Content.Server.Chat.Managers;
using Content.Server.Ghost;
using Content.Server.Imperial.Medieval.SkeletonInvasion;
using Content.Server.Mind;
using Content.Server.Popups;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Server.Imperial.Medieval.BoneCradle;

public sealed class BoneCradleResurrectionSystem : EntitySystem
{
    [Dependency] private readonly BoneCradleTeleportSystem _teleport = default!;
    [Dependency] private readonly SkeletonProgressionSystem _progression = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly GhostSystem _ghosts = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly ISharedPlayerManager _players = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BoneCradleResurrectionComponent, MapInitEvent>(OnMapInit,
            after: [typeof(BoneCradleTeleportSystem)]);
    }

    private void OnMapInit(Entity<BoneCradleResurrectionComponent> ent, ref MapInitEvent args)
    {
        if (TerminatingOrDeleted(ent.Owner) || EntityManager.IsQueuedForDeletion(ent.Owner) ||
            !HasComp<BoneCradleComponent>(ent.Owner) ||
            !TryComp<BoneCradleTeleportComponent>(ent.Owner, out var teleport))
            return;

        foreach (var mind in _progression.GetDeadAntagonistMinds())
        {
            if (!_teleport.TryFindDestination((ent.Owner, teleport), null, out var destination))
            {
                Log.Error($"Cannot resurrect {ToPrettyString(mind.Owner)} at {ToPrettyString(ent.Owner)}: no clear destination found.");
                continue;
            }

            var body = Spawn(ent.Comp.SkeletonPrototype, destination);
            _transform.AttachToGridOrMap(body);
            if (!_progression.TryResetUpgrades(mind))
            {
                QueueDel(body);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(mind.Comp.CharacterName))
                _metadata.SetEntityName(body, mind.Comp.CharacterName);

            TransferMind(mind, body);
            if (mind.Comp.OwnedEntity != body)
            {
                QueueDel(body);
                continue;
            }

            _progression.DeleteSkullAfterRevival(mind, body);
            mind.Comp.TimeOfDeath = null;
            NotifyGhost(mind, ent.Comp);
        }
    }

    private void TransferMind(Entity<MindComponent> mind, EntityUid body)
    {
        if (mind.Comp.CurrentEntity is not { } current || TerminatingOrDeleted(current) ||
            !TryComp<GhostComponent>(current, out var ghost))
        {
            _mind.TransferTo(mind.Owner, body, mind: mind.Comp);
            return;
        }

        if (mind.Comp.OwnedEntity != current)
        {
            _ghosts.SetCanReturnToBody((current, ghost), true);
            _mind.TransferTo(mind.Owner, body, mind: mind.Comp);
            return;
        }

        var position = Transform(current).MapUid is { } map && !TerminatingOrDeleted(map)
            ? _transform.ToCoordinates(_transform.GetMapCoordinates(current))
            : Transform(body).Coordinates;
        _mind.TransferTo(mind.Owner, body, ghostCheckOverride: true, mind: mind.Comp);
        if (_ghosts.SpawnGhost((mind.Owner, mind.Comp), position, canReturn: true) == null)
            _mind.TransferTo(mind.Owner, body, mind: mind.Comp);
    }

    private void NotifyGhost(Entity<MindComponent> mind, BoneCradleResurrectionComponent component)
    {
        if (mind.Comp.UserId is not { } user || !_players.TryGetSessionById(user, out var session) ||
            session.AttachedEntity is not { } entity || !HasComp<GhostComponent>(entity))
            return;

        var message = Loc.GetString(component.Notification);
        _popup.PopupCursor(message, session, PopupType.LargeCaution);
        _chat.DispatchServerMessage(session, message);
        _audio.PlayGlobal(component.NotificationSound, session);
    }
}
