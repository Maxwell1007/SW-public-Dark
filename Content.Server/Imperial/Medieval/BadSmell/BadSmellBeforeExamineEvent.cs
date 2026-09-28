namespace Content.Server.BadSmell;

[Serializable]
public sealed class BadSmellBeforeExamineEvent : EntityEventArgs
{
    public bool Cancelled { get; set; }
    public List<string> Scents { get; } = new();
}