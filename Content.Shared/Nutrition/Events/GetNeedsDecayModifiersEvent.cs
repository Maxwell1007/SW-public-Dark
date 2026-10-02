namespace Content.Shared.Nutrition.Events;

[ByRefEvent]
public record struct GetNeedsDecayModifiersEvent(float Modifier = 1f);
