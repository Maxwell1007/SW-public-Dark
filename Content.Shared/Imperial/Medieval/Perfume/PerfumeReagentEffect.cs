using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared.Imperial.Medieval.Perfume;

public sealed partial class PerfumeReagentEffect : EntityEffect
{
    /// <summary>
    /// Уникальный ID запаха для предотвращения дублирования (например, "lavender")
    /// </summary>
    [DataField(required: true)]
    public string ScentId = default!;

    /// <summary>
    /// Ключ локализации запаха
    /// </summary>
    [DataField(required: true)]
    public LocId LocKey = default!;

    [DataField]
    public float DurationPerUnit = 300f;

    [DataField]
    public float MaxDuration = 3600f;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var target = args.TargetEntity;
        var entMan = args.EntityManager;

        var units = 1f;
        if (args is EntityEffectReagentArgs reagentArgs)
        {
            units = reagentArgs.Quantity.Float() * reagentArgs.Scale.Float();
        }

        var addedSeconds = DurationPerUnit * units;
        if (addedSeconds <= 0f)
            return;

        var comp = entMan.EnsureComponent<PerfumeTargetComponent>(target);

        var currentSeconds = 0f;
        if (comp.Scents.TryGetValue(ScentId, out var existing))
        {
            currentSeconds = (float)existing.Duration.TotalSeconds;
        }

        var newSeconds = MathF.Min(currentSeconds + addedSeconds, MaxDuration);

        comp.Scents[ScentId] = new PerfumeScentInstance
        {
            LocKey = LocKey,
            Duration = TimeSpan.FromSeconds(newSeconds)
        };

        entMan.Dirty(target, comp);
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;
}