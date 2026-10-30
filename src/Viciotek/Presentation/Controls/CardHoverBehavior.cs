using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Viciotek.Presentation.Controls;

/// <summary>
/// Subtle, game-like hover lift for cover cards. Attach it to the card root
/// without scaling or resampling the artwork. When <see cref="Enabled"/>
/// is false the card stays flat, letting the user disable motion effects.
/// </summary>
public static class CardHoverBehavior
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(CardHoverBehavior),
        new PropertyMetadata(false, OnEnabledChanged));

    private static readonly DependencyProperty AttachedProperty = DependencyProperty.RegisterAttached(
        "Attached",
        typeof(bool),
        typeof(CardHoverBehavior),
        new PropertyMetadata(false));

    private const double HoverOffset = -6;
    private const int DurationMilliseconds = 180;

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
        else if (element.IsLoaded)
        {
            OnLoaded(element, new RoutedEventArgs());
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.MouseEnter -= OnMouseEnter;
            element.MouseLeave -= OnMouseLeave;
            element.IsKeyboardFocusWithinChanged -= OnKeyboardFocusChanged;
            element.MouseEnter += OnMouseEnter;
            element.MouseLeave += OnMouseLeave;
            element.IsKeyboardFocusWithinChanged += OnKeyboardFocusChanged;
            UpdateHover(element);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.MouseEnter -= OnMouseEnter;
            element.MouseLeave -= OnMouseLeave;
            element.IsKeyboardFocusWithinChanged -= OnKeyboardFocusChanged;
            Reset(element);
        }
    }

    private static void OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element && ShouldAnimate(GetEnabled(element), SystemParameters.ClientAreaAnimation))
        {
            Animate(element, HoverOffset);
        }
    }

    private static void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            UpdateHover(element);
        }
    }

    private static void OnKeyboardFocusChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            UpdateHover(element);
        }
    }

    private static void UpdateHover(FrameworkElement element)
    {
        if (!ShouldAnimate(GetEnabled(element), SystemParameters.ClientAreaAnimation))
        {
            Reset(element);
            return;
        }
        Animate(element, element.IsMouseOver || element.IsKeyboardFocusWithin ? HoverOffset : 0);
    }

    private static void Animate(FrameworkElement element, double target)
    {
        if (element.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            element.RenderTransform = transform;
        }

        var animation = new DoubleAnimation(transform.Y, target, TimeSpan.FromMilliseconds(DurationMilliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        transform.Y = target;
        transform.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    internal static bool ShouldAnimate(bool appEnabled, bool systemEnabled) => appEnabled && systemEnabled;

    private static void Reset(FrameworkElement element)
    {
        if (element.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.Y = 0;
        }
        else
        {
            element.RenderTransform = new TranslateTransform();
        }
    }
}
