namespace Content.Server.Imperial.Medieval.Alchemy;

[RegisterComponent]
public sealed partial class AlchemyCoolingBathComponent : Component
{
    [DataField] public float Temperature = 273.15f;
    [DataField] public List<int> Durations = new() { 5, 10, 15, 30 };
    public float RemainingTime;
    public bool IsProcessing;
}
