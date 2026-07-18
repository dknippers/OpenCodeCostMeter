using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace OpenCodeCostMeter.Services;

public static class ModelDisplayNameRules
{
    private static readonly Lazy<List<PrefixRule>> Rules = new(LoadRules);
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    public static string Format(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return "(unknown)";

        return Cache.GetOrAdd(modelId, static id =>
        {
            var result = ApplyDefault(id);

            foreach (var rule in Rules.Value)
            {
                if (rule.Prefix != "*" && !id.StartsWith(rule.Prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var (find, replace) in rule.Replacements)
                {
                    result = result.Replace(find, replace, StringComparison.Ordinal);
                }
            }

            return result;
        });
    }

    private static string ApplyDefault(string modelId)
    {
        var withSpaces = modelId.Replace('-', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(withSpaces);
    }

    private static List<PrefixRule> LoadRules()
    {
        var path = ConfigPath();
        if (path == null || !File.Exists(path)) return [];

        var rules = new List<PrefixRule>();
        foreach (var rawLine in File.ReadAllLines(path, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            var pipeIdx = line.IndexOf('|');
            if (pipeIdx <= 0) continue;

            var prefix = line[..pipeIdx].Trim();
            var pairsPart = line[(pipeIdx + 1)..];

            var replacements = new List<(string Find, string Replace)>();
            foreach (var pair in pairsPart.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var eqIdx = pair.IndexOf('=');
                if (eqIdx <= 0) continue;
                replacements.Add((pair[..eqIdx], pair[(eqIdx + 1)..]));
            }

            if (prefix.Length > 0 && replacements.Count > 0)
            {
                rules.Add(new PrefixRule(prefix, replacements));
            }
        }

        return rules;
    }

    private static string? ConfigPath()
    {
        var exeDir = AppContext.BaseDirectory;
        return exeDir == null ? null : Path.Combine(exeDir, "model-display-names.txt");
    }

    private sealed record PrefixRule(string Prefix, List<(string Find, string Replace)> Replacements);
}
