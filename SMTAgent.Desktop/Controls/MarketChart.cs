using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SMTAgent.Core.Models;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;

namespace SMTAgent.Desktop.Controls;

public sealed class MarketChart : FrameworkElement
{
    public static readonly DependencyProperty SymbolProperty =
        DependencyProperty.Register(nameof(Symbol), typeof(string), typeof(MarketChart), new FrameworkPropertyMetadata("ES", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CandlesProperty =
        DependencyProperty.Register(nameof(Candles), typeof(IEnumerable), typeof(MarketChart), new FrameworkPropertyMetadata(null, OnCollectionPropertyChanged));

    public static readonly DependencyProperty SwingPointsProperty =
        DependencyProperty.Register(nameof(SwingPoints), typeof(IEnumerable), typeof(MarketChart), new FrameworkPropertyMetadata(null, OnCollectionPropertyChanged));

    public static readonly DependencyProperty SignalsProperty =
        DependencyProperty.Register(nameof(Signals), typeof(IEnumerable), typeof(MarketChart), new FrameworkPropertyMetadata(null, OnCollectionPropertyChanged));

    public static readonly DependencyProperty FocusedAnnotationsProperty =
        DependencyProperty.Register(nameof(FocusedAnnotations), typeof(IEnumerable), typeof(MarketChart), new FrameworkPropertyMetadata(null, OnCollectionPropertyChanged));

    public static readonly DependencyProperty ViewStartIndexProperty =
        DependencyProperty.Register(nameof(ViewStartIndex), typeof(int), typeof(MarketChart), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty VisibleCandleCountProperty =
        DependencyProperty.Register(nameof(VisibleCandleCount), typeof(int), typeof(MarketChart), new FrameworkPropertyMetadata(90, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AutoScrollToLatestProperty =
        DependencyProperty.Register(nameof(AutoScrollToLatest), typeof(bool), typeof(MarketChart), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CrosshairTimeProperty =
        DependencyProperty.Register(nameof(CrosshairTime), typeof(DateTime?), typeof(MarketChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly Pen _gridPen = new(new SolidColorBrush(Color.FromRgb(222, 230, 244)), 1);
    private readonly Pen _minorGridPen = new(new SolidColorBrush(Color.FromRgb(235, 240, 249)), 1);
    private readonly Pen _wickPen = new(new SolidColorBrush(Color.FromRgb(55, 65, 81)), 1);
    private readonly Brush _bullBrush = new SolidColorBrush(Color.FromRgb(0, 145, 234));
    private readonly Brush _bearBrush = new SolidColorBrush(Color.FromRgb(47, 52, 58));
    private readonly Brush _mutedTextBrush = new SolidColorBrush(Color.FromRgb(31, 41, 55));
    private readonly Brush _softTextBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));
    private readonly Brush _panelBrush = new SolidColorBrush(Color.FromRgb(238, 244, 255));
    private readonly Brush _tooltipBrush = new SolidColorBrush(Color.FromArgb(242, 255, 255, 255));
    private readonly Brush _swingHighBrush = new SolidColorBrush(Color.FromRgb(47, 107, 255));
    private readonly Brush _swingLowBrush = new SolidColorBrush(Color.FromRgb(47, 107, 255));
    private readonly Brush _smtDotBrush = new SolidColorBrush(Color.FromRgb(255, 211, 0));
    private readonly Pen _smtDotPen = new(new SolidColorBrush(Color.FromRgb(17, 24, 39)), 1.4);
    private readonly Pen _bearSignalPen = new(new SolidColorBrush(Color.FromRgb(242, 54, 69)), 2.2);
    private readonly Pen _bullSignalPen = new(new SolidColorBrush(Color.FromRgb(47, 107, 255)), 2.2);
    private readonly Pen _crosshairPen = new(new SolidColorBrush(Color.FromArgb(150, 86, 99, 120)), 1);
    private readonly Pen _bosPen = new(new SolidColorBrush(Color.FromRgb(37, 99, 235)), 2);
    private readonly Brush _bullZoneFillBrush = new SolidColorBrush(Color.FromArgb(58, 14, 165, 233));
    private readonly Brush _bearZoneFillBrush = new SolidColorBrush(Color.FromArgb(58, 47, 52, 58));
    private readonly Brush _ifvgBullFillBrush = new SolidColorBrush(Color.FromArgb(50, 14, 165, 233));
    private readonly Brush _ifvgBearFillBrush = new SolidColorBrush(Color.FromArgb(54, 47, 52, 58));
    private readonly Pen _ifvgPen = new(new SolidColorBrush(Color.FromRgb(17, 24, 39)), 1.8);
    private readonly Pen _halfBoxPen = new(new SolidColorBrush(Color.FromRgb(17, 24, 39)), 1.8);
    private readonly Pen _entryPen = new(new SolidColorBrush(Color.FromRgb(37, 99, 235)), 1.6);
    private readonly Brush _riskBrush = new SolidColorBrush(Color.FromArgb(48, 242, 54, 69));
    private readonly Brush _rewardBrush = new SolidColorBrush(Color.FromArgb(56, 14, 165, 233));
    private Point? _dragStartPoint;
    private int _dragStartIndex;

    public MarketChart()
    {
        Focusable = true;
        ClipToBounds = true;

        var refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        refreshTimer.Tick += (_, _) => InvalidateVisual();
        refreshTimer.Start();
    }

    public string Symbol
    {
        get => (string)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public IEnumerable? Candles
    {
        get => (IEnumerable?)GetValue(CandlesProperty);
        set => SetValue(CandlesProperty, value);
    }

    public IEnumerable? SwingPoints
    {
        get => (IEnumerable?)GetValue(SwingPointsProperty);
        set => SetValue(SwingPointsProperty, value);
    }

    public IEnumerable? Signals
    {
        get => (IEnumerable?)GetValue(SignalsProperty);
        set => SetValue(SignalsProperty, value);
    }

    public IEnumerable? FocusedAnnotations
    {
        get => (IEnumerable?)GetValue(FocusedAnnotationsProperty);
        set => SetValue(FocusedAnnotationsProperty, value);
    }

    public int ViewStartIndex
    {
        get => (int)GetValue(ViewStartIndexProperty);
        set => SetValue(ViewStartIndexProperty, value);
    }

    public int VisibleCandleCount
    {
        get => (int)GetValue(VisibleCandleCountProperty);
        set => SetValue(VisibleCandleCountProperty, value);
    }

    public bool AutoScrollToLatest
    {
        get => (bool)GetValue(AutoScrollToLatestProperty);
        set => SetValue(AutoScrollToLatestProperty, value);
    }

    public DateTime? CrosshairTime
    {
        get => (DateTime?)GetValue(CrosshairTimeProperty);
        set => SetValue(CrosshairTimeProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        drawingContext.DrawRectangle(_panelBrush, null, new Rect(0, 0, width, height));

        var candles = ToList<Candle>(Candles);
        if (candles.Count == 0)
        {
            DrawEmptyState(drawingContext, width, height);
            return;
        }

        var plot = GetPlot(width, height);
        EnsureViewport(candles.Count);
        var start = Math.Clamp(ViewStartIndex, 0, Math.Max(0, candles.Count - 1));
        var visibleCount = NormalizeVisible(VisibleCandleCount, candles.Count);
        start = Math.Clamp(start, 0, Math.Max(0, candles.Count - visibleCount));
        var visibleCandles = candles.Skip(start).Take(visibleCount).ToList();
        if (visibleCandles.Count == 0)
        {
            DrawEmptyState(drawingContext, width, height);
            return;
        }

        var focusedAnnotations = ToList<FocusedChartAnnotation>(FocusedAnnotations);
        var min = visibleCandles.Min(candle => candle.Low);
        var max = visibleCandles.Max(candle => candle.High);
        foreach (var annotationPrice in focusedAnnotations
            .SelectMany(annotation => new[] { (decimal?)annotation.Price, annotation.SecondaryPrice, annotation.TertiaryPrice })
            .Where(price => price.HasValue)
            .Select(price => price!.Value))
        {
            min = Math.Min(min, annotationPrice);
            max = Math.Max(max, annotationPrice);
        }
        var pad = Math.Max((max - min) * 0.08m, 1m);
        min -= pad;
        max += pad;

        var pricePlot = new Rect(plot.Left, plot.Top, plot.Width, plot.Height * 0.78);
        var volumePlot = new Rect(plot.Left, plot.Top + (plot.Height * 0.80), plot.Width, plot.Height * 0.20);

        DrawGrid(drawingContext, pricePlot, min, max);
        DrawFvgSignals(drawingContext, ToList<SmtSignal>(Signals), candles, start, visibleCount, pricePlot, min, max);
        DrawCandles(drawingContext, candles, start, visibleCount, pricePlot, min, max);
        DrawLastPriceLine(drawingContext, visibleCandles[^1], pricePlot, min, max);
        DrawFocusedAnnotations(drawingContext, focusedAnnotations, candles, start, visibleCount, pricePlot, min, max);
        DrawHighLowSignals(drawingContext, ToList<SmtSignal>(Signals), candles, start, visibleCount, pricePlot, min, max);
        DrawVolume(drawingContext, candles, start, visibleCount, volumePlot);
        DrawTimeScale(drawingContext, candles, start, visibleCount, plot);
        DrawCrosshair(drawingContext, candles, start, visibleCount, pricePlot, min, max);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var candles = ToList<Candle>(Candles);
        if (candles.Count == 0)
        {
            return;
        }

        var plot = GetPlot(ActualWidth, ActualHeight);
        var position = e.GetPosition(this);
        var oldVisible = NormalizeVisible(VisibleCandleCount, candles.Count);
        var oldStart = Math.Clamp(ViewStartIndex, 0, Math.Max(0, candles.Count - oldVisible));
        var cursorRatio = Math.Clamp((position.X - plot.Left) / Math.Max(plot.Width, 1), 0, 1);
        var cursorIndex = oldStart + (cursorRatio * oldVisible);
        var zoomFactor = e.Delta > 0 ? 0.82 : 1.18;
        var newVisible = NormalizeVisible((int)Math.Round(oldVisible * zoomFactor), candles.Count);
        var newStart = (int)Math.Round(cursorIndex - ((cursorIndex - oldStart) * newVisible / oldVisible));

        SetBoundValue(AutoScrollToLatestProperty, false);
        SetViewport(newStart, newVisible, candles.Count);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        CaptureMouse();
        _dragStartPoint = e.GetPosition(this);
        _dragStartIndex = ViewStartIndex;
        Cursor = Cursors.SizeWE;
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
        _dragStartPoint = null;
        Cursor = Cursors.Cross;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var candles = ToList<Candle>(Candles);
        if (candles.Count == 0)
        {
            return;
        }

        var position = e.GetPosition(this);
        var plot = GetPlot(ActualWidth, ActualHeight);
        UpdateCrosshair(candles, position, plot);

        if (_dragStartPoint is null || e.LeftButton != MouseButtonState.Pressed)
        {
            Cursor = Cursors.Cross;
            return;
        }

        var visible = NormalizeVisible(VisibleCandleCount, candles.Count);
        var deltaX = position.X - _dragStartPoint.Value.X;
        var candlesPerPixel = visible / Math.Max(plot.Width, 1);
        var newStart = (int)Math.Round(_dragStartIndex - (deltaX * candlesPerPixel));
            SetBoundValue(AutoScrollToLatestProperty, false);
        SetViewport(newStart, visible, candles.Count);
        Cursor = Cursors.SizeWE;
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!IsMouseCaptured)
        {
            SetCurrentValue(CrosshairTimeProperty, null);
        }
    }

    private void DrawEmptyState(DrawingContext context, double width, double height)
    {
        var text = BuildText("Waiting for synchronized market data...", 13, _softTextBrush, FontWeights.Medium);
        context.DrawText(text, new Point((width - text.Width) / 2, (height - text.Height) / 2));
    }

    private void DrawGrid(DrawingContext context, Rect plot, decimal min, decimal max)
    {
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Top + (plot.Height / 4 * i);
            context.DrawLine(_gridPen, new Point(plot.Left, y), new Point(plot.Right, y));

            var price = max - ((max - min) / 4 * i);
            var label = BuildText(price.ToString("N2"), 12, _mutedTextBrush, FontWeights.SemiBold);
            context.DrawText(label, new Point(plot.Right + 8, y - 8));
        }

        for (var i = 0; i <= 5; i++)
        {
            var x = plot.Left + (plot.Width / 5 * i);
            context.DrawLine(_minorGridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
    }

    private void DrawCandles(DrawingContext context, IReadOnlyList<Candle> candles, int start, int visibleCount, Rect plot, decimal min, decimal max)
    {
        var step = plot.Width / Math.Max(visibleCount, 1);
        var bodyWidth = Math.Clamp(step * 0.58, 3, 12);
        var end = Math.Min(candles.Count, start + visibleCount);

        for (var index = start; index < end; index++)
        {
            var candle = candles[index];
            var x = XForIndex(index, start, step, plot);
            var highY = Scale(candle.High, min, max, plot);
            var lowY = Scale(candle.Low, min, max, plot);
            var openY = Scale(candle.Open, min, max, plot);
            var closeY = Scale(candle.Close, min, max, plot);
            var up = candle.Close >= candle.Open;
            var bodyBrush = up ? _bullBrush : _bearBrush;

            var wickPen = new Pen(up ? bodyBrush : _wickPen.Brush, 1);
            context.DrawLine(wickPen, new Point(x, highY), new Point(x, lowY));
            var bodyTop = Math.Min(openY, closeY);
            var bodyHeight = Math.Max(2, Math.Abs(openY - closeY));
            var body = new Rect(x - bodyWidth / 2, bodyTop, bodyWidth, bodyHeight);
            context.DrawRectangle(bodyBrush, null, body);

            if (index == candles.Count - 1)
            {
                var pulse = 0.45 + (0.35 * Math.Abs(Math.Sin(DateTime.Now.TimeOfDay.TotalSeconds * 2)));
                var alpha = (byte)(90 + (95 * pulse));
                var pulsePen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 255, 216, 138)), 1.4);
                var pulseBody = body;
                pulseBody.Inflate(3, 3);
                context.DrawRoundedRectangle(null, pulsePen, pulseBody, 3, 3);
            }
        }
    }

    private void DrawVolume(DrawingContext context, IReadOnlyList<Candle> candles, int start, int visibleCount, Rect plot)
    {
        var end = Math.Min(candles.Count, start + visibleCount);
        if (end <= start)
        {
            return;
        }

        var maxVolume = Math.Max(1, candles.Skip(start).Take(end - start).Max(candle => candle.Volume));
        var step = plot.Width / Math.Max(visibleCount, 1);
        var bodyWidth = Math.Clamp(step * 0.62, 2, 12);
        var label = BuildText("Vol", 12, _mutedTextBrush, FontWeights.SemiBold);
        context.DrawText(label, new Point(plot.Left + 2, plot.Top + 2));

        for (var index = start; index < end; index++)
        {
            var candle = candles[index];
            var x = XForIndex(index, start, step, plot);
            var height = Math.Max(2, (double)candle.Volume / maxVolume * (plot.Height - 18));
            var top = plot.Bottom - height;
            var color = candle.Close >= candle.Open
                ? Color.FromArgb(92, 14, 165, 233)
                : Color.FromArgb(92, 47, 52, 58);
            context.DrawRectangle(new SolidColorBrush(color), null, new Rect(x - bodyWidth / 2, top, bodyWidth, height));
        }
    }

    private void DrawLastPriceLine(DrawingContext context, Candle candle, Rect plot, decimal min, decimal max)
    {
        var up = candle.Close >= candle.Open;
        var color = up ? Color.FromRgb(0, 145, 234) : Color.FromRgb(47, 52, 58);
        var brush = new SolidColorBrush(color);
        var y = Scale(candle.Close, min, max, plot);
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(155, color.R, color.G, color.B)), 1)
        {
            DashStyle = DashStyles.Dot
        };

        context.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
        var priceText = BuildText(candle.Close.ToString("N2"), 12, Brushes.White, FontWeights.Bold);
        var tag = new Rect(plot.Right + 4, y - 12, priceText.Width + 14, 24);
        context.DrawRoundedRectangle(brush, null, tag, 3, 3);
        context.DrawText(priceText, new Point(tag.Left + 7, tag.Top + 4));
    }

    private void DrawHighLowSignals(DrawingContext context, IReadOnlyList<SmtSignal> signals, IReadOnlyList<Candle> candles, int start, int visibleCount, Rect plot, decimal min, decimal max)
    {
        var step = plot.Width / Math.Max(visibleCount, 1);
        var end = start + visibleCount;
        var candlesByTime = candles
            .Select((candle, index) => new { candle.Time, Index = index })
            .ToDictionary(item => item.Time, item => item.Index);

        foreach (var signal in signals.Where(signal => signal.SetupType == SmtSetupType.HighLow && (signal.LeaderSymbol == Symbol || signal.FailedSymbol == Symbol)))
        {
            var previousSwingTime = Symbol == "ES" ? signal.EsPreviousSwingTime : signal.NqPreviousSwingTime;
            if (!candlesByTime.TryGetValue(signal.Time, out var currentIndex) ||
                !candlesByTime.TryGetValue(previousSwingTime, out var previousIndex))
            {
                continue;
            }

            if ((currentIndex < start || currentIndex >= end) && (previousIndex < start || previousIndex >= end))
            {
                continue;
            }

            var previousValue = Symbol == "ES" ? signal.EsPreviousSwingValue : signal.NqPreviousSwingValue;
            var currentValue = Symbol == "ES" ? signal.EsCurrentValue : signal.NqCurrentValue;
            var pen = signal.Type == SmtSignalType.Bearish ? _bearSignalPen : _bullSignalPen;
            var previousX = XForIndex(previousIndex, start, step, plot);
            var currentX = XForIndex(currentIndex, start, step, plot);
            var previousY = Scale(previousValue, min, max, plot);
            var currentY = Scale(currentValue, min, max, plot);

            context.DrawLine(pen, new Point(previousX, previousY), new Point(currentX, currentY));
            DrawSmtDot(context, currentX, currentY);
        }
    }

    private void DrawFvgSignals(DrawingContext context, IReadOnlyList<SmtSignal> signals, IReadOnlyList<Candle> candles, int start, int visibleCount, Rect plot, decimal min, decimal max)
    {
        var step = plot.Width / Math.Max(visibleCount, 1);
        var end = start + visibleCount;
        var candlesByTime = candles
            .Select((candle, index) => new { candle.Time, Index = index })
            .ToDictionary(item => item.Time, item => item.Index);

        foreach (var signal in signals.Where(signal => (signal.SetupType is SmtSetupType.Fvg or SmtSetupType.InvertedFvg) && (signal.LeaderSymbol == Symbol || signal.FailedSymbol == Symbol)))
        {
            var lower = Symbol == "ES" ? signal.EsFvgLower : signal.NqFvgLower;
            var upper = Symbol == "ES" ? signal.EsFvgUpper : signal.NqFvgUpper;
            var startTime = Symbol == "ES" ? signal.EsFvgStartTime : signal.NqFvgStartTime;
            var endTime = Symbol == "ES" ? signal.EsFvgEndTime : signal.NqFvgEndTime;
            if (lower is null || upper is null || startTime is null || endTime is null ||
                !candlesByTime.TryGetValue(startTime.Value, out var fvgStartIndex) ||
                !candlesByTime.TryGetValue(endTime.Value, out var fvgEndIndex) ||
                !candlesByTime.TryGetValue(signal.Time, out var currentIndex))
            {
                continue;
            }

            if (currentIndex < start || fvgStartIndex >= end)
            {
                continue;
            }

            var brush = signal.Type == SmtSignalType.Bearish ? _bearBrush : _bullBrush;
            var border = new Pen(brush, signal.LeaderSymbol == Symbol ? 1.8 : 1.2);
            var fill = signal.Type == SmtSignalType.Bearish
                ? new SolidColorBrush(Color.FromArgb(42, 242, 54, 69))
                : new SolidColorBrush(Color.FromArgb(42, 47, 107, 255));
            var x1 = XForIndex(fvgStartIndex, start, step, plot) - (step / 2);
            var x2 = XForIndex(Math.Max(fvgEndIndex, currentIndex), start, step, plot) + (step / 2);
            var y1 = Scale(upper.Value, min, max, plot);
            var y2 = Scale(lower.Value, min, max, plot);
            var rect = new Rect(new Point(x1, y1), new Point(x2, y2));
            context.DrawRectangle(fill, border, rect);

            var currentValue = Symbol == "ES" ? signal.EsCurrentValue : signal.NqCurrentValue;
            var currentX = XForIndex(currentIndex, start, step, plot);
            var currentY = Scale(currentValue, min, max, plot);
            DrawSmtDot(context, currentX, currentY);
        }
    }

    private void DrawSmtDot(DrawingContext context, double x, double y)
    {
        context.DrawEllipse(_smtDotBrush, _smtDotPen, new Point(x, y), 5.5, 5.5);
    }

    private void DrawFocusedAnnotations(DrawingContext context, IReadOnlyList<FocusedChartAnnotation> annotations, IReadOnlyList<Candle> candles, int start, int visibleCount, Rect plot, decimal min, decimal max)
    {
        if (annotations.Count == 0)
        {
            return;
        }

        var step = plot.Width / Math.Max(visibleCount, 1);
        var visibleStart = candles[start].Time;
        var visibleEnd = candles[Math.Min(candles.Count - 1, start + visibleCount - 1)].Time;

        foreach (var annotation in annotations.Where(annotation => annotation.EndTime >= visibleStart && annotation.StartTime <= visibleEnd))
        {
            switch (annotation.Kind)
            {
                case FocusedAnnotationKind.Bos:
                    DrawBosAnnotation(context, annotation, candles, start, step, plot, min, max);
                    break;
                case FocusedAnnotationKind.Fvg:
                case FocusedAnnotationKind.Ifvg:
                    DrawGapAnnotation(context, annotation, candles, start, step, plot, min, max);
                    break;
                case FocusedAnnotationKind.HalfBox:
                    DrawHalfBoxAnnotation(context, annotation, candles, start, step, plot, min, max);
                    break;
                case FocusedAnnotationKind.StopTakeProfit:
                    DrawStopTakeProfitAnnotation(context, annotation, candles, start, step, plot, min, max);
                    break;
            }
        }
    }

    private void DrawBosAnnotation(DrawingContext context, FocusedChartAnnotation annotation, IReadOnlyList<Candle> candles, int start, double step, Rect plot, decimal min, decimal max)
    {
        if (!TryGetX(annotation.StartTime, candles, start, step, plot, out var x1) ||
            !TryGetX(annotation.EndTime, candles, start, step, plot, out var x2) ||
            annotation.SecondaryPrice is null)
        {
            return;
        }

        var y1 = Scale(annotation.Price, min, max, plot);
        var y2 = Scale(annotation.SecondaryPrice.Value, min, max, plot);
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(17, 24, 39)), 1.6), new Point(x1, y1), new Point(x2, y1));
        context.DrawLine(_bosPen, new Point(x2 - 18, y2), new Point(x2 + 18, y2));
        DrawTextLabel(context, "BOS", x2 + 6, y2 - 24, _bosPen.Brush, FontWeights.Bold);
    }

    private void DrawGapAnnotation(DrawingContext context, FocusedChartAnnotation annotation, IReadOnlyList<Candle> candles, int start, double step, Rect plot, decimal min, decimal max)
    {
        if (!TryGetX(annotation.StartTime, candles, start, step, plot, out var x1) ||
            !TryGetX(annotation.EndTime, candles, start, step, plot, out var x2) ||
            annotation.SecondaryPrice is null)
        {
            return;
        }

        var upper = Math.Max(annotation.Price, annotation.SecondaryPrice.Value);
        var lower = Math.Min(annotation.Price, annotation.SecondaryPrice.Value);
        var rect = new Rect(
            Math.Min(x1, x2) - (step / 2),
            Scale(upper, min, max, plot),
            Math.Max(step, Math.Abs(x2 - x1) + step),
            Math.Max(2, Scale(lower, min, max, plot) - Scale(upper, min, max, plot)));

        var isBearish = annotation.Direction == SmtSignalType.Bearish;
        if (annotation.Kind == FocusedAnnotationKind.Ifvg)
        {
            context.DrawRectangle(isBearish ? _ifvgBearFillBrush : _ifvgBullFillBrush, _ifvgPen, rect);
            DrawTextLabel(context, "IFVG", rect.Left + 6, rect.Top + (rect.Height / 2) - 9, _ifvgPen.Brush, FontWeights.Bold);
            return;
        }

        var pen = isBearish ? _bearSignalPen : _bullSignalPen;
        context.DrawRectangle(isBearish ? _bearZoneFillBrush : _bullZoneFillBrush, new Pen(pen.Brush, 1.2), rect);
        DrawTextLabel(context, "FVG", rect.Left + 6, rect.Top + 4, pen.Brush, FontWeights.Bold);
    }

