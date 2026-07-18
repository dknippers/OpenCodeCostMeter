namespace OpenCodeCostMeter;

public sealed class LaunchOptions
{
    public string? DbPath { get; set; }
    public bool ShowHelp { get; set; }

    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        var options = new LaunchOptions();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-?", StringComparison.Ordinal) ||
                string.Equals(arg, "/?", StringComparison.Ordinal))
            {
                options.ShowHelp = true;
            }
            else if (string.Equals(arg, "--db-path", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Count)
                {
                    options.DbPath = args[++i];
                }
            }
        }
        return options;
    }
}
