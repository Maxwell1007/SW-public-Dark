namespace Content.Server.Actions;

[ByRefEvent]
public readonly record struct ActionAttachmentChangedEvent(EntityUid Performer, bool Added);
