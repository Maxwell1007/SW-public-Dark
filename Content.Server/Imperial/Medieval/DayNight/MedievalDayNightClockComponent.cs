namespace Content.Server.Imperial.Medieval.DayNight;

[RegisterComponent]
public sealed partial class MedievalDayNightClockComponent : Component
{
    [DataField]
    public TimeSpan DayDuration = TimeSpan.FromMinutes(12);

    [DataField]
    public TimeSpan NightDuration = TimeSpan.FromMinutes(12);

    [DataField]
    public TimeSpan LightingUpdateInterval = TimeSpan.FromSeconds(0.5);

    [DataField]
    public Color ColorMultiplier = Color.White;

    [ViewVariables]
    public int PhaseIndex;

    [ViewVariables]
    public TimeSpan PhaseStartedAt;

    [ViewVariables]
    public TimeSpan NextTransition;

    [ViewVariables]
    public TimeSpan NextLightingUpdate;

    [ViewVariables]
    public bool ClockStarted;
}
