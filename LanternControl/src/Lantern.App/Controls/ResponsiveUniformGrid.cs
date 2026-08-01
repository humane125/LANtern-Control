using System.Windows;
using System.Windows.Controls.Primitives;

namespace Lantern.App.Controls;

public sealed class ResponsiveUniformGrid : UniformGrid
{
    public static readonly DependencyProperty BreakpointProperty =
        DependencyProperty.Register(
            nameof(Breakpoint),
            typeof(double),
            typeof(ResponsiveUniformGrid),
            new FrameworkPropertyMetadata(900D, OnLayoutPropertyChanged));

    public static readonly DependencyProperty WideColumnsProperty =
        DependencyProperty.Register(
            nameof(WideColumns),
            typeof(int),
            typeof(ResponsiveUniformGrid),
            new FrameworkPropertyMetadata(4, OnLayoutPropertyChanged));

    public double Breakpoint
    {
        get => (double)GetValue(BreakpointProperty);
        set => SetValue(BreakpointProperty, value);
    }

    public int WideColumns
    {
        get => (int)GetValue(WideColumnsProperty);
        set => SetValue(WideColumnsProperty, value);
    }

    public static int GetColumnCount(double width, double breakpoint, int wideColumns)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wideColumns);
        return width < breakpoint ? Math.Min(2, wideColumns) : wideColumns;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var width = double.IsFinite(constraint.Width) ? constraint.Width : ActualWidth;
        Columns = GetColumnCount(width, Breakpoint, WideColumns);
        return base.MeasureOverride(constraint);
    }

    private static void OnLayoutPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs) =>
        ((ResponsiveUniformGrid)dependencyObject).InvalidateMeasure();
}
