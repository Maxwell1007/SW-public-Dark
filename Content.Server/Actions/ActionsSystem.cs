using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using JetBrains.Annotations;

namespace Content.Server.Actions
{
    [UsedImplicitly]
    public sealed class ActionsSystem : SharedActionsSystem
    {
        protected override void ActionAdded(Entity<ActionsComponent> performer, Entity<ActionComponent> action)
        {
            var ev = new ActionAttachmentChangedEvent(performer, true);
            RaiseLocalEvent(action, ref ev);
        }

        protected override void ActionRemoved(Entity<ActionsComponent> performer, Entity<ActionComponent> action)
        {
            var ev = new ActionAttachmentChangedEvent(performer, false);
            RaiseLocalEvent(action, ref ev);
        }
    }
}
