using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Lantern.App.ViewModels;

namespace Lantern.App.Controls;

public sealed class LiveTrafficChart : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty =
        DependencyProperty.Register(
            nameof(Samples),
            typeof(IReadOnlyList<TrafficSample>),
            typeof(LiveTrafficChart),
            new FrameworkPropertyMetadata(
                Array.Empty<TrafficSample>(),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnSamplesChanged));

    public static readonly DependencyProperty VisibleDurationProperty =
        DependencyProperty.Register(
            nameof(VisibleDuration),
            typeof(TimeSpan),
            typeof(LiveTrafficChart),
            new FrameworkPropertyMetadata(
                TimeSpan.FromHours(1),
                FrameworkPropertyMetadataOptions.AffectsRender));

    private const double LeftPadding = 54;
    private const double TopPadding = 18;
    private const double RightPadding = 12;
    private const double BottomPadding = 28;
    private int selectedIndex = -1;

    public LiveTrafficChart()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
        SnapsToDevicePixels = true;
    }

    public IReadOnlyList<TrafficSample> Samples
    {
        get => (IReadOnlyList<TrafficSample>)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public TimeSpan VisibleDuration
    {
        get => (TimeSpan)GetValue(VisibleDurationProperty);
        set => SetValue(VisibleDurationProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var plotWidth = Math.Max(0, ActualWidth - LeftPadding - RightPadding);
        var plotHeight = Math.Max(0, ActualHeight - TopPadding - BottomPadding);
        if (plotWidth <= 0 || plotHeight <= 0)
        {
            return;
        }

        var secondaryText = GetBrush("SecondaryText", Color.FromRgb(168, 144, 149));
        var border = GetBrush("Border", Color.FromRgb(43, 34, 38));
        var download = GetBrush("DownloadAccent", Color.FromRgb(215, 44, 67));
        var upload = GetBrush("UploadAccent", Color.FromRgb(155, 102, 112));
        var samples = Samples ?? Array.Empty<TrafficSample>();
        if (samples.Count == 0)
        {
            DrawGrid(
                drawingContext,
                plotWidth,
                plotHeight,
                border,
                secondaryText,
                null,
                null);
            DrawText(
                drawingContext,
                "Traffic appears after control starts",
                new Point(LeftPadding + 16, TopPadding + (plotHeight / 2) - 8),
                secondaryText,
                13);
            return;
        }

        var window = TrafficChartScale.GetWindow(samples, GetVisibleDuration());
        var start = window.Start;
        var end = window.End;
        var renderSamples = TrafficChartScale.GetRenderSamples(
            samples,
            Math.Max(2, (int)(plotWidth / 3)));
        DrawGrid(
            drawingContext,
            plotWidth,
            plotHeight,
            border,
            secondaryText,
            start,
            end);
        var maximum = TrafficChartScale.GetMaximum(samples);
        DrawSeries(
            drawingContext,
            renderSamples,
            sample => sample.DownloadBytesPerSecond,
            start,
            end,
            maximum,
            plotWidth,
            plotHeight,
            download);
        DrawSeries(
            drawingContext,
            renderSamples,
            sample => sample.UploadBytesPerSecond,
            start,
            end,
            maximum,
            plotWidth,
            plotHeight,
            upload);
        DrawSelection(
            drawingContext,
            samples,
            start,
            end,
            maximum,
            plotWidth,
            plotHeight,
            download,
            upload);
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        base.OnMouseMove(eventArgs);
        SelectNearest(eventArgs.GetPosition(this).X);
    }

    protected override void OnMouseLeave(MouseEventArgs eventArgs)
    {
        base.OnMouseLeave(eventArgs);
        if (!IsKeyboardFocusWithin)
        {
            selectedIndex = -1;
            ToolTip = null;
            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        var samples = Samples ?? Array.Empty<TrafficSample>();
        if (samples.Count == 0 || eventArgs.Key is not (Key.Left or Key.Right))
        {
            base.OnKeyDown(eventArgs);
            return;
        }

        selectedIndex = selectedIndex < 0
            ? samples.Count - 1
            : Math.Clamp(
                selectedIndex + (eventArgs.Key == Key.Left ? -1 : 1),
                0,
                samples.Count - 1);
        UpdateToolTip(samples[selectedIndex]);
        InvalidateVisual();
        eventArgs.Handled = true;
    }

    private static void OnSamplesChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        var chart = (LiveTrafficChart)dependencyObject;
        var samples = eventArgs.NewValue as IReadOnlyList<TrafficSample>;
        if (samples is null || chart.selectedIndex >= samples.Count)
        {
            chart.selectedIndex = -1;
            chart.ToolTip = null;
        }
    }

    private void SelectNearest(double mouseX)
    {
        var samples = Samples ?? Array.Empty<TrafficSample>();
        if (samples.Count == 0)
        {
            return;
        }

        var plotWidth = Math.Max(1, ActualWidth - LeftPadding - RightPadding);
        var window = TrafficChartScale.GetWindow(samples, GetVisibleDuration());
        var start = window.Start;
        var end = window.End;
        selectedIndex = TrafficChartScale.GetNearestSampleIndex(
            samples,
            mouseX - LeftPadding,
            start,
            end,
            plotWidth);
        UpdateToolTip(samples[selectedIndex]);
        InvalidateVisual();
    }

    private TimeSpan GetVisibleDuration() =>
        VisibleDuration > TimeSpan.Zero
            ? VisibleDuration
            : TimeSpan.FromHours(1);

    private void UpdateToolTip(TrafficSample sample)
    {
        ToolTip = TrafficChartPresentation.BuildHoverText(sample);
    }

    private static void DrawGrid(
        DrawingContext drawingContext,
        double plotWidth,
        double plotHeight,
        Brush border,
        Brush secondaryText,
        DateTimeOffset? start,
        DateTimeOffset? end)
    {
        var pen = new Pen(border, 1);
        pen.Freeze();
        for (var line = 0; line <= 4; line++)
        {
            var y = TopPadding + ((plotHeight / 4) * line);
            drawingContext.DrawLine(
                pen,
                new Point(LeftPadding, y),
                new Point(LeftPadding + plotWidth, y));
        }

        if (start is { } startTime && end is { } endTime)
        {
            DrawText(
                drawingContext,
                startTime.LocalDateTime.ToString("h:mm:ss", CultureInfo.CurrentCulture),
                new Point(LeftPadding, TopPadding + plotHeight + 8),
                secondaryText,
                11);
            var endText = endTime.LocalDateTime.ToString("h:mm:ss", CultureInfo.CurrentCulture);
            var formatted = CreateText(endText, secondaryText, 11);
            drawingContext.DrawText(
                formatted,
                new Point(LeftPadding + plotWidth - formatted.Width, TopPadding + plotHeight + 8));
        }
    }

    private static void DrawSeries(
        DrawingContext drawingContext,
        IReadOnlyList<TrafficSample> samples,
        Func<TrafficSample, double> selector,
        DateTimeOffset start,
        DateTimeOffset end,
        double maximum,
        double plotWidth,
        double plotHeight,
        Brush brush)
    {
        if (samples.Count == 0)
        {
            return;
        }

        if (samples.Count == 1)
        {
            var only = samples[0];
            DrawPoint(
                drawingContext,
                LeftPadding + TrafficChartScale.GetX(only.Timestamp, start, end, plotWidth),
                selector(only),
                maximum,
                plotHeight,
                brush);
            return;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var index = 0; index < samples.Count; index++)
            {
                var sample = samples[index];
                var point = new Point(
                    LeftPadding + TrafficChartScale.GetX(
                        sample.Timestamp,
                        start,
                        end,
                        plotWidth),
                    TopPadding + plotHeight -
                    (Math.Clamp(selector(sample), 0, maximum) / maximum * plotHeight));
                if (index == 0)
                {
                    context.BeginFigure(point, false, false);
                }
                else
                {
                    context.LineTo(point, true, false);
                }
            }
        }

        geometry.Freeze();
        var pen = new Pen(brush, 2);
        pen.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
        var latest = samples[^1];
        DrawPoint(
            drawingContext,
            LeftPadding + TrafficChartScale.GetX(latest.Timestamp, start, end, plotWidth),
            selector(latest),
            maximum,
            plotHeight,
            brush);
    }

    private void DrawSelection(
        DrawingContext drawingContext,
        IReadOnlyList<TrafficSample> samples,
        DateTimeOffset start,
        DateTimeOffset end,
        double maximum,
        double plotWidth,
        double plotHeight,
        Brush download,
        Brush upload)
    {
        if (selectedIndex < 0 || selectedIndex >= samples.Count)
        {
            return;
        }

        var sample = samples[selectedIndex];
        var x = LeftPadding + TrafficChartScale.GetX(
            sample.Timestamp,
            start,
            end,
            plotWidth);
        var guide = new Pen(GetBrush("SecondaryText", Color.FromRgb(168, 144, 149)), 1)
        {
            DashStyle = DashStyles.Dash,
        };
        drawingContext.DrawLine(
            guide,
            new Point(x, TopPadding),
            new Point(x, TopPadding + plotHeight));
        DrawPoint(drawingContext, x, sample.DownloadBytesPerSecond, maximum, plotHeight, download);
        DrawPoint(drawingContext, x, sample.UploadBytesPerSecond, maximum, plotHeight, upload);
        DrawHoverCard(drawingContext, sample, x);
    }

    private void DrawHoverCard(
        DrawingContext drawingContext,
        TrafficSample sample,
        double sampleX)
    {
        var content = TrafficChartPresentation.BuildHoverText(sample);
        var text = CreateText(
            content,
            GetBrush("PrimaryText", Color.FromRgb(244, 238, 240)),
            11);
        var cardWidth = Math.Max(190, text.Width + 24);
        var cardHeight = text.Height + 18;
        var left = sampleX + 12;
        if (left + cardWidth > ActualWidth - RightPadding)
        {
            left = sampleX - cardWidth - 12;
        }

        left = Math.Clamp(left, LeftPadding, Math.Max(LeftPadding, ActualWidth - cardWidth - RightPadding));
        var card = new Rect(left, TopPadding + 8, cardWidth, cardHeight);
        drawingContext.DrawRoundedRectangle(
            GetBrush("SurfaceRaised", Color.FromRgb(23, 19, 23)),
            new Pen(GetBrush("Border", Color.FromRgb(43, 34, 38)), 1),
            card,
            7,
            7);
        drawingContext.DrawText(text, new Point(card.Left + 12, card.Top + 9));
    }

    private static void DrawPoint(
        DrawingContext drawingContext,
        double x,
        double value,
        double maximum,
        double plotHeight,
        Brush brush)
    {
        var y = TopPadding + plotHeight -
                (Math.Clamp(value, 0, maximum) / maximum * plotHeight);
        drawingContext.DrawEllipse(
            brush,
            new Pen(GetBrush("WindowBackground", Color.FromRgb(8, 9, 11)), 1),
            new Point(x, y),
            4,
            4);
    }

    private static Brush GetBrush(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    private static void DrawText(
        DrawingContext drawingContext,
        string text,
        Point point,
        Brush brush,
        double size) =>
        drawingContext.DrawText(CreateText(text, brush, size), point);

    private static FormattedText CreateText(string text, Brush brush, double size) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            brush,
            Application.Current?.MainWindow is Visual visual
                ? VisualTreeHelper.GetDpi(visual).PixelsPerDip
                : 1D);
}
