namespace OpenCodeCostMeter.Models;

public sealed record ModelBreakdown(
    string Provider,
    string Model,
    double Cost);