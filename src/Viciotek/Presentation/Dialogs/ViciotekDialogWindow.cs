using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Viciotek.Infrastructure.Shell;

namespace Viciotek.Presentation.Dialogs;

/// <summary>
/// Shared chrome for the three modal dialogs: borderless, movable and closed
/// with Escape. Transparency lets the outer border draw a soft drop shadow.
/// </summary>
public abstract class ViciotekDialogWindow : Window
{
    protected ViciotekDialogWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
        PreviewKeyDown += OnDialogPreviewKeyDown;
        SourceInitialized += (_, _) => SetWorkAreaLimit(Owner ?? this);
        ContentRendered += (_, _) => { SetWorkAreaLimit(this); WindowAppearance.KeepDialogOnScreen(this); };
        DpiChanged += (_, _) => { SetWorkAreaLimit(this); WindowAppearance.KeepDialogOnScreen(this); };
    }

    private void SetWorkAreaLimit(Window reference)
    {
        var available = WindowAppearance.WorkAreaSize(reference);
        MinWidth = Math.Min(MinWidth, available.Width);
        MinHeight = Math.Min(MinHeight, available.Height);
        MaxWidth = available.Width;
        MaxHeight = available.Height;
    }

    protected void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    protected void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OnDialogPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}
