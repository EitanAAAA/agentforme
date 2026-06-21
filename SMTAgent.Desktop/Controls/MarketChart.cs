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

    public static readonly DependencyProperty ViewStartIndexProperty =
        DependencyProperty.Register(nameof(ViewStartIndex), typeof(int), typeof(MarketChart), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty VisibleCandleCountProperty =
        DependencyProperty.Register(nameof(VisibleCandleCount), typeof(int), typeof(MarketChart), new FrameworkPropertyMetadata(90, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AutoScrollToLatestProperty =
        DependencyProperty.Register(nameof(AutoScrollToLatest), typeof(bool), typeof(MarketChart), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CrosshairTimeProperty =
        DependencyProperty.Register(nameof(CrosshairTime), typeof(DateTime?), typeof(MarketChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly Pen _gridPen = new(new SolidColorBrush(Color.FromRgb(30, 40, 56)), 1);
    private readonly Pen _wickPen = new(new SolidColorBrush(Color.FromRgb(142, 156, 181)), 1);
    private readonly Brush _bullBrush = new SolidColorBrush(Color.FromRgb(53, 218, 163));
    private readonly Brush _bearBrush = new SolidColorBrush(Color.FromRgb(255, 92, 120));
    private readonly Brush _mutedTextBrush = new SolidColorBrush(Color.FromRgb(137, 151, 176));
    private readonly Brush _panelBrush = new SolidColorBrush(Color.FromRgb(10, 15, 24));
    private readonly Brush _tooltipBrush = new SolidColorBrush(Color.FromArgb(236, 13, 19, 31));
    private readonly Brush _swingHighBrush = new SolidColorBrush(Color.FromRgb(255, 190, 84));
    private readonly Brush _swingLowBrush = new SolidColorBrush(Color.FromRgb(102, 191, 255));
    private readonly Pen _bearSignalPen = new(new SolidColorBrush(Color.FromRgb(255, 92, 120)), 2.2);
    private readonly Pen _bullSignalPen = new(new SolidColorBrush(Color.FromRgb(53, 218, 163)), 2.2);
    private readonly Pen _crosshairPen = new(new SolidColorBrush(Color.FromArgb(150, 160, 176, 198)), 1);
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
        drawingContext.DrawRoundedRectangle(_panelBrush, null, new Rect(0, 0, width, height), 8, 8);

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

        var min = visibleCandles.Min(candle => candle.Low);
        var max = visibleCandles.Max(candle => candle.High);
        var pad = Math.Max((max - min) * 0.08m, 1m);
        min -= pad;
        max += pad;

        DrawGrid(drawingContext, plot, min, max);
        DrawCandles(drawingContext, candles, start, visibleCount, plot, min, max);
        DrawSwings(drawingContext, ToList<SwingPoint>(SwingPoints), start, visibleCount, plot, min, max);
        DrawSignals(drawingContext, ToList<SmtSignal>(Signals), candles, start, visibleCount, plot, min, max);
        DrawTimeScale(drawingContext, candles, start, visibleCount, plot);
        DrawCrosshair(drawingContext, candles, start, visibleCount, plot, min, max);
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
        var text = BuildText("Waiting for synchronized 15m data...", 13, _mutedTextBrush, FontWeights.Medium);
        context.DrawText(text, new Point((width - text.Width) / 2, (height - text.Height) / 2));
    }

    private void DrawGrid(DrawingContext context, Rect plot, decimal min, decimal max)
    {
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Top + (plot.Height / 4 * i);
            context.DrawLine(_gridPen, new Point(plot.Left, y), new Point(plot.Right, y));

            var price = max - ((max - min) / 4 * i);
            var label = BuildText(price.ToString("0.00"), 10, _mutedTextBrush, FontWeights.Normal);
            context.DrawText(label, new Point(plot.Right + 8, y - 8));
        }

        for (var i = 0; i <= 5; i++)
        {
            var x = plot.Left + (plot.Width / 5 * i);
            context.DrawLine(_gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
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

            context.DrawLine(_wickPen, new Point(x, highY), new Point(x, lowY));
            var bodyTop = Math.Min(openY, closeY);
            var bodyHeight = Math.Max(2, Math.Abs(openY - closeY));
            var body = new Rect(x - bodyWidth / 2, bodyTop, bodyWidth, bodyHeight);
            context.DrawRoundedRectangle(bodyBrush, null, body, 2, 2);

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

    private void DrawSwings(DrawingContext context, IReadOnlyList<SwingPoint> swings, int start, int visibleCount, Rect plot, decimal min, decimal max)
    {
        var step = plot.Width / Math.Max(visibleCount, 1);
        var end = start + visibleCount;

        foreach (var swing in swings.Where(swing => swing.Symbol == Symbol && swing.CandleIndex >= start && swing.CandleIndex < end))
        {
            var x = XForIndex(swing.CandleIndex, start, step, plot);
            var y = Scale(swing.Price, min, max, plot);
            var brush = swing.Type == SwingPointType.High ? _swingHighBrush : _swingLowBrush;
            context.DrawEllipse(brush, null, new Point(x, y), 4, 4);
        }
    }

    private void DrawSignals(DrawingContext context, IReadOnlyList<SmtSignal> signals, IReadOnlyList<Candle> candles, int start, int visibleCount, Rect plot, decimal min, decimal max)
    {
        var step = plot.Width / Math.Max(visibleCount, 1);
        var end = start + visibleCount;
        var candlesByTime = candles
            .Select((candle, index) => new { candle.Time, Index = index })
            .ToDictionary(item => item.Time, item => item.Index);

        foreach (var signal in signals.Where(signal => signal.LeaderSymbol == Symbol || signal.FailedSymbol == Symbol))
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
            var brush = signal.Type == SmtSignalType.Bearish ? _bearBrush : _bullBrush;
            var pen = signal.Type == SmtSignalType.Bearish ? _bearSignalPen : _bullSignalPen;
            var previousX = XForIndex(previousIndex, start, step, plot);
            var currentX = XForIndex(currentIndex, start, step, plot);
            var previousY = Scale(previousValue, min, max, plot);
            var currentY = Scale(currentValue, min, max, plot);
            var isLeader = signal.LeaderSymbol == Symbol;
            var textOffset = signal.Type == SmtSignalType.Bearish ? -46 : 26;

            context.DrawLine(pen, new Point(previousX, previousY), new Point(currentX, currentY));
            context.DrawEllipse(null, pen, new Point(previousX, previousY), 7, 7);
            context.DrawEllipse(brush, new Pen(Brushes.White, isLeader ? 2.4 : 1.4), new Point(currentX, currentY), isLeader ? 10 : 8, isLeader ? 10 : 8);

            var title = BuildText(signal.Type == SmtSignalType.Bearish ? "Bearish SMT" : "Bullish SMT", 12, Brushes.White, FontWeights.Bold);
            var detail = BuildText(signal.Reason, 10, Brushes.White, FontWeights.SemiBold);
            var values = BuildText(signal.CalculationSummary, 10, _mutedTextBrush, FontWeights.SemiBold);
            var labelWidth = Math.Max(Math.Max(title.Width, detail.Width), values.Width) + 16;
            var labelHeight = title.Height + detail.Height + values.Height + 12;
            var labelX = Math.Clamp(currentX - (labelWidth / 2), plot.Left + 4, plot.Right - labelWidth - 4);
            var labelY = Math.Clamp(currentY + textOffset, plot.Top + 4, plot.Bottom - labelHeight - 4);

            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(224, 15, 22, 36)), new Pen(brush, 1), new Rect(labelX, labelY, labelWidth, labelHeight), 6, 6);
            context.DrawText(title, new Point(labelX + 8, labelY + 5));
            context.DrawText(detail, new Point(labelX + 8, labelY + 5 + title.Height));
            context.DrawText(values, new Point(labelX + 8, labelY + 5 + title.Height + detail.Height));
        }
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
            var label = BuildText(candle.Time.ToString("HH:mm"), 10, _mutedTextBrush, FontWeights.Normal);
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
        var tooltip = BuildText(text, 11, Brushes.White, FontWeights.SemiBold);
        var tooltipWidth = tooltip.Width + 16;
        var tooltipHeight = tooltip.Height + 10;
        var tooltipX = Math.Clamp(x + 10, plot.Left + 4, plot.Right - tooltipWidth - 4);
        var tooltipY = Math.Clamp(y - tooltipHeight - 10, plot.Top + 4, plot.Bottom - tooltipHeight - 4);
        context.DrawRoundedRectangle(_tooltipBrush, new Pen(new SolidColorBrush(Color.FromRgb(70, 90, 120)), 1), new Rect(tooltipX, tooltipY, tooltipWidth, tooltipHeight), 5, 5);
        context.DrawText(tooltip, new Point(tooltipX + 8, tooltipY + 5));
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
        var margin = new Thickness(18, 18, 62, 34);
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
