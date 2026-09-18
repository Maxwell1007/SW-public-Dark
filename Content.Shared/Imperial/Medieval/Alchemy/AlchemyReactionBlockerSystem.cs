using System.Linq;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reaction;
using Robust.Shared.Prototypes;

namespace Content.Shared.Imperial.Medieval.Alchemy;

public sealed class AlchemyReactionBlockerSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    private readonly HashSet<string> _products = new();

    public override void Initialize()
    {
        Rebuild();
        SubscribeLocalEvent<SolutionComponent, ReactionAttemptEvent>(OnReaction);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnReload);
    }

    private void Rebuild()
    {
        _products.Clear();
        foreach (var recipe in _prototypes.EnumeratePrototypes<AlchemyRecipePrototype>().Where(p => !p.Abstract))
        {
            foreach (var product in recipe.Products.Keys)
            {
                if (product.StartsWith("medieval", StringComparison.Ordinal))
                    _products.Add(product);
            }
        }
    }

    private void OnReload(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<AlchemyRecipePrototype>())
            Rebuild();
    }

    private void OnReaction(EntityUid uid, SolutionComponent comp, ref ReactionAttemptEvent args)
    {
        if (args.Reaction.Products.Keys.Any(_products.Contains))
            args.Cancelled = true;
    }
}
