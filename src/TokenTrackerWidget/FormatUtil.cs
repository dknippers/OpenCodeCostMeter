using System.Globalization;

namespace TokenTrackerWidget;

public static class FormatUtil
{
    public static long ToCents(double usd)
    {
        var rounded = Math.Round(usd * 100.0, MidpointRounding.AwayFromZero);
        return (long)rounded;
    }

    public static string FormatBig(long n)
    {
        return n switch
        {
            >= 1_000_000 => (n / 1_000_000.0).ToString("0.##", CultureInfo.InvariantCulture) + "M",
            >= 1_000 => (n / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k",
            _ => n.ToString("0", CultureInfo.InvariantCulture)
        };
    }

    public static string FormatCurrency(double usd)
        => usd.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
}