using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Imperial.Medieval.SkeletonInvasion.Progression;

public sealed class SkeletonUpgradeLayout : LayoutContainer
{
    public Vector2 Offset { get; private set; }

    public void SetOffset(Vector2 offset)
    {
        Offset = offset;
        InvalidateArrange();
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        foreach (var child in Children)
        {
            if (child is not SkeletonUpgradeEntry entry)
                continue;

            var position = finalSize / 2 + entry.Prototype.Position + Offset - entry.DesiredSize / 2;
            entry.Arrange(UIBox2.FromDimensions(position, entry.DesiredSize));
        }

        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        foreach (var child in Children)
        {
            if (child is not SkeletonUpgradeEntry entry)
                continue;

            foreach (var otherChild in Children)
            {
                if (otherChild is not SkeletonUpgradeEntry prerequisite ||
                    !entry.Prototype.Required.Contains(prerequisite.Prototype.ID))
                    continue;

                handle.DrawLine(entry.PixelPosition + entry.PixelSize / 2,
                    prerequisite.PixelPosition + prerequisite.PixelSize / 2, Color.WhiteSmoke);
            }
        }
    }
}
