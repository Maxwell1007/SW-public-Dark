using System.Linq;
using Content.Server.Actions;
using Content.Server.Imperial.Medieval.Skills;
using Content.Shared.Actions.Components;
using Content.Shared.Imperial.Medieval.Magic.Memory;
using Content.Shared.Imperial.Medieval.Skills;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.Magic.Memory;

public sealed class MagicMemorySystem : EntitySystem
{
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private readonly HashSet<EntityUid> _refreshing = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<SkillsComponent, ComponentInit>(OnSkillsInit);
        SubscribeLocalEvent<MagicMemoryComponent, SkillLevelChangedEvent>(OnSkillChanged);
        SubscribeLocalEvent<MagicMemoryComponent, MapInitEvent>(OnMemoryInit);
        SubscribeLocalEvent<SpellMemoryComponent, ActionAttachmentChangedEvent>(OnActionChanged);
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        Refresh(args.Entity);
    }

    private void OnSkillsInit(EntityUid uid, SkillsComponent component, ComponentInit args)
    {
        Refresh(uid);
    }

    private void OnMemoryInit(EntityUid uid, MagicMemoryComponent component, MapInitEvent args)
    {
        Refresh(uid);
    }

    private void OnSkillChanged(EntityUid uid, MagicMemoryComponent component, ref SkillLevelChangedEvent args)
    {
        if (args.Id == SharedSkillsSystem.IntelligenceId)
            Refresh(uid);
    }

    private void OnActionChanged(EntityUid uid, SpellMemoryComponent component, ref ActionAttachmentChangedEvent args)
    {
        if (!HasComp<MagicMemoryComponent>(args.Performer) && !HasComp<ActorComponent>(args.Performer) &&
            !HasComp<SkillsComponent>(args.Performer))
        {
            return;
        }

        if (args.Added)
            component.SpellOwner = args.Performer;

        if (!TerminatingOrDeleted(args.Performer))
            Refresh(args.Performer);
    }

    public MagicMemoryComponent Refresh(EntityUid uid)
    {
        var memory = EnsureComp<MagicMemoryComponent>(uid);
        if (!_refreshing.Add(uid))
            return memory;

        try
        {
            var intelligence = TryComp<SkillsComponent>(uid, out var skills)
                ? skills.Levels.GetValueOrDefault(SharedSkillsSystem.IntelligenceId, 10)
                : 0;
            var maximum = Math.Max(0, intelligence + (intelligence >= 20 ? 4 : 0));
            var current = 0;

            foreach (var action in _actions.GetActions(uid).ToArray())
            {
                if (!TryComp<SpellMemoryComponent>(action, out var spell))
                    continue;

                spell.SpellOwner = uid;
                var cost = Math.Max(0, spell.Cost);
                if (spell.Forgotten || cost > maximum - current)
                {
                    spell.Forgotten = true;
                    _actions.RemoveAction(uid, action.Owner);
                    continue;
                }

                current += cost;
            }

            if (memory.MaxMemory != maximum || memory.CurrentMemory != current)
            {
                memory.MaxMemory = maximum;
                memory.CurrentMemory = current;
                Dirty(uid, memory);
            }
        }
        finally
        {
            _refreshing.Remove(uid);
        }

        return memory;
    }

    public int GetCost(EntProtoId prototype)
    {
        return _prototypes.Index(prototype).TryGetComponent<SpellMemoryComponent>(out var spell, EntityManager.ComponentFactory)
            ? Math.Max(0, spell.Cost)
            : 0;
    }

    public bool CanLearn(EntityUid uid, int cost, EntityUid? replacedAction = null)
    {
        var memory = Refresh(uid);
        var released = 0;
        if (TryComp<SpellMemoryComponent>(replacedAction, out var spell) &&
            TryComp<ActionComponent>(replacedAction, out var action) && action.AttachedEntity == uid && !spell.Forgotten)
        {
            released = Math.Max(0, spell.Cost);
        }

        return Math.Max(0, cost) <= memory.MaxMemory - memory.CurrentMemory + released;
    }

    public bool TryForget(EntityUid uid, EntityUid actionUid)
    {
        if (!TryComp<SpellMemoryComponent>(actionUid, out var spell) || spell.Forgotten ||
            !TryComp<ActionComponent>(actionUid, out var action) || action.AttachedEntity != uid)
        {
            return false;
        }

        spell.SpellOwner = uid;
        spell.Forgotten = true;
        _actions.RemoveAction(uid, actionUid);
        Refresh(uid);
        return true;
    }

    public bool TryRemember(EntityUid uid, EntityUid actionUid)
    {
        if (!TryComp<SpellMemoryComponent>(actionUid, out var spell) || !spell.Forgotten || spell.SpellOwner != uid ||
            !TryComp<ActionComponent>(actionUid, out var action) || !CanLearn(uid, spell.Cost))
        {
            return false;
        }

        spell.Forgotten = false;
        if (!_actions.AddActionDirect(uid, actionUid))
        {
            spell.Forgotten = true;
            return false;
        }

        _actions.SetCooldown(actionUid, (action.UseDelay ?? TimeSpan.Zero) * 2);
        Refresh(uid);
        return true;
    }
}
