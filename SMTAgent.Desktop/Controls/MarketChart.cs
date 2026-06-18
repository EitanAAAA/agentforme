using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using SMTAgent.Core.Models;

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

    private readonly Pen _gridPen = new(new SolidColorBrush(Color.FromRgb(36, 47, 67)), 1);
    private readonly Pen _wickPen = new(new SolidColorBrush(Color.FromRgb(142, 156, 181)), 1);
    private readonly Brush _bullBrush = new SolidColorBrush(Color.FromRgb(53, 218, 163));
    private readonly Brush _bearBrush = new SolidColorBrush(Color.FromRgb(255, 92, 120));
    private readonly Brush _mutedTextBrush = new SolidColorBrush(Color.FromRgb(137, 151, 176));
    private readonly Brush _panelBrush = new SolidColorBrush(Color.FromRgb(12, 17, 28));
    private readonly Brush _swingHighBrush = new SolidColorBrush(Color.FromRgb(255, 190, 84));
    private readonly Brush _swingLowBrush = new SolidColorBrush(Color.FromRgb(102, 191, 255));
    private readonly Pen _bearSignalPen = new(new SolidColorBrush(Color.FromRgb(255, 92, 120)), 2.2);
    private readonly Pen _bullSignalPen = new(new SolidColorBrush(Color.FromRgb(53, 218, 163)), 2.2);

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

        var margin = new Thickness(44, 16, 18, 30);
        var plot = new Rect(margin.Left, margin.Top, Math.Max(20, width - margin.Left - margin.Right), Math.Max(20, height - margin.Top - margin.Bottom));
        var min = candles.Min(candle => candle.Low);
        var max = candles.Max(candle => candle.High);
        var pad = Math.Max((max - min) * 0.08m, 1m);
        min -= pad;
        max += pad;

        DrawGrid(drawingContext, plot, min, max);
        DrawCandles(drawingContext, candles, plot, min, max);
        DrawSwings(drawingContext, ToList<SwingPoint>(SwingPoints), candles, plot, min, max);
        DrawSignals(drawingContext, ToList<SmtSignal>(Signals), candles, plot, min, max);
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
            context.DrawText(label, new Point(4, y - 8));
        }
    }

    private void DrawCandles(DrawingContext context, IReadOnlyList<Candle> candles, Rect plot, decimal min, decimal max)
    {
        var step = plot.Width / Math.Max(candles.Count, 1);
        var bodyWidth = Math.Clamp(step * 0.52, 3, 10);

        for (var index = 0; index < candles.Count; index++)
        {
            var candle = candles[index];
            var x = plot.Left + (step * index) + (step / 2);
            var highY = Scale(candle.High, min, max, plot);
            var lowY = Scale(candle.Low, min, max, plot);
            var openY = Scale(candle.Open, min, max, plot);
            var closeY = Scale(candle.Close, min, max, plot);
            var up = candle.Close >= candle.Open;
            var bodyBrush = up ? _bullBrush : _bearBrush;

            context.DrawLine(_wickPen, new Point(x, highY), new Point(x, lowY));
            var bodyTop = Math.Min(openY, closeY);
            var bodyHeight = Math.Max(2, Math.Abs(openY - closeY));
            context.DrawRoundedRectangle(bodyBrush, null, new Rect(x - bodyWidth / 2, bodyTop, bodyWidth, bodyHeight), 2, 2);
        }
    }

    private void DrawSwings(DrawingContext context, IReadOnlyList<SwingPoint> swings, IReadOnlyList<Candle> candles, Rect plot, decimal min, decimal max)
    {
        var step = plot.Width / Math.Max(candles.Count, 1);

        foreach (var swing in swings.Where(swing => swing.Symbol == Symbol && swing.CandleIndex < candles.Count))
        {
            var x = plot.Left + (step * swing.CandleIndex) + (step / 2);
            var y = Scale(swing.Price, min, max, plot);
            var brush = swing.Type == SwingPointType.High ? _swingHighBrush : _swingLowBrush;
            context.DrawEllipse(brush, null, new Point(x, y), 4, 4);
        }
    }

    private void DrawSignals(DrawingContext context, IReadOnlyList<SmtSignal> signals, IReadOnlyList<Candle> candles, Rect plot, decimal min, decimal max)
    {
        var step = plot.Width / Math.Max(candles.Count, 1);
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

            var previousValue = Symbol == "ES" ? signal.EsPreviousSwingValue : signal.NqPreviousSwingValue;
            var currentValue = Symbol == "ES" ? signal.EsCurrentValue : signal.NqCurrentValue;
            var brush = signal.Type == SmtSignalType.Bearish ? _bearBrush : _bullBrush;
            var pen = signal.Type == SmtSignalType.Bearish ? _bearSignalPen : _bullSignalPen;
            var previousX = plot.Left + (step * previousIndex) + (step / 2);
            var currentX = plot.Left + (step * currentIndex) + (step / 2);
            var previousY = Scale(previousValue, min, max, plot);
            var currentY = Scale(currentValue, min, max, plot);
            var isLeader = signal.LeaderSymbol == Symbol;
            var textOffset = signal.Type == SmtSignalType.Bearish ? -44 : 24;

            context.DrawLine(pen, new Point(previousX, previousY), new Point(currentX, currentY));
            context.DrawEllipse(null, pen, new Point(previousX, previousY), 7, 7);
            context.DrawEllipse(brush, new Pen(Brushes.White, isLeader ? 2.4 : 1.4), new Point(currentX, currentY), isLeader ? 10 : 8, isLeader ? 10 : 8);

            var title = BuildText(signal.Type == SmtSignalType.Bearish ? "Bearish SMT" : "Bullish SMT", 12, Brushes.White, FontWeights.Bold);
            var detail = BuildText(signal.Reason, 10, Brushes.White, FontWeights.SemiBold);
            var labelWidth = Math.Max(title.Width, detail.Width) + 16;
            var labelHeight = title.Height + detail.Height + 10;
            var labelX = Math.Clamp(currentX - (labelWidth / 2), plot.Left + 4, plot.Right - labelWidth - 4);
            var labelY = Math.Clamp(currentY + textOffset, plot.Top + 4, plot.Bottom - labelHeight - 4);

            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(220, 15, 22, 36)), new Pen(brush, 1), new Rect(labelX, labelY, labelWidth, labelHeight), 6, 6);
            context.DrawText(title, new Point(labelX + 8, labelY + 5));
            context.DrawText(detail, new Point(labelX + 8, labelY + 5 + title.Height));
        }
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

        chart.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }
}
