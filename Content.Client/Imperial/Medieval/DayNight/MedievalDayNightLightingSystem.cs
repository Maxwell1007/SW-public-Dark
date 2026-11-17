using Content.Shared.Imperial.Medieval.DayNight;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Client.Imperial.Medieval.DayNight;

public sealed class MedievalDayNightLightingSystem : SharedMedievalDayNightLightingSystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        var query = AllEntityQuery<MedievalDayNightLightingComponent, MapLightComponent>();
        while (query.MoveNext(out _, out var lighting, out var mapLight))
        {
            if (lighting.PhaseDuration > TimeSpan.Zero)
                mapLight.AmbientLightColor = GetColor(lighting, _timing.CurTime);
        }
    }
}
