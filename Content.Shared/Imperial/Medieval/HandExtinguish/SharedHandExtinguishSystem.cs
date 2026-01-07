using Content.Shared.Atmos.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Robust.Shared.Network;

namespace Content.Shared.Imperial.Medieval.HandExtinguish;

public sealed class SharedHandExtinguishSystem : EntitySystem
{
    [Dependency] private readonly INetManager _netManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FlammableComponent, InteractHandEvent>(
            OnInteractHand,
            before:
            [
                typeof(Content.Shared.Interaction.InteractionPopupSystem)
            ]);
    }

    private void OnInteractHand(EntityUid uid, FlammableComponent component, InteractHandEvent args)
    {
        if (!_netManager.IsClient)
            return;

        if (args.Handled)
            return;

        if (args.User == args.Target)
            return;

        if (!component.OnFire || !component.CanExtinguish)
            return;

        if (!HasComp<HandsComponent>(args.User))
            return;

        args.Handled = true;
    }
}
