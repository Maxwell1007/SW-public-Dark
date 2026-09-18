using System.Linq;
using Content.Shared.FixedPoint;

namespace Content.Shared.Imperial.Medieval.Alchemy;

public sealed class AlchemyGenerationSystem : EntitySystem
{
    public static string Pick(System.Random random, Dictionary<string, int> weights)
    {
        var total = weights.Values.Where(w => w > 0).Sum();
        if (total <= 0)
            throw new InvalidOperationException("Alchemy requires a positive choice weight.");
        var choice = random.Next(total);
        foreach (var (id, weight) in weights.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (weight <= 0)
                continue;
            choice -= weight;
            if (choice < 0)
                return id;
        }
        throw new InvalidOperationException("Invalid alchemy weights.");
    }

    public static AlchemyIngredient GenerateIngredient(AlchemyIngredientPrototype proto, System.Random random)
    {
        if (proto.MinYield <= 0 || proto.MaxYield < proto.MinYield || proto.AspectCount <= 0 ||
            proto.Solvents.Count == 0 || proto.AspectWeights.Count(p => p.Value > 0) < proto.AspectCount ||
            proto.MinYield < proto.AspectCount)
            throw new InvalidOperationException($"Invalid alchemy ingredient {proto.ID}.");
        var result = new AlchemyIngredient { Solvent = proto.Solvents[random.Next(proto.Solvents.Count)] };
        var weights = new Dictionary<string, int>(proto.AspectWeights);
        var remaining = random.Next(proto.MinYield, proto.MaxYield + 1);
        for (var i = 0; i < proto.AspectCount; i++)
        {
            var aspect = Pick(random, weights);
            weights.Remove(aspect);
            var amount = i == proto.AspectCount - 1 ? remaining : random.Next(1, remaining - (proto.AspectCount - i - 1) + 1);
            result.Aspects.Add(aspect, FixedPoint2.New(amount));
            remaining -= amount;
        }
        return result;
    }