    private void DrawHalfBoxAnnotation(DrawingContext context, FocusedChartAnnotation annotation, IReadOnlyList<Candle> candles, int start, double step, Rect plot, decimal min, decimal max)
    {
        if (!TryGetX(annotation.StartTime, candles, start, step, plot, out var x1) ||
            !TryGetX(annotation.EndTime, candles, start, step, plot, out var x2) ||
            annotation.SecondaryPrice is null ||
            annotation.TertiaryPrice is null)
        {
            return;
        }

        var high = Math.Max(annotation.Price, annotation.TertiaryPrice.Value);
        var low = Math.Min(annotation.Price, annotation.TertiaryPrice.Value);
        var rect = new Rect(
            Math.Min(x1, x2),
            Scale(high, min, max, plot),
            Math.Max(step * 8, Math.Abs(x2 - x1)),
            Math.Max(2, Scale(low, min, max, plot) - Scale(high, min, max, plot)));

        var fill = annotation.Direction == SmtSignalType.Bearish
            ? new SolidColorBrush(Color.FromArgb(30, 47, 52, 58))
            : new SolidColorBrush(Color.FromArgb(30, 14, 165, 233));
        context.DrawRectangle(fill, _halfBoxPen, rect);
        DrawLevel(context, "0", annotation.Price, rect.Left, rect.Right, plot, min, max, _halfBoxPen, true);
        DrawLevel(context, "0.5", annotation.SecondaryPrice.Value, rect.Left, rect.Right, plot, min, max, _entryPen, true);
        DrawLevel(context, "1", annotation.TertiaryPrice.Value, rect.Left, rect.Right, plot, min, max, _halfBoxPen, true);
    }

