using Content.Shared.Nutrition.Events;

namespace Content.Shared.Imperial.Medieval.BlackSpire;

public sealed class SharedBlackSpireCurseSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BlackSpireCurseComponent, GetNeedsDecayModifiersEvent>(OnNeedsDecay);
    }

    private void OnNeedsDecay(Entity<BlackSpireCurseComponent> ent, ref GetNeedsDecayModifiersEvent args)
    {
        if (ent.Comp.Active)
            args.Modifier *= ent.Comp.Multiplier;
    }
}
