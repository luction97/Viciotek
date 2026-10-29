using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Viciotek.Presentation.Controls;

/// <summary>
/// Subtle, game-like hover lift for cover cards. Attach it to the card root
/// and set <c>RenderTransformOrigin="0.5,0.5"</c>. When <see cref="Enabled"/>
/// is false the card stays flat, letting the user disable motion effects.
/// </summary>
public static class CardHoverBehavior
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(CardHoverBehavior),
        new PropertyMetadata(true, OnEnabledChanged));

    private static readonly DependencyProperty AttachedProperty = DependencyProperty.RegisterAttached(
        "Attached",
        typeof(bool),
        typeof(CardHoverBehavior),
        new PropertyMetadata(false));

    private const double HoverScale = 1.045;
    private const int DurationMilliseconds = 150;

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not FrameworkElement element)
        {
            return;
        }

        if (!(bool)element.GetValue(AttachedProperty))
        {
            element.SetValue(AttachedProperty, true);
            element.Loaded += OnLoaded;
            element.Unloaded += OnUnloaded;
        }

        if (!(bool)e.NewValue)
        {
            Reset(element);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.MouseEnter += OnMouseEnter;
            element.MouseLeave += OnMouseLeave;
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.MouseEnter -= OnMouseEnter;
            element.MouseLeave -= OnMouseLeave;
            Reset(element);
        }
    }

    private static void OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element && GetEnabled(element))
        {
            Animate(element, HoverScale);
        }
    }

    private static void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            Animate(element, 1.0);
        }
    }

    private static void Animate(FrameworkElement element, double target)
    {
        if (element.RenderTransform is not ScaleTransform transform)
        {
            transform = new ScaleTransform(1.0, 1.0);
            element.RenderTransform = transform;
        }

        var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(DurationMilliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private static void Reset(FrameworkElement element)
    {
        if (element.RenderTransform is ScaleTransform transform)
        {
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            transform.ScaleX = 1.0;
            transform.ScaleY = 1.0;
        }
        else
        {
            element.RenderTransform = new ScaleTransform(1.0, 1.0);
        }
    }
}
