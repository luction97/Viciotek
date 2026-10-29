using System.Windows;
using System.Windows.Controls;
using Viciotek.Infrastructure.Imaging;
using Viciotek.Infrastructure.Shell;

namespace Viciotek.Presentation.Controls;

/// <summary>
/// ListBox that exposes the image services to the item template without
/// coupling the view models to WPF imaging classes.
/// </summary>
public sealed class GameGallery : ListBox
{
    public static readonly DependencyProperty CoverLoaderProperty = DependencyProperty.Register(
        nameof(CoverLoader),
        typeof(CoverImageLoader),
        typeof(GameGallery),
        new PropertyMetadata(null));

    public static readonly DependencyProperty IconLoaderProperty = DependencyProperty.Register(
        nameof(IconLoader),
        typeof(IconService),
        typeof(GameGallery),
        new PropertyMetadata(null));

    public CoverImageLoader? CoverLoader
    {
        get => (CoverImageLoader?)GetValue(CoverLoaderProperty);
        set => SetValue(CoverLoaderProperty, value);
    }

    public IconService? IconLoader
    {
        get => (IconService?)GetValue(IconLoaderProperty);
        set => SetValue(IconLoaderProperty, value);
    }

    public void ScrollToTop()
    {
        if (Items.Count > 0)
        {
            ScrollIntoView(Items[0]);
        }
    }

    public void ScrollToItem(object item)
    {
        if (Items.Contains(item))
        {
            ScrollIntoView(item);
        }
    }
}
