namespace Content.Client.Imperial.Medieval.Trading;

[RegisterComponent]
[Access(typeof(TradingExamineSystem))]
public sealed partial class TradingExamineComponent : Component
{
    public EntityUid Pit;
    public EntityUid Target;
    public bool PreviousSkipChecks;
    public bool RestorePending;
}
