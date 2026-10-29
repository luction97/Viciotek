using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Viciotek.Presentation.Controls;

/// <summary>
/// Border that clips its children to the rounded shape. WPF's Border draws a
/// rounded background but does not clip children, which is visible when a
/// cover image is painted edge to edge.
/// </summary>
public sealed class RoundedClipBorder : Border
{
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateClip();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == CornerRadiusProperty)
        {
            UpdateClip();
        }
    }

    private void UpdateClip()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            Clip = null;
            return;
        }

        var radius = Math.Max(
            Math.Max(CornerRadius.TopLeft, CornerRadius.TopRight),
            Math.Max(CornerRadius.BottomLeft, CornerRadius.BottomRight));

        if (radius <= 0)
        {
            Clip = null;
            return;
        }

        var geometry = new RectangleGeometry(
            new Rect(0, 0, ActualWidth, ActualHeight),
            radius,
            radius);
        geometry.Freeze();
        Clip = geometry;
    }
}
