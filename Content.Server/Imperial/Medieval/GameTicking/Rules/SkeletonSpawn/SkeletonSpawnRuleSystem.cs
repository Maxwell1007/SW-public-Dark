using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.GameTicking.Rules;
using Content.Server.Mind;
using Content.Server.Roles;
using Content.Shared.Database;
using Content.Shared.GameTicking.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server.Imperial.Medieval.GameTicking.Rules.SkeletonSpawn;

public sealed class SkeletonSpawnRuleSystem : GameRuleSystem<SkeletonSpawnRuleComponent>
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly ISharedPlayerManager _players = default!;
    [Dependency] private readonly RoleSystem _roles = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    protected override void Started(
        EntityUid uid,
        SkeletonSpawnRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (component.Performer is not { } performer ||
            TerminatingOrDeleted(performer) ||
            !_players.TryGetSessionByEntity(performer, out _) ||
            !_mind.TryGetMind(performer, out var mindId, out var mind) ||
            !_transform.TryGetMapOrGridCoordinates(performer, out var coordinates))
        {
            Log.Error($"Cannot spawn skeleton for rule {ToPrettyString(uid)}: no valid player, mind, or coordinates.");
            ForceEndSelf(uid, gameRule);
            return;
        }

        var skeleton = Spawn(component.SkeletonPrototype, coordinates.Value);
        _transform.AttachToGridOrMap(skeleton);
        _mind.TransferTo(mindId, skeleton, true, mind: mind);

        if (!_roles.MindGetAllRoleInfo((mindId, mind))
                .Any(role => role.Antagonist && role.Prototype == component.AntagPrototype.Id))
            _roles.MindAddRole(mindId, component.MindRole, mind);

        _adminLog.Add(
            LogType.Action,
            LogImpact.High,
            $"{ToPrettyString(performer):player} used {args.RuleId} and transferred to {ToPrettyString(skeleton):target} as {component.AntagPrototype}.");

        ForceEndSelf(uid, gameRule);
    }
}
