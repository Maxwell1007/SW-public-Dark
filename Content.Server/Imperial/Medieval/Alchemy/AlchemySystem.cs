using System.Linq;
using Content.Server.Chemistry.EntitySystems;
using Content.Server.GameTicking.Events;
using Content.Server.Stack;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Imperial.Medieval.Alchemy;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Imperial.Medieval.GameTicking.Rules;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.EntityEffects;
using Content.Shared.Kitchen.Components;

namespace Content.Server.Imperial.Medieval.Alchemy;

public sealed partial class AlchemySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly StackSystem _stacks = default!;
    [Dependency] private readonly PuddleSystem _puddles = default!;

    private readonly Dictionary<string, AlchemyIngredient> _ingredients = new();
    private readonly List<AlchemyRecipe> _recipes = new();
    private bool _ready;
    private int _historyLimit = 1;

    public IReadOnlyList<AlchemyRecipe> Recipes
    {
        get
        {
            EnsureRound();
            return _recipes;
        }
    }

    public override void Initialize()
    {
        InitializeApparatus();
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
        SubscribeLocalEvent<AlchemyIngredientComponent, ExaminedEvent>(OnExamineIngredient);
        SubscribeLocalEvent<AlchemyToolComponent, ExaminedEvent>(OnExamineTool);
        SubscribeLocalEvent<AlchemyToolComponent, AfterInteractEvent>(OnToolInteract);
        SubscribeLocalEvent<AlchemyToolComponent, InteractUsingEvent>(OnStationInteract);
        SubscribeLocalEvent<AlchemyToolComponent, AlchemyDoAfterEvent>(OnOperationFinished);
        SubscribeLocalEvent<ReactionMixerComponent, ComponentStartup>(OnReactionMixerStartup);
        SubscribeLocalEvent<AlchemyMixerComponent, ReactionMixDoAfterEvent>(OnReactionMixFinished,
            before: new[] { typeof(ReactionMixerSystem) });
        SubscribeLocalEvent<AlchemySolutionComponent, SolutionChangedEvent>(OnSolutionChanged);
        SubscribeLocalEvent<AlchemyVesselComponent, SolutionContainerChangedEvent>(OnVesselChanged);
        SubscribeLocalEvent<MixableSolutionComponent, SolutionContainerChangedEvent>(OnMixableChanged);
        SubscribeLocalEvent<AlchemyVesselComponent, SolutionContainerOverflowEvent>(OnOverflow);
    }

    private void OnRoundStarting(RoundStartingEvent args) => EnsureRound();

    private void OnRoundCleanup(RoundRestartCleanupEvent args)
    {
        _ready = false;
        _recipes.Clear();
        _ingredients.Clear();
        _historyLimit = 1;
    }

    private void EnsureRound()
    {
        if (_ready)
            return;
        var random = new System.Random(_random.Next());
        var operations = _prototypes.EnumeratePrototypes<AlchemyOperationPrototype>().ToDictionary(o => o.ID);
        foreach (var operation in operations.Values)
        {
            if (operation.Complexity <= 0 || operation.Duration < 0 || !float.IsFinite(operation.Duration) ||
                operation.Temperature is { } temperature && (!float.IsFinite(temperature) || temperature < 0))
                throw new InvalidOperationException($"Invalid alchemy operation {operation.ID}.");
        }
        foreach (var proto in _prototypes.EnumeratePrototypes<AlchemyIngredientPrototype>().OrderBy(p => p.ID, StringComparer.Ordinal))
        {
            var ingredient = AlchemyGenerationSystem.GenerateIngredient(proto, random);
            _prototypes.Index<ReagentPrototype>(ingredient.Solvent);
            foreach (var aspect in ingredient.Aspects.Keys)
                _prototypes.Index<ReagentPrototype>(aspect);
            _ingredients.Add(proto.ID, ingredient);
        }
        foreach (var proto in _prototypes.EnumeratePrototypes<AlchemyRecipePrototype>()
                     .Where(p => !p.Abstract)
                     .OrderBy(p => p.Randomized).ThenBy(p => p.ID, StringComparer.Ordinal))
        {
            AlchemyRecipe? recipe = null;
            for (var attempt = 0; attempt < (proto.Randomized ? 512 : 1); attempt++)
            {
                var candidate = AlchemyGenerationSystem.GenerateRecipe(proto, operations, random);
                if (_recipes.Any(r => AlchemyGenerationSystem.Conflicts(r, candidate)))
                    continue;
                recipe = candidate;
                break;
            }
            if (recipe == null)
                throw new InvalidOperationException($"Cannot generate a unique alchemy recipe for {proto.ID}.");
            foreach (var reagent in recipe.Ingredients.Keys.Concat(recipe.Products.Keys))
                _prototypes.Index<ReagentPrototype>(reagent);
            foreach (var entity in recipe.Entities.Keys)
                _prototypes.Index<EntityPrototype>(entity);
            _recipes.Add(recipe);
            _historyLimit = Math.Max(_historyLimit, recipe.Steps.Count);
        }
        _recipes.Sort((a, b) => a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority) : string.CompareOrdinal(a.Id, b.Id));
        var requiredAspects = _recipes.SelectMany(r => r.Ingredients.Keys)
            .Where(r => r.StartsWith("Alchemy", StringComparison.Ordinal)).ToHashSet();
        var profiles = _prototypes.EnumeratePrototypes<AlchemyIngredientPrototype>().OrderBy(p => p.ID, StringComparer.Ordinal).ToList();
        for (var attempt = 0; !requiredAspects.IsSubsetOf(_ingredients.Values.SelectMany(i => i.Aspects.Keys)); attempt++)
        {
            if (attempt >= 512)
                throw new InvalidOperationException("Alchemy ingredients cannot supply the generated recipes.");
            foreach (var profile in profiles)
                _ingredients[profile.ID] = AlchemyGenerationSystem.GenerateIngredient(profile, random);
        }
        _ready = true;
    }

    private void OnExamineIngredient(EntityUid uid, AlchemyIngredientComponent comp, ExaminedEvent args)
    {
        EnsureRound();
        if (!_ingredients.TryGetValue(comp.Profile, out var ingredient))
            return;
        var aspects = string.Join(", ", ingredient.Aspects.Select(p => $"{ReagentName(p.Key)}: {p.Value}"));
        args.PushText(Loc.GetString("alchemy-ingredient-description", ("solvent", ReagentName(ingredient.Solvent)),
            ("aspects", aspects), ("cost", ingredient.Aspects.Values.Aggregate(FixedPoint2.Zero, (a, b) => a + b) * 0.25)));
    }

    private void OnExamineTool(EntityUid uid, AlchemyToolComponent comp, ExaminedEvent args)
    {
        var operation = _prototypes.Index<AlchemyOperationPrototype>(comp.Operation);
        args.PushText(Loc.GetString("alchemy-tool-description", ("operation", Loc.GetString(operation.Name)), ("duration", operation.Duration)));
    }

    public string ReagentName(string id)
    {
        var proto = _prototypes.Index<ReagentPrototype>(id);
        return string.IsNullOrWhiteSpace(proto.LocalizedName) ? id : proto.LocalizedName;
    }

    private void OnToolInteract(EntityUid uid, AlchemyToolComponent comp, AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target == null)
            return;
        args.Handled = StartOperation(uid, comp, args.Target.Value, args.User, uid);
    }

    private void OnStationInteract(EntityUid uid, AlchemyToolComponent comp, InteractUsingEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = StartOperation(uid, comp, args.Used, args.User, args.Used);
    }

    private bool StartOperation(EntityUid tool, AlchemyToolComponent comp, EntityUid vessel, EntityUid user, EntityUid used)
    {
        var operation = _prototypes.Index<AlchemyOperationPrototype>(comp.Operation);
        if (operation.Temperature != null)
            return false;
        if (!_solutions.TryGetMixableSolution(vessel, out var solution, out _) || solution == null)
            return false;
        EnsureRound();
        var vesselComponent = EnsureComp<AlchemyVesselComponent>(vessel);
        vesselComponent.Solution = solution.Value.Comp.Solution.Name ?? vesselComponent.Solution;
        var tracker = EnsureComp<AlchemySolutionComponent>(solution.Value.Owner);
        var ev = new AlchemyDoAfterEvent
        {
            Solution = GetNetEntity(solution.Value.Owner),
            Revision = tracker.Revision,
            Operation = comp.Operation,
        };
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(operation.Duration), ev, tool, vessel, used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
        return true;
    }

    private void OnSolutionChanged(EntityUid uid, AlchemySolutionComponent comp, ref SolutionChangedEvent args)
    {
        comp.Revision++;
    }

    private void OnReactionMixerStartup(EntityUid uid, ReactionMixerComponent comp, ComponentStartup args)
    {
        EnsureComp<AlchemyMixerComponent>(uid);
    }

    private void OnReactionMixFinished(EntityUid uid, AlchemyMixerComponent comp, ref ReactionMixDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target || TerminatingOrDeleted(target) ||
            !TryComp<ReactionMixerComponent>(uid, out var mixer) ||
            !mixer.ReactionTypes.Any(type => type.Id == "Stir"))
            return;

        var attempt = new MixingAttemptEvent(uid);
        RaiseLocalEvent(uid, ref attempt);
        if (attempt.Cancelled || !_solutions.TryGetMixableSolution(target, out var solution, out _) ||
            solution == null || solution.Value.Comp.Solution.Volume <= 0)
            return;

        var vessel = EnsureComp<AlchemyVesselComponent>(target);
        vessel.Solution = solution.Value.Comp.Solution.Name ?? vessel.Solution;
        if (vessel.Processing)
            return;
        vessel.Processing = true;
        try
        {
            CompleteOperation(solution.Value, "Stir", args.User);
        }
        finally
        {
            UpdateTemperatureState(vessel, solution.Value.Comp.Solution);
            vessel.Processing = false;
        }
    }

    private void OnOperationFinished(EntityUid uid, AlchemyToolComponent comp, AlchemyDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target == null || comp.Operation != args.Operation)
            return;
        args.Handled = true;
        if (!_solutions.TryGetMixableSolution(args.Target.Value, out var solution, out _) || solution == null ||
            GetNetEntity(solution.Value.Owner) != args.Solution ||
            !TryComp<AlchemySolutionComponent>(solution.Value.Owner, out var tracker) || tracker.Revision != args.Revision)
        {
            _popup.PopupEntity(Loc.GetString("alchemy-mixture-changed"), uid, args.User);
            return;
        }
        var operation = _prototypes.Index<AlchemyOperationPrototype>(comp.Operation);
        if (operation.Temperature != null)
            return;
        var vessel = CompOrNull<AlchemyVesselComponent>(args.Target.Value);
        if (vessel != null)
            vessel.Processing = true;
        try
        {
            CompleteOperation(solution.Value, comp.Operation, args.User);
        }
        finally
        {
            if (vessel != null)
            {
                UpdateTemperatureState(vessel, solution.Value.Comp.Solution);
                vessel.Processing = false;
            }
        }
        _popup.PopupEntity(Loc.GetString("alchemy-operation-complete", ("operation", Loc.GetString(operation.Name))), uid, args.User);
    }

    private void OnMixableChanged(EntityUid uid, MixableSolutionComponent comp, ref SolutionContainerChangedEvent args)
    {
        if (args.SolutionId != comp.Solution || HasComp<AlchemyVesselComponent>(uid) || args.Solution.Volume <= 0)
            return;
        var currentTemperature = args.Solution.Temperature;
        if (!_prototypes.EnumeratePrototypes<AlchemyOperationPrototype>().Any(operation =>
                operation.Temperature is { } temperature &&
                (operation.Heating ? currentTemperature >= temperature : currentTemperature <= temperature)))
            return;
        var vessel = EnsureComp<AlchemyVesselComponent>(uid);
        vessel.Solution = comp.Solution;
        OnVesselChanged(uid, vessel, ref args);
    }

    private void UpdateTemperatureState(AlchemyVesselComponent vessel, Solution solution)
    {
        vessel.Hot = solution.Volume > 0 && solution.Temperature >= _prototypes.Index(vessel.HeatOperation).Temperature;
        vessel.Cold = solution.Volume > 0 && solution.Temperature <= _prototypes.Index(vessel.CoolOperation).Temperature;
    }

    private void OnVesselChanged(EntityUid uid, AlchemyVesselComponent comp, ref SolutionContainerChangedEvent args)
    {
        if (comp.Processing || args.SolutionId != comp.Solution)
            return;
        var wasHot = comp.Hot;
        var wasCold = comp.Cold;
        UpdateTemperatureState(comp, args.Solution);
        if (args.Solution.Volume <= 0 || !_solutions.TryGetSolution(uid, comp.Solution, out var solution, out _) || solution == null)
            return;
        comp.Processing = true;
        try
        {
            Extract(uid, comp, solution.Value);
            var apparatus = CompOrNull<AlchemyApparatusComponent>(uid);
            var items = apparatus is { IsProcessing: true } ? apparatus.Items : null;
            var user = apparatus?.User is { } actor && !TerminatingOrDeleted(actor) ? apparatus.User : null;
            if (comp.Hot && !wasHot)
                CompleteOperation(solution.Value, comp.HeatOperation.Id, user, items);
            else if (comp.Cold && !wasCold)
                CompleteOperation(solution.Value, comp.CoolOperation.Id, user, items);
        }
        finally
        {
            comp.Processing = false;
        }
    }

    private void OnOverflow(EntityUid uid, AlchemyVesselComponent comp, ref SolutionContainerOverflowEvent args)
    {
        if (args.Handled)
            return;
        _puddles.TrySpillAt(Transform(uid).Coordinates, args.Overflow, out _);
        args.Handled = true;
    }

    private void Extract(EntityUid uid, AlchemyVesselComponent vessel, Entity<SolutionComponent> solution)
    {
        if (solution.Comp.Solution.Temperature < vessel.NigredoTemperature || !TryComp<StorageComponent>(uid, out var storage))
            return;
        EnsureRound();
        foreach (var item in storage.Container.ContainedEntities.ToArray())
        {
            if (TerminatingOrDeleted(item) || !TryComp<AlchemyIngredientComponent>(item, out var ingredient) ||
                !_ingredients.TryGetValue(ingredient.Profile, out var profile))
                continue;
            var output = profile.Aspects.Values.Aggregate(FixedPoint2.Zero, (a, b) => a + b);
            var cost = output * 0.25;
            var available = solution.Comp.Solution.GetTotalPrototypeQuantity(profile.Solvent);
            var count = TryComp<StackComponent>(item, out var stack) ? stack.Count : 1;
            count = Math.Min(count, available.Value / cost.Value);
            if (count <= 0)
                continue;
            RemovePrototype(solution.Comp.Solution, profile.Solvent, cost * count);
            foreach (var (aspect, amount) in profile.Aspects)
                solution.Comp.Solution.AddReagent(aspect, amount * count);
            if (stack != null)
                _stacks.SetCount(item, stack.Count - count);
            else
                QueueDel(item);
            _solutions.UpdateChemicals(solution, false);
        }
    }

    private void CompleteOperation(Entity<SolutionComponent> solution, string operation, EntityUid? user = null,
        IReadOnlyList<EntityUid>? items = null)
    {
        EnsureRound();
        AlchemyRecipeSystem.RecordOperation(solution.Comp.Solution, operation, _historyLimit);
        if (items != null)
        {
            items = items.Where(item => !TerminatingOrDeleted(item) && !EntityManager.IsQueuedForDeletion(item)).ToList();
            foreach (var item in items)
            {
                var history = EnsureComp<AlchemyItemHistoryComponent>(item).Operations;
                history.Add(operation);
                if (history.Count > _historyLimit)
                    history.RemoveRange(0, history.Count - _historyLimit);
            }
        }
        ExecuteRecipes(solution, user, items);
    }

    private void ExecuteRecipes(Entity<SolutionComponent> solution, EntityUid? user = null,
        IReadOnlyList<EntityUid>? items = null)
    {
        EnsureRound();
        foreach (var recipe in _recipes)
        {
            var entities = new Dictionary<string, int>();
            var matchingHistory = true;
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (MetaData(item).EntityPrototype is not { } proto)
                        continue;
                    var history = Comp<AlchemyItemHistoryComponent>(item).Operations;
                    if (recipe.Entities.ContainsKey(proto.ID) && !history.TakeLast(recipe.Steps.Count).SequenceEqual(recipe.Steps))
                        matchingHistory = false;
                    entities.TryGetValue(proto.ID, out var count);
                    entities[proto.ID] = count + (TryComp<StackComponent>(item, out var stack) ? stack.Count : 1);
                }
            }
            if (!matchingHistory || !AlchemyRecipeSystem.TryMatch(solution.Comp.Solution, recipe, out var consumed, out var products,
                    out var consumedEntities, entities))
                continue;
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (MetaData(item).EntityPrototype is not { } proto ||
                        !consumedEntities.TryGetValue(proto.ID, out var remaining) || remaining <= 0 ||
                        !Comp<AlchemyItemHistoryComponent>(item).Operations.TakeLast(recipe.Steps.Count).SequenceEqual(recipe.Steps))
                        continue;
                    var count = TryComp<StackComponent>(item, out var stack) ? Math.Min(stack.Count, remaining) : 1;
                    consumedEntities[proto.ID] -= count;
                    if (stack != null)
                        _stacks.SetCount(item, stack.Count - count);
                    else
                        QueueDel(item);
                }
            }
            foreach (var (reagent, amount) in consumed)
                RemovePrototype(solution.Comp.Solution, reagent, amount);
            foreach (var (reagent, amount) in products)
                solution.Comp.Solution.AddReagent(reagent, amount);
            if (user is { } actor && TryComp<AffectRoundStatsComponent>(actor, out var stats))
                stats.Potions++;
            var counters = EntityQueryEnumerator<RoundStatCounterRuleComponent>();
            while (counters.MoveNext(out var counter))
                counter.TotalPotions++;
            foreach (var (reagent, amount) in products)
            {
                if (!_prototypes.TryIndex<ReactionPrototype>(reagent, out var reaction))
                    continue;
                var scale = amount / recipe.Products[reagent];
                var effectArgs = new EntityEffectReagentArgs(solution, EntityManager, null,
                    solution.Comp.Solution, scale, null, null, 1f);
                foreach (var effect in reaction.Effects)
                {
                    if (effect.ShouldApply(effectArgs))
                        effect.Effect(effectArgs);
                }
            }
            break;
        }
        _solutions.UpdateChemicals(solution, false);
    }

    private static void RemovePrototype(Solution solution, string prototype, FixedPoint2 amount)
    {
        foreach (var entry in solution.Contents.ToArray())
        {
            if (entry.Reagent.Prototype != prototype)
                continue;
            amount -= solution.RemoveReagent(entry.Reagent, FixedPoint2.Min(amount, entry.Quantity));
            if (amount <= 0)
                return;
        }
    }
}