    private void DrawStopTakeProfitAnnotation(DrawingContext context, FocusedChartAnnotation annotation, IReadOnlyList<Candle> candles, int start, double step, Rect plot, decimal min, decimal max)
    {
        if (!TryGetX(annotation.StartTime, candles, start, step, plot, out var x1) ||
            !TryGetX(annotation.EndTime, candles, start, step, plot, out var x2) ||
            annotation.SecondaryPrice is null ||
            annotation.TertiaryPrice is null)
        {
            return;
        }

        var entry = annotation.Price;
        var stop = annotation.SecondaryPrice.Value;
        var target = annotation.TertiaryPrice.Value;
        var xLeft = Math.Min(x1, x2);
        var width = Math.Max(step * 4, Math.Abs(x2 - x1));
        DrawTradeZone(context, xLeft, width, entry, stop, plot, min, max, _riskBrush, "SL");
        DrawTradeZone(context, xLeft, width, entry, target, plot, min, max, _rewardBrush, "TP");
        DrawLevel(context, "Entry", entry, xLeft, xLeft + width, plot, min, max, _entryPen);
    }

    private void DrawTradeZone(DrawingContext context, double x, double width, decimal entry, decimal target, Rect plot, decimal min, decimal max, Brush brush, string label)
    {
        var y1 = Scale(entry, min, max, plot);
        var y2 = Scale(target, min, max, plot);
        var rect = new Rect(x, Math.Min(y1, y2), width, Math.Max(2, Math.Abs(y2 - y1)));
        context.DrawRectangle(brush, null, rect);
        DrawSmallLabel(context, label, rect.Right + 5, rect.Top + 3, new SolidColorBrush(Color.FromRgb(31, 41, 55)), Brushes.White);
    }

