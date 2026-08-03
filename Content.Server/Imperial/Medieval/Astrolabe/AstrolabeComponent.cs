namespace Content.Server.Imperial.Medieval.Astrolabe;

[RegisterComponent]
public sealed partial class AstrolabeComponent : Component
{
    [DataField]
    public int IntelligenceMinToUse = 8;

    [DataField]
    public float BaseDoAfterSeconds = 10f;

    [DataField]
    public float MinimumDoAfterSeconds = 0.5f;

    [DataField]
    public float IntelligenceModifier = 0.5f;
}
