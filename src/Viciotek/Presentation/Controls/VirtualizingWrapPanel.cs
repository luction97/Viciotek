using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Viciotek.Presentation.Controls;

/// <summary>
/// Uniform grid with genuine UI virtualization. Only containers in (and one
/// row around) the viewport are generated; a standard WrapPanel does not do
/// this. The grid is centered and the number of columns follows the width.
/// </summary>
public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth),
        typeof(double),
        typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(200d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight),
        typeof(double),
        typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(300d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap),
        typeof(double),
        typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty HorizontalPaddingProperty = DependencyProperty.Register(
        nameof(HorizontalPadding),
        typeof(double),
        typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(40d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty VerticalPaddingProperty = DependencyProperty.Register(
        nameof(VerticalPadding),
        typeof(double),
        typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(20d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private const double ScrollLineDelta = 72;
    private Size _extent;
    private Size _viewport;
    private Point _offset;
    private int _columns = 1;
    private int _rows;
    private int _itemCount;
    private double _gridLeft;
    private int _firstRealizedIndex = -1;
    private int _lastRealizedIndex = -1;
    private bool _realizeRetryScheduled;

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public double HorizontalPadding
    {
        get => (double)GetValue(HorizontalPaddingProperty);
        set => SetValue(HorizontalPaddingProperty, value);
    }

    public double VerticalPadding
    {
        get => (double)GetValue(VerticalPaddingProperty);
        set => SetValue(VerticalPaddingProperty, value);
    }

    public int Columns => _columns;
    public int RealizedItemCount => InternalChildren.Count;

    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; } = true;
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;
    public ScrollViewer? ScrollOwner { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Subscribe to generator changes even while initially empty (e.g. remote cover results).
        // Otherwise an Add can leave this panel's cached item count at zero until a resize.
        _ = InternalChildren;
        UpdateLayoutMetrics(availableSize);

        if (_itemCount == 0)
        {
            CleanUpItems(0, -1);
            return CreateViewportSize(availableSize);
        }

        var firstVisible = FindFirstVisibleIndex();
        var lastVisible = FindLastVisibleIndex();
        _firstRealizedIndex = firstVisible;
        _lastRealizedIndex = lastVisible;

        CleanUpItems(firstVisible, lastVisible);
        RealizeItems(firstVisible, lastVisible);

        // The item container generator is occasionally not connected during
        // the first measure pass. Without this retry an initially loaded
        // library can stay invisible until the user resizes the window.
        if (InternalChildren.Count == 0 && !_realizeRetryScheduled)
        {
            _realizeRetryScheduled = true;
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    _realizeRetryScheduled = false;
                    InvalidateMeasure();
                }));
        }

        return CreateViewportSize(availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var generator = ItemContainerGenerator;
        if (generator is null)
        {
            return finalSize;
        }

        for (var childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
        {
            var child = InternalChildren[childIndex];
            var itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0));
            if (itemIndex < 0)
            {
                continue;
            }

            var row = itemIndex / _columns;
            var column = itemIndex % _columns;
            var x = _gridLeft + (column * (ItemWidth + Gap));
            var y = VerticalPadding + (row * (ItemHeight + Gap)) - VerticalOffset;

            child.Arrange(new Rect(x, y, ItemWidth, ItemHeight));
        }

        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        switch (args.Action)
        {
            case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Move:
                if (args.ItemUICount > 0)
                {
                    RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                }
                break;
            case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                RemoveInternalChildRange(0, InternalChildren.Count);
                break;
        }

        InvalidateMeasure();
    }

    protected override void BringIndexIntoView(int index)
    {
        if (index < 0 || index >= _itemCount || _columns <= 0)
        {
            return;
        }

        var row = index / _columns;
        var top = VerticalPadding + (row * (ItemHeight + Gap));
        var bottom = top + ItemHeight;

        if (top < VerticalOffset)
        {
            SetVerticalOffset(top);
        }
        else if (bottom > VerticalOffset + ViewportHeight)
        {
            SetVerticalOffset(bottom - ViewportHeight);
        }
    }

    private void UpdateLayoutMetrics(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? Math.Max(_viewport.Width, 900) : availableSize.Width;
        if (width <= 0)
        {
            width = 1;
        }

        _itemCount = ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0;

        var usableWidth = Math.Max(0, width - (2 * HorizontalPadding));
        _columns = Math.Max(1, (int)Math.Floor((usableWidth + Gap) / (ItemWidth + Gap)));
        _rows = _itemCount == 0 ? 0 : (int)Math.Ceiling(_itemCount / (double)_columns);

        var contentWidth = (_columns * ItemWidth) + (Math.Max(0, _columns - 1) * Gap);
        _gridLeft = HorizontalPadding + Math.Max(0, (usableWidth - contentWidth) / 2);

        var contentHeight = _rows == 0
            ? 0
            : (VerticalPadding * 2) + (_rows * ItemHeight) + (Math.Max(0, _rows - 1) * Gap);

        var height = double.IsInfinity(availableSize.Height)
            ? Math.Max(contentHeight, ItemHeight + (2 * VerticalPadding))
            : Math.Max(0, availableSize.Height);

        var newExtent = new Size(width, contentHeight);
        var newViewport = new Size(width, height);
        var changed = newExtent != _extent || newViewport != _viewport;

        _extent = newExtent;
        _viewport = newViewport;

        var maxOffset = Math.Max(0, _extent.Height - _viewport.Height);
        if (VerticalOffset > maxOffset)
        {
            _offset.Y = maxOffset;
            changed = true;
        }

        if (_offset.Y < 0)
        {
            _offset.Y = 0;
            changed = true;
        }

        if (changed)
        {
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    private int FindFirstVisibleIndex()
    {
        if (_rows == 0)
        {
            return 0;
        }

        var row = Math.Floor((VerticalOffset - VerticalPadding) / (ItemHeight + Gap));
        var bufferedRow = Math.Max(0, (int)row - 1);
        return Math.Clamp(bufferedRow * _columns, 0, Math.Max(0, _itemCount - 1));
    }

    private int FindLastVisibleIndex()
    {
        if (_rows == 0)
        {
            return -1;
        }

        var row = Math.Floor((VerticalOffset + ViewportHeight - VerticalPadding) / (ItemHeight + Gap));
        var bufferedRow = Math.Min(_rows - 1, (int)row + 1);
        return Math.Clamp(((bufferedRow + 1) * _columns) - 1, 0, Math.Max(0, _itemCount - 1));
    }

    private void RealizeItems(int firstIndex, int lastIndex)
    {
        if (firstIndex < 0 || lastIndex < firstIndex)
        {
            return;
        }

        var generator = ItemContainerGenerator;
        if (generator is null)
        {
            return;
        }

        var startPosition = generator.GeneratorPositionFromIndex(firstIndex);
        var childIndex = startPosition.Offset == 0 ? startPosition.Index : startPosition.Index + 1;

        using (generator.StartAt(startPosition, GeneratorDirection.Forward, true))
        {
            for (var itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++, childIndex++)
            {
                var child = (UIElement)generator.GenerateNext(out var newlyRealized);
                if (newlyRealized)
                {
                    if (childIndex >= InternalChildren.Count)
                    {
                        AddInternalChild(child);
                    }
                    else
                    {
                        InsertInternalChild(childIndex, child);
                    }

                    generator.PrepareItemContainer(child);
                }

                child.Measure(new Size(ItemWidth, ItemHeight));
            }
        }
    }

    private void CleanUpItems(int firstIndex, int lastIndex)
    {
        var generator = ItemContainerGenerator;
        if (generator is null)
        {
            return;
        }

        for (var childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
        {
            var position = new GeneratorPosition(childIndex, 0);
            var itemIndex = generator.IndexFromGeneratorPosition(position);
            if (itemIndex < 0 || itemIndex < firstIndex || itemIndex > lastIndex)
            {
                generator.Remove(position, 1);
                RemoveInternalChildRange(childIndex, 1);
            }
        }
    }

    private Size CreateViewportSize(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? _viewport.Width : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height)
            ? Math.Max(_extent.Height, ItemHeight + (2 * VerticalPadding))
            : availableSize.Height;
        return new Size(Math.Max(0, width), Math.Max(0, height));
    }

    public void LineUp() => SetVerticalOffset(VerticalOffset - ScrollLineDelta);

    public void LineDown() => SetVerticalOffset(VerticalOffset + ScrollLineDelta);

    public void LineLeft()
    {
    }

    public void LineRight()
    {
    }

    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - (ItemHeight + Gap));

    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + (ItemHeight + Gap));

    public void MouseWheelLeft()
    {
    }

    public void MouseWheelRight()
    {
    }

    public void PageUp() => SetVerticalOffset(VerticalOffset - (ViewportHeight * 0.9));

    public void PageDown() => SetVerticalOffset(VerticalOffset + (ViewportHeight * 0.9));

    public void PageLeft()
    {
    }

    public void PageRight()
    {
    }

    public void SetHorizontalOffset(double offset)
    {
    }

    public void SetVerticalOffset(double offset)
    {
        var maxOffset = Math.Max(0, ExtentHeight - ViewportHeight);
        var target = Math.Clamp(offset, 0, maxOffset);
        if (Math.Abs(target - _offset.Y) < 0.1)
        {
            return;
        }

        _offset.Y = target;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
        InvalidateArrange();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        var child = visual as UIElement;
        while (child is not null && !InternalChildren.Contains(child))
        {
            child = VisualTreeHelper.GetParent(child) as UIElement;
        }

        if (child is null)
        {
            return Rect.Empty;
        }

        var childIndex = InternalChildren.IndexOf(child);
        if (childIndex < 0)
        {
            return Rect.Empty;
        }

        var generator = ItemContainerGenerator;
        var itemIndex = generator?.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0)) ?? -1;
        if (itemIndex < 0)
        {
            return Rect.Empty;
        }

        var row = itemIndex / _columns;
        var top = VerticalPadding + (row * (ItemHeight + Gap));
        var bottom = top + ItemHeight;

        if (top < VerticalOffset)
        {
            SetVerticalOffset(top);
        }
        else if (bottom > VerticalOffset + ViewportHeight)
        {
            SetVerticalOffset(bottom - ViewportHeight);
        }

        var x = _gridLeft + ((itemIndex % _columns) * (ItemWidth + Gap));
        var y = top - VerticalOffset;
        return new Rect(x, y, ItemWidth, ItemHeight);
    }
}