    private void DrawLevel(DrawingContext context, string label, decimal price, double x1, double x2, Rect plot, decimal min, decimal max, Pen pen, bool drawBothSides = false)
    {
        var y = Scale(price, min, max, plot);
        context.DrawLine(pen, new Point(x1, y), new Point(x2, y));
        DrawTextLabel(context, label, x2 + 5, y - 10, pen.Brush, FontWeights.Bold);
        if (drawBothSides)
        {
            DrawTextLabel(context, label, x1 - 26, y - 10, pen.Brush, FontWeights.Bold);
        }
    }

    private void DrawSmallLabel(DrawingContext context, string label, double x, double y, Brush background, Brush foreground)
    {
        var text = BuildText(label, 10, foreground, FontWeights.Bold);
        var rect = new Rect(x, y, text.Width + 12, text.Height + 6);
        context.DrawRoundedRectangle(background, null, rect, 3, 3);
        context.DrawText(text, new Point(rect.Left + 6, rect.Top + 3));
    }

    private void DrawTextLabel(DrawingContext context, string label, double x, double y, Brush brush, FontWeight weight)
    {
        var text = BuildText(label, 13, brush, weight);
        context.DrawText(text, new Point(x, y));
    }

    private static bool TryGetX(DateTime time, IReadOnlyList<Candle> candles, int start, double step, Rect plot, out double x)
    {
        var index = -1;
        for (var i = 0; i < candles.Count; i++)
        {
            if (candles[i].Time >= time)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            index = candles.Count - 1;
        }

        x = XForIndex(index, start, step, plot);
        return true;
    }

