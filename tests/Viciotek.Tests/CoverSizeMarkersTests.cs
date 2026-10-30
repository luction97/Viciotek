using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Shapes;
namespace Viciotek.Tests;
public sealed class CoverSizeMarkersTests
{
    [Theory]
    [InlineData(300)][InlineData(550)]
    public Task Markers_AlignWithEveryPresetAtDifferentWidths(double width) => WpfTest.RunAsync(() =>
    {
        var theme = (ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/Viciotek;component/Presentation/Theme/Theme.xaml", UriKind.Relative));
        var slider = new Slider { Width = width, Minimum = 0, Maximum = 3, TickFrequency = 1,
            IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, SmallChange = 1, LargeChange = 1,
            Style = (Style)theme["CoverSizeSliderStyle"] };
        var window = new Window { Content = slider, Width = width + 50, Height = 150, Left = -3000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); window.UpdateLayout();
            Assert.Equal(Cursors.Hand, slider.Cursor);
            var markers = (Grid)slider.Template.FindName("PresetMarkers", slider);
            var track = (Track)slider.Template.FindName("PART_Track", slider);
            var dots = markers.Children.OfType<Ellipse>().ToArray();
            Assert.Equal(4, dots.Length);
            for (var preset = 0; preset <= 3; preset++)
            {
                slider.Value = preset; window.UpdateLayout();
                var dot = dots[preset].TranslatePoint(new Point(3, 3), slider);
                var thumb = track.Thumb.TranslatePoint(new Point(track.Thumb.ActualWidth / 2, track.Thumb.ActualHeight / 2), slider);
                Assert.InRange(Math.Abs(dot.X - thumb.X), 0, 1);
            }
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });
}
