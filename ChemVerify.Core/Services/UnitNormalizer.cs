namespace ChemVerify.Core.Services;

/// <summary>
/// Normalizes scientific units to canonical forms so that equivalent measurements
/// (e.g. 2 h and 120 min) can be compared by the contradiction validator.
/// </summary>
public static class UnitNormalizer
{
    /// <summary>
    /// Returns the canonical unit string for a given raw unit.
    /// </summary>
    public static string GetCanonicalUnit(string unit) => unit switch
    {
        "C" or "°C" => "°C",
        "K" => "°C",
        "h" => "min",
        "min" => "min",
        _ => unit
    };

    /// <summary>
    /// Converts a numeric value from its original unit into canonical form.
    /// </summary>
    public static double NormalizeValue(string unit, double value) => unit switch
    {
        "K" => value - 273.15,
        "h" => value * 60.0,
        _ => value
    };
}