    private void DrawTimeScale(DrawingContext context, IReadOnlyList<Candle> candles, int start, int visibleCount, Rect plot)
    {
        var step = plot.Width / Math.Max(visibleCount, 1);
        var desiredLabels = Math.Min(7, visibleCount);
        var spacing = Math.Max(1, visibleCount / desiredLabels);
        var end = Math.Min(candles.Count, start + visibleCount);

        for (var index = start; index < end; index += spacing)
        {
            var candle = candles[index];
            var x = XForIndex(index, start, step, plot);
            var label = BuildText(candle.Time.ToString("HH:mm"), 12, _mutedTextBrush, FontWeights.SemiBold);
            context.DrawText(label, new Point(Math.Clamp(x - (label.Width / 2), plot.Left, plot.Right - label.Width), plot.Bottom + 9));
        }
    }

    private void DrawCrosshair(DrawingContext context, IReadOnlyList<Candle> candles, int start, int visibleCount, Rect plot, decimal min, decimal max)
    {
        if (CrosshairTime is null)
        {
            return;
        }

        var index = candles.ToList().FindIndex(candle => candle.Time == CrosshairTime.Value);
        if (index < start || index >= start + visibleCount)
        {
            return;
        }

        var candle = candles[index];
        var step = plot.Width / Math.Max(visibleCount, 1);
        var x = XForIndex(index, start, step, plot);
        var y = Scale(candle.Close, min, max, plot);
        context.DrawLine(_crosshairPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        context.DrawLine(_crosshairPen, new Point(plot.Left, y), new Point(plot.Right, y));

        var text = $"{candle.Time:MM-dd HH:mm}  O {candle.Open:0.00}  H {candle.High:0.00}  L {candle.Low:0.00}  C {candle.Close:0.00}";
        var tooltip = BuildText(text, 11, _mutedTextBrush, FontWeights.SemiBold);
        var tooltipWidth = tooltip.Width + 16;
        var tooltipHeight = tooltip.Height + 10;
        var tooltipX = Math.Clamp(x + 10, plot.Left + 4, plot.Right - tooltipWidth - 4);
        var tooltipY = Math.Clamp(y - tooltipHeight - 10, plot.Top + 4, plot.Bottom - tooltipHeight - 4);
        context.DrawRoundedRectangle(_tooltipBrush, new Pen(new SolidColorBrush(Color.FromRgb(205, 214, 226)), 1), new Rect(tooltipX, tooltipY, tooltipWidth, tooltipHeight), 5, 5);
        context.DrawText(tooltip, new Point(tooltipX + 8, tooltipY + 5));

        var priceLabel = BuildText(candle.Close.ToString("N2"), 11, Brushes.White, FontWeights.Bold);
        var priceRect = new Rect(plot.Right + 4, y - 11, priceLabel.Width + 14, 22);
        context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(31, 41, 55)), null, priceRect, 3, 3);
        context.DrawText(priceLabel, new Point(priceRect.Left + 7, priceRect.Top + 3));

