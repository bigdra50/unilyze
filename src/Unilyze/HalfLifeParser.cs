namespace Unilyze;

internal static class HalfLifeParser
{
    public static bool TryParse(string value, out TimeSpan halfLife, out string? error)
    {
        halfLife = default;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Half-life value is required.";
            return false;
        }

        var dot = value.LastIndexOf('.');
        if (dot <= 0 || dot == value.Length - 1)
        {
            error = $"Invalid half-life format '{value}'. Expected <N>.<unit> (e.g. 90.day).";
            return false;
        }

        var numberPart = value[..dot];
        var unit = value[(dot + 1)..];

        if (!double.TryParse(numberPart, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            error = $"Invalid half-life amount in '{value}'.";
            return false;
        }

        halfLife = unit.ToLowerInvariant() switch
        {
            "day" or "days" => TimeSpan.FromDays(amount),
            "week" or "weeks" => TimeSpan.FromDays(amount * 7),
            "month" or "months" => TimeSpan.FromDays(amount * 30),
            "year" or "years" => TimeSpan.FromDays(amount * 365),
            _ => default,
        };

        if (halfLife == default)
        {
            error = $"Unknown half-life unit in '{value}'. Use day, week, month, or year.";
            return false;
        }

        return true;
    }
}
