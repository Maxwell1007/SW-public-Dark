using Content.Shared.Imperial.Medieval.SkeletonInvasion;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client.Imperial.Medieval.SkeletonInvasion.Progression;

public sealed class SkeletonProgressionBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private SkeletonProgressionWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<SkeletonProgressionWindow>();
        _window.BuyPressed += upgrade => SendMessage(new SkeletonBuyUpgradeMessage(upgrade));
        if (State is SkeletonProgressionBuiState state)
            _window.Populate(state);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is SkeletonProgressionBuiState progression)
            _window?.Populate(progression);
    }
}