        var timeLabel = BuildText(candle.Time.ToString("MM-dd HH:mm"), 11, Brushes.White, FontWeights.Bold);
        var timeRect = new Rect(Math.Clamp(x - ((timeLabel.Width + 16) / 2), plot.Left, plot.Right - timeLabel.Width - 16), plot.Bottom + 6, timeLabel.Width + 16, 22);
        context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(31, 41, 55)), null, timeRect, 3, 3);
        context.DrawText(timeLabel, new Point(timeRect.Left + 8, timeRect.Top + 3));
    }

    private void UpdateCrosshair(IReadOnlyList<Candle> candles, Point position, Rect plot)
    {
        if (!plot.Contains(position))
        {
            SetBoundValue(CrosshairTimeProperty, null);
            return;
        }

        var visible = NormalizeVisible(VisibleCandleCount, candles.Count);
        var start = Math.Clamp(ViewStartIndex, 0, Math.Max(0, candles.Count - visible));
        var ratio = Math.Clamp((position.X - plot.Left) / Math.Max(plot.Width, 1), 0, 0.999);
        var index = Math.Clamp(start + (int)Math.Floor(ratio * visible), 0, candles.Count - 1);
        SetBoundValue(CrosshairTimeProperty, candles[index].Time);
    }

    private void EnsureViewport(int candleCount)
    {
        if (candleCount <= 0)
        {
            return;
        }

        var visible = NormalizeVisible(VisibleCandleCount, candleCount);
        var start = AutoScrollToLatest ? Math.Max(0, candleCount - visible) : Math.Clamp(ViewStartIndex, 0, Math.Max(0, candleCount - visible));
        if (visible != VisibleCandleCount)
        {
            SetBoundValue(VisibleCandleCountProperty, visible);
        }

        if (start != ViewStartIndex)
        {
            SetBoundValue(ViewStartIndexProperty, start);
        }
    }

    private void SetViewport(int start, int visible, int candleCount)
    {
        visible = NormalizeVisible(visible, candleCount);
        start = Math.Clamp(start, 0, Math.Max(0, candleCount - visible));
        SetBoundValue(VisibleCandleCountProperty, visible);
        SetBoundValue(ViewStartIndexProperty, start);
        InvalidateVisual();
    }

    private void SetBoundValue(DependencyProperty property, object? value)
    {
        SetCurrentValue(property, value);
        BindingOperations.GetBindingExpression(this, property)?.UpdateSource();
    }

    private static Rect GetPlot(double width, double height)
    {
        var margin = new Thickness(10, 0, 72, 34);
        return new Rect(margin.Left, margin.Top, Math.Max(20, width - margin.Left - margin.Right), Math.Max(20, height - margin.Top - margin.Bottom));
    }

    private static int NormalizeVisible(int requested, int candleCount)
    {
        if (candleCount <= 0)
        {
            return 0;
        }

        var minimum = Math.Min(12, candleCount);
        return Math.Clamp(requested, minimum, candleCount);
    }

    private static double XForIndex(int index, int start, double step, Rect plot)
    {
        return plot.Left + ((index - start) * step) + (step / 2);
    }

    private static double Scale(decimal price, decimal min, decimal max, Rect plot)
    {
        var range = Math.Max((double)(max - min), 0.01);
        var normalized = ((double)(price - min)) / range;
        return plot.Bottom - (normalized * plot.Height);
    }

    private static List<T> ToList<T>(IEnumerable? source)
    {
        return source?.OfType<T>().ToList() ?? [];
    }

    private static FormattedText BuildText(string text, double size, Brush brush, FontWeight weight)
    {
        return new FormattedText(
            text,
            Thread.CurrentThread.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip);
    }

    private static void OnCollectionPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not MarketChart chart)
        {
            return;
        }

        if (args.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= chart.OnCollectionChanged;
        }

        if (args.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += chart.OnCollectionChanged;
        }

        chart.EnsureViewport(ToList<Candle>(chart.Candles).Count);
        chart.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        EnsureViewport(ToList<Candle>(Candles).Count);
        InvalidateVisual();
    }
}
