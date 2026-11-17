using Content.Server.Administration;
using Content.Server.GameTicking;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.Imperial.Medieval.GameTicking.Rules.SkeletonSpawn;

[AdminCommand(AdminFlags.Debug)]
public sealed class SpawnSkeletonRuleCommand : IConsoleCommand
{
    private const string RulePrototype = "MedievalSkeletonSpawnRule";

    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IEntitySystemManager _entitySystems = default!;

    public string Command => "spawnskeleton";
    public string Description => "Spawns a skeleton follower of Morbius and transfers the command user into it.";
    public string Help => $"Usage: {Command}";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { AttachedEntity: { } performer })
        {
            shell.WriteError("This command must be run through a client console while controlling an entity.");
            return;
        }

        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        var ticker = _entitySystems.GetEntitySystem<GameTicker>();
        var ruleUid = ticker.AddGameRule(RulePrototype);
        var rule = _entityManager.GetComponent<SkeletonSpawnRuleComponent>(ruleUid);
        rule.Performer = performer;
        ticker.StartGameRule(ruleUid);
    }
}
