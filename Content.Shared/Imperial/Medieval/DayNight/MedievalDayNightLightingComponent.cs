using Robust.Shared.GameStates;

namespace Content.Shared.Imperial.Medieval.DayNight;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MedievalDayNightLightingComponent : Component
{
    [DataField, AutoNetworkedField]
    public Color DayColor = Color.White;

    [DataField, AutoNetworkedField]
    public Color NightTint = new(0.55f, 0.65f, 1f);

    [DataField, AutoNetworkedField]
    public float NightBrightness = 0.18f;

    [DataField, AutoNetworkedField]
    public TimeSpan TransitionDuration = TimeSpan.FromMinutes(2);

    [DataField, AutoNetworkedField]
    public Color ColorMultiplier = Color.White;

    [ViewVariables, AutoNetworkedField]
    public bool IsNight;

    [ViewVariables, AutoNetworkedField]
    public TimeSpan PhaseStartedAt;

    [ViewVariables, AutoNetworkedField]
    public TimeSpan PhaseDuration;

    [ViewVariables]
    public bool LightingConfigured;
}
