using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Lantern.App.Controls;
using Lantern.App.ViewModels;

namespace Lantern.Linux.Controls;

public sealed class TrafficChart : Control
{
    private const double LeftPadding = 54;
    private const double TopPadding = 18;
    private const double RightPadding = 12;
    private const double BottomPadding = 28;
    private static readonly IBrush SecondaryText = new SolidColorBrush(Color.Parse("#A89095"));
    private static readonly IBrush BorderBrush = new SolidColorBrush(Color.Parse("#2B2226"));
    private static readonly IBrush DownloadBrush = new SolidColorBrush(Color.Parse("#D72C43"));
    private static readonly IBrush UploadBrush = new SolidColorBrush(Color.Parse("#9B6670"));
    private static readonly IBrush PrimaryText = new SolidColorBrush(Color.Parse("#F4EEF0"));
    private static readonly IBrush CardBrush = new SolidColorBrush(Color.Parse("#171317"));
    private static readonly Pen GridPen = new(BorderBrush, 1);
    private static readonly Pen DownloadPen = new(DownloadBrush, 2);
    private static readonly Pen UploadPen = new(UploadBrush, 2);
    private IReadOnlyList<TrafficSample> samples = [];
    private int selectedIndex = -1;

    public TrafficChart()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        PointerMoved += OnPointerMoved;
        PointerExited += OnPointerExited;
        KeyDown += OnKeyDown;
    }

    public void SetSamples(IReadOnlyList<TrafficSample> value)
    {
        samples = value ?? [];
        if (selectedIndex >= samples.Count)
        {
            selectedIndex = -1;
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        var plotWidth = Math.Max(0, Bounds.Width - LeftPadding - RightPadding);
        var plotHeight = Math.Max(0, Bounds.Height - TopPadding - BottomPadding);
        if (plotWidth <= 0 || plotHeight <= 0)
        {
            return;
        }

        var window = samples.Count == 0
            ? (TrafficChartWindow?)null
            : TrafficChartScale.GetWindow(samples, TimeSpan.FromMinutes(10));
        DrawGrid(context, plotWidth, plotHeight, window?.Start, window?.End);
        if (samples.Count == 0)
        {
            DrawText(
                context,
                "Traffic appears after control starts",
                new Point(LeftPadding + 16, TopPadding + (plotHeight / 2) - 8),
                SecondaryText,
                13);
            return;
        }

        var start = window!.Value.Start;
        var end = window.Value.End;
        var maximum = TrafficChartScale.GetMaximum(samples);
        var renderSamples = TrafficChartScale.GetRenderSamples(
            samples,
            Math.Max(2, (int)(plotWidth / 3)));
        DrawSeries(context, renderSamples, sample => sample.DownloadBytesPerSecond,
            start, end, maximum, plotWidth, plotHeight, DownloadPen, DownloadBrush);
        DrawSeries(context, renderSamples, sample => sample.UploadBytesPerSecond,
            start, end, maximum, plotWidth, plotHeight, UploadPen, UploadBrush);
        DrawSelection(context, start, end, maximum, plotWidth, plotHeight);
    }

    private static void DrawGrid(
        DrawingContext context,
        double plotWidth,
        double plotHeight,
        DateTimeOffset? start,
        DateTimeOffset? end)
    {
        for (var line = 0; line <= 4; line++)
        {
            var y = TopPadding + ((plotHeight / 4) * line);
            context.DrawLine(GridPen, new Point(LeftPadding, y), new Point(LeftPadding + plotWidth, y));
        }

        if (start is not { } startTime || end is not { } endTime)
        {
            return;
        }

        DrawText(context, startTime.LocalDateTime.ToString("h:mm:ss", CultureInfo.CurrentCulture),
            new Point(LeftPadding, TopPadding + plotHeight + 8), SecondaryText, 11);
        var endText = CreateText(endTime.LocalDateTime.ToString("h:mm:ss", CultureInfo.CurrentCulture), SecondaryText, 11);
        context.DrawText(endText, new Point(LeftPadding + plotWidth - endText.Width, TopPadding + plotHeight + 8));
    }

    private static void DrawSeries(
        DrawingContext context,
        IReadOnlyList<TrafficSample> renderSamples,
        Func<TrafficSample, double> selector,
        DateTimeOffset start,
        DateTimeOffset end,
        double maximum,
        double plotWidth,
        double plotHeight,
        Pen pen,
        IBrush brush)
    {
        if (renderSamples.Count == 0)
        {
            return;
        }

        if (renderSamples.Count > 1)
        {
            for (var index = 1; index < renderSamples.Count; index++)
            {
                context.DrawLine(
                    pen,
                    GetPoint(renderSamples[index - 1], selector, start, end, maximum, plotWidth, plotHeight),
                    GetPoint(renderSamples[index], selector, start, end, maximum, plotWidth, plotHeight));
            }
        }

        var latest = renderSamples[^1];
        var point = GetPoint(latest, selector, start, end, maximum, plotWidth, plotHeight);
        context.DrawEllipse(brush, new Pen(new SolidColorBrush(Color.Parse("#08090B")), 1), point, 4, 4);
    }

    private void DrawSelection(
        DrawingContext context,
        DateTimeOffset start,
        DateTimeOffset end,
        double maximum,
        double plotWidth,
        double plotHeight)
    {
        if (selectedIndex < 0 || selectedIndex >= samples.Count)
        {
            return;
        }

        var sample = samples[selectedIndex];
        var x = LeftPadding + TrafficChartScale.GetX(sample.Timestamp, start, end, plotWidth);
        context.DrawLine(
            new Pen(SecondaryText, 1, dashStyle: new DashStyle([4, 4], 0)),
            new Point(x, TopPadding),
            new Point(x, TopPadding + plotHeight));
        DrawSelectedPoint(context, x, sample.DownloadBytesPerSecond, maximum, plotHeight, DownloadBrush);
        DrawSelectedPoint(context, x, sample.UploadBytesPerSecond, maximum, plotHeight, UploadBrush);
        DrawHoverCard(context, sample, x);
    }

    private void DrawHoverCard(DrawingContext context, TrafficSample sample, double sampleX)
    {
        var text = CreateText(TrafficChartPresentation.BuildHoverText(sample), PrimaryText, 11);
        var cardWidth = Math.Max(190, text.Width + 24);
        var cardHeight = text.Height + 18;
        var left = sampleX + 12;
        if (left + cardWidth > Bounds.Width - RightPadding)
        {
            left = sampleX - cardWidth - 12;
        }

        left = Math.Clamp(left, LeftPadding, Math.Max(LeftPadding, Bounds.Width - cardWidth - RightPadding));
        var card = new RoundedRect(new Rect(left, TopPadding + 8, cardWidth, cardHeight), 7);
        context.DrawRectangle(CardBrush, new Pen(BorderBrush, 1), card);
        context.DrawText(text, new Point(left + 12, TopPadding + 17));
    }

    private static Point GetPoint(
        TrafficSample sample,
        Func<TrafficSample, double> selector,
        DateTimeOffset start,
        DateTimeOffset end,
        double maximum,
        double plotWidth,
        double plotHeight) =>
        new(
            LeftPadding + TrafficChartScale.GetX(sample.Timestamp, start, end, plotWidth),
            TopPadding + plotHeight - (Math.Clamp(selector(sample), 0, maximum) / maximum * plotHeight));

    private static void DrawSelectedPoint(
        DrawingContext context,
        double x,
        double value,
        double maximum,
        double plotHeight,
        IBrush brush)
    {
        var y = TopPadding + plotHeight - (Math.Clamp(value, 0, maximum) / maximum * plotHeight);
        context.DrawEllipse(brush, new Pen(new SolidColorBrush(Color.Parse("#08090B")), 1), new Point(x, y), 4, 4);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (samples.Count == 0)
        {
            return;
        }

        var plotWidth = Math.Max(1, Bounds.Width - LeftPadding - RightPadding);
        var window = TrafficChartScale.GetWindow(samples, TimeSpan.FromMinutes(10));
        selectedIndex = TrafficChartScale.GetNearestSampleIndex(
            samples,
            eventArgs.GetPosition(this).X - LeftPadding,
            window.Start,
            window.End,
            plotWidth);
        InvalidateVisual();
    }

    private void OnPointerExited(object? sender, PointerEventArgs eventArgs)
    {
        if (!IsKeyboardFocusWithin)
        {
            selectedIndex = -1;
            InvalidateVisual();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (samples.Count == 0 || eventArgs.Key is not (Key.Left or Key.Right))
        {
            return;
        }

        selectedIndex = selectedIndex < 0
            ? samples.Count - 1
            : Math.Clamp(selectedIndex + (eventArgs.Key == Key.Left ? -1 : 1), 0, samples.Count - 1);
        InvalidateVisual();
        eventArgs.Handled = true;
    }

    private static void DrawText(
        DrawingContext context,
        string text,
        Point point,
        IBrush brush,
        double size) => context.DrawText(CreateText(text, brush, size), point);

    private static FormattedText CreateText(string text, IBrush brush, double size) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Inter"), size, brush);
}
