using SMTAgent.Core.Models;

namespace SMTAgent.Core.Engines;

public sealed class SwingDetector
{
    public IReadOnlyList<SwingPoint> Detect(IReadOnlyList<Candle> candles, int strength)
    {
        if (strength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(strength), "Swing strength must be at least 1.");
        }

        var swings = new List<SwingPoint>();
        if (candles.Count < (strength * 2) + 1)
        {
            return swings;
        }

        for (var index = strength; index < candles.Count - strength; index++)
        {
            var candle = candles[index];
            var isSwingHigh = true;
            var isSwingLow = true;

            for (var offset = 1; offset <= strength; offset++)
            {
                if (candle.High <= candles[index - offset].High || candle.High <= candles[index + offset].High)
                {
                    isSwingHigh = false;
                }

                if (candle.Low >= candles[index - offset].Low || candle.Low >= candles[index + offset].Low)
                {
                    isSwingLow = false;
                }
            }

            if (isSwingHigh)
            {
                swings.Add(new SwingPoint(candle.Symbol, candle.Time, candle.High, SwingPointType.High, index));
            }

            if (isSwingLow)
            {
                swings.Add(new SwingPoint(candle.Symbol, candle.Time, candle.Low, SwingPointType.Low, index));
            }
        }

        return swings;
    }
}
