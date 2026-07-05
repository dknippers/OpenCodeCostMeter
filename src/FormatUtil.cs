namespace OpenCodeCostMeter;

public static class FormatUtil
{
    public static long ToCents(double usd)
    {
        var rounded = Math.Round(usd * 100.0, MidpointRounding.AwayFromZero);
        return (long)rounded;
    }
}