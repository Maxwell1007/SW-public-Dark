namespace Content.Server.Imperial.Medieval.GameTicking.Rules;

[RegisterComponent]
public sealed partial class MedievalLycantropyRuleComponent : Component
{
    [DataField]
    public TimeSpan BloodMoonRoundEndDelay = TimeSpan.FromMinutes(10);

    [ViewVariables]
    public TimeSpan? EndTime;
}
