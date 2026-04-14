using Content.Server.GameTicking.Rules;
using Content.Server.Imperial.Medieval.Ships.Sea.Generation;
using Content.Shared.GameTicking.Components;

namespace Content.Server.Imperial.Medieval.GameTicking.Rules;

public sealed class MedievalSeaGenerationRuleSystem : GameRuleSystem<MedievalSeaGenerationRuleComponent>
{
    [Dependency] private readonly SeasGenerationSystem _seasGeneration = default!;

    protected override void Started(EntityUid uid, MedievalSeaGenerationRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (component.Executed)
            return;

        var seasGenerationState = EnsureComp<SeasGenerationStateComponent>(uid);
        _seasGeneration.EnsureSeasGenerated(seasGenerationState);

        component.Executed = true;
        ForceEndSelf(uid, gameRule);
    }
}
