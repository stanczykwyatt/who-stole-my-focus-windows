using System.Globalization;
using System.Text;

namespace WhoStoleMyFocus.Core;

/// <summary>The log as CSV, for spreadsheets and bug reports.</summary>
public static class CsvExport
{
    public static readonly string[] Header =
    [
        "Time", "App", "Part Of", "Path", "Took Focus From", "What Happened", "Why", "Seconds Since Input", "Seconds In Front",
    ];

    /// <summary>
    /// Events in the order given, one row each, with a header row. Times are ISO 8601 with the time
    /// zone offset, so they sort correctly and convert anywhere. Paths in <paramref name="home"/> are
    /// shortened to "~", so a shared file doesn't include your username.
    /// </summary>
    public static string Csv(IEnumerable<FocusEvent> events, TimeZoneInfo? zone = null, string? home = null)
    {
        zone ??= TimeZoneInfo.Local;
        var text = new StringBuilder();
        text.Append(string.Join(",", Header.Select(Field))).Append('\n');
        foreach (var e in events)
        {
            string[] row =
            [
                Timestamp(e.Date, zone),
                e.App.Name,
                e.App.ContainingAppName ?? "",
                e.App.Path is { } path ? Describe.ShortPath(path, home) : "",
                e.Previous?.Name ?? "",
                Describe.VerdictName(e.Verdict),
                EnumNameConverter<Cause>.NameOf(e.Cause),
                Number(e.Input.Latest),
                e.HeldFor is { } held ? Number(held) : "",
            ];
            text.Append(string.Join(",", row.Select(Field))).Append('\n');
        }
        return text.ToString();
    }

    static string Timestamp(DateTime date, TimeZoneInfo zone)
    {
        var utc = DateTime.SpecifyKind(date, DateTimeKind.Utc);
        var offset = zone.GetUtcOffset(utc);
        var local = utc + offset;
        var stamp = local.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        if (offset == TimeSpan.Zero) return stamp + "Z";
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var magnitude = offset.Duration();
        return $"{stamp}{sign}{magnitude.Hours:00}:{magnitude.Minutes:00}";
    }

    /// <summary>
    /// Quotes a field when needed. App names come from other apps, so a name that starts like a formula
    /// gets a leading apostrophe and opens in a spreadsheet as plain text.
    /// </summary>
    internal static string Field(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        var needsQuotes = value.IndexOfAny(['"', ',', '\n', '\r']) >= 0 || value.StartsWith(' ') || value.EndsWith(' ');
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    /// <summary>
    /// One decimal, rounded from the number's exact value like the Mac app's, so 0.15 (really
    /// 0.1499…) is "0.1" on both. .NET's own formatting would round it up.
    /// </summary>
    static string Number(double value)
    {
        var exact = decimal.Parse(value.ToString("G17", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
        return Math.Round(exact, 1, MidpointRounding.ToEven).ToString("0.0", CultureInfo.InvariantCulture);
    }
}
