using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client.Imperial.Medieval.HandTransfer;

public sealed class HandTransferOverlay(
    IInputManager input,
    IPlayerManager player,
    IEntityManager entityManager) : Overlay
{
    private static readonly Vector2 CursorOffset = new(8f, 8f);
    private static readonly Vector2 IndicatorSize = new(12f, 12f);

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (player.LocalEntity is not { } user ||
            !entityManager.HasComponent<HandTransferTargetingComponent>(user))
        {
            return;
        }

        var mousePosition = input.MouseScreenPosition;
        if (mousePosition.Window == WindowId.Invalid)
            return;

        var indicator = UIBox2.FromDimensions(mousePosition.Position + CursorOffset, IndicatorSize);
        args.ScreenHandle.DrawRect(indicator, Color.White.WithAlpha(0.75f));
    }
}
