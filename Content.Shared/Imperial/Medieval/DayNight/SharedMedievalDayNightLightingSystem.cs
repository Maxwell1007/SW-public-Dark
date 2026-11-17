namespace Content.Shared.Imperial.Medieval.DayNight;

public abstract class SharedMedievalDayNightLightingSystem : EntitySystem
{
    public static Color GetColor(MedievalDayNightLightingComponent component, TimeSpan time)
    {
        var transition = Math.Min(component.TransitionDuration.TotalSeconds, component.PhaseDuration.TotalSeconds);
        var elapsed = (time - component.PhaseStartedAt).TotalSeconds;
        var progress = transition <= 0 ? 0f : (float) Math.Clamp(
            (elapsed - component.PhaseDuration.TotalSeconds + transition) / transition, 0, 1);
        var night = component.IsNight ? 1f - progress : progress;
        var brightness = 1f + (Math.Clamp(component.NightBrightness, 0f, 1f) - 1f) * night;
        var tint = component.NightTint;
        var color = component.DayColor;
        var multiplier = component.ColorMultiplier;
        return new Color(
            color.R * brightness * (1f + (tint.R - 1f) * night) * multiplier.R,
            color.G * brightness * (1f + (tint.G - 1f) * night) * multiplier.G,
            color.B * brightness * (1f + (tint.B - 1f) * night) * multiplier.B,
            color.A);
    }
}