    public static AlchemyRecipe GenerateRecipe(AlchemyRecipePrototype proto,
        IReadOnlyDictionary<string, AlchemyOperationPrototype> operations, System.Random random)
    {
        if (proto.Abstract || proto.MaxComplexity < 0 || proto.ExpectedSteps < 0 || proto.ExpectedSteps > 64 ||
            proto.MinParts <= 0 || proto.MaxParts < proto.MinParts || proto.Products.Count == 0 ||
            proto.Products.Any(p => p.Value <= 0) || proto.Entities.Any(p => p.Value <= 0))
            throw new InvalidOperationException($"Invalid alchemy recipe {proto.ID}.");
        if (!proto.Randomized && (proto.Ingredients == null || proto.Steps == null))
            throw new InvalidOperationException($"Fixed alchemy recipe {proto.ID} is incomplete.");
        var result = new AlchemyRecipe
        {
            Id = proto.ID,
            Group = proto.Group,
            Products = new(proto.Products),
            Entities = new(proto.Entities),
            StrictRatio = proto.StrictRatio ?? (!proto.Randomized || random.Next(4) != 0),
            AllowImpurities = proto.AllowImpurities ?? (proto.Randomized && random.Next(4) == 0),
            Priority = proto.Priority,
        };
        var ingredients = proto.Ingredients ?? Enumerable.Range(0, proto.IngredientCount)
            .Select(_ => new AlchemyIngredientRequirement()).ToList();
        var weights = new Dictionary<string, int>(proto.AspectWeights);
        if (proto.Tier < 3)
            weights.Remove("AlchemyDarkness");
        foreach (var ingredient in ingredients)
        {
            if (ingredient.Reagent != null)
                weights.Remove(ingredient.Reagent);
        }
        var selected = new List<(string Reagent, FixedPoint2? Amount)>();
        foreach (var ingredient in ingredients)
        {
            if (!proto.Randomized && (ingredient.Reagent == null || ingredient.Amount == null))
                throw new InvalidOperationException($"Fixed alchemy recipe {proto.ID} has an incomplete ingredient.");
            var reagent = ingredient.Reagent ?? Pick(random, weights);
            weights.Remove(reagent);
            selected.Add((reagent, ingredient.Amount));
        }
        var remainingAspects = proto.TotalAspectAmount;
        var missingAspects = 0;
        if (remainingAspects is { } total)
        {
            if (total <= 0)
                throw new InvalidOperationException($"Invalid aspect total in alchemy recipe {proto.ID}.");
            foreach (var (reagent, amount) in selected)
            {
                if (!IsAspect(reagent))
                    continue;
                if (amount is { } fixedAmount)
                    remainingAspects -= fixedAmount;
                else
                    missingAspects++;
            }
            if (remainingAspects < FixedPoint2.New(proto.MinParts) * missingAspects ||
                remainingAspects > FixedPoint2.New(proto.MaxParts) * missingAspects)
                throw new InvalidOperationException($"Alchemy recipe {proto.ID} cannot satisfy its aspect total without changing manual amounts.");
        }
        foreach (var (reagent, specifiedAmount) in selected)
        {
            var amount = specifiedAmount ?? FixedPoint2.Zero;
            if (specifiedAmount == null)
            {
                if (remainingAspects is { } remaining && IsAspect(reagent))
                {
                    missingAspects--;
                    var minimum = FixedPoint2.Max(FixedPoint2.New(proto.MinParts), remaining - FixedPoint2.New(proto.MaxParts) * missingAspects);
                    var maximum = FixedPoint2.Min(FixedPoint2.New(proto.MaxParts), remaining - FixedPoint2.New(proto.MinParts) * missingAspects);
                    var minimumWhole = (minimum.Value + 99) / 100;
                    var maximumWhole = maximum.Value / 100;
                    amount = missingAspects == 0 ? remaining : minimumWhole <= maximumWhole
                        ? FixedPoint2.New(random.Next(minimumWhole, maximumWhole + 1))
                        : minimum;
                    remainingAspects -= amount;
                }
                else
                    amount = FixedPoint2.New(random.Next(proto.MinParts, proto.MaxParts + 1));
            }
            if (amount <= 0 || !result.Ingredients.TryAdd(reagent, amount) ||
                (reagent == "AlchemyDarkness" && proto.Tier < 3))
                throw new InvalidOperationException($"Invalid ingredient in alchemy recipe {proto.ID}.");
        }
        if (result.Ingredients.Count == 0)
            throw new InvalidOperationException($"Alchemy recipe {proto.ID} has no ingredients.");
        var steps = proto.Steps ?? Enumerable.Range(0, proto.ExpectedSteps).Select(_ => new AlchemyStep()).ToList();
        if (steps.Count > 64)
            throw new InvalidOperationException($"Too many steps in alchemy recipe {proto.ID}.");
        var budget = proto.MaxComplexity;
        foreach (var step in steps)
        {
            if (step.Operation == null)
                continue;
            if (!operations.TryGetValue(step.Operation, out var operation))
                throw new InvalidOperationException($"Invalid operation in alchemy recipe {proto.ID}.");
            budget -= operation.Complexity;
        }
        if (budget < 0)
            throw new InvalidOperationException($"Alchemy recipe {proto.ID} exceeds its complexity budget.");
        var candidates = operations.Values.Where(o => o.MinimumTier <= proto.Tier && o.Weight > 0 && o.Complexity > 0).ToList();
        var missing = steps.Count(s => s.Operation == null);
        if (missing > 0 && (!proto.Randomized || candidates.Count == 0))
            throw new InvalidOperationException($"Cannot generate steps for alchemy recipe {proto.ID}.");
        var cheapest = candidates.Count == 0 ? 0 : candidates.Min(o => o.Complexity);
        if (missing * cheapest > budget)
            throw new InvalidOperationException($"Alchemy recipe {proto.ID} cannot fit its expected steps in its budget.");
        foreach (var step in steps)
        {
            if (step.Operation != null)
            {
                result.Steps.Add(step.Operation);
                continue;
            }
            missing--;
            var eligible = candidates.Where(o => o.Complexity <= budget - missing * cheapest)
                .ToDictionary(o => o.ID, o => o.Weight);
            var id = Pick(random, eligible);
            result.Steps.Add(id);
            budget -= operations[id].Complexity;
        }
        return result;
    }

    private static bool IsAspect(string reagent) => reagent is
        "AlchemyWater" or "AlchemyEarth" or "AlchemyFire" or "AlchemyLight" or "AlchemyDarkness";

    public static bool Conflicts(AlchemyRecipe left, AlchemyRecipe right)
    {
        if (left.Entities.Count != right.Entities.Count || left.Entities.Keys.Any(k => !right.Entities.ContainsKey(k)) ||
            left.Ingredients.Count != right.Ingredients.Count ||
            left.Ingredients.Keys.Any(k => !right.Ingredients.ContainsKey(k)))
            return false;
        var first = left.Ingredients.First();
        if (left.StrictRatio && right.StrictRatio && (left.Ingredients.Any(p =>
                (long) p.Value.Value * right.Ingredients[first.Key].Value !=
                (long) right.Ingredients[p.Key].Value * first.Value.Value) || left.Entities.Any(p =>
                (long) p.Value * right.Ingredients[first.Key].Value !=
                (long) right.Entities[p.Key] * first.Value.Value)))
            return false;
        var shorter = left.Steps.Count <= right.Steps.Count ? left.Steps : right.Steps;
        var longer = left.Steps.Count <= right.Steps.Count ? right.Steps : left.Steps;
        if (shorter.Count == 0)
            return true;
        for (var offset = 0; offset <= longer.Count - shorter.Count; offset++)
        {
            if (shorter.SequenceEqual(longer.Skip(offset).Take(shorter.Count)))
                return true;
        }
        return false;
    }
}

