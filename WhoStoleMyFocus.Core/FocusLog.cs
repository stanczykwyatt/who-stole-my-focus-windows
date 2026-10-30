using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhoStoleMyFocus.Core;

/// <summary>Every app that came to the front, oldest first, up to a limit.</summary>
public sealed class FocusLog : IEquatable<FocusLog>
{
    public const int DefaultLimit = 5_000;

    readonly List<FocusEvent> events;

    public FocusLog(IEnumerable<FocusEvent>? events = null, int limit = DefaultLimit)
    {
        Limit = Math.Max(1, limit);
        var all = events?.ToList() ?? [];
        this.events = all.Skip(Math.Max(0, all.Count - Limit)).ToList();
    }

    public int Limit { get; }
    public IReadOnlyList<FocusEvent> Events => events;

    /// <summary>Adds an event, dropping the oldest once the log is full.</summary>
    public void Append(FocusEvent e)
    {
        events.Add(e);
        if (events.Count > Limit) events.RemoveRange(0, events.Count - Limit);
    }

    public FocusEvent? Event(Guid id) => events.LastOrDefault(e => e.Id == id);

    /// <summary>Changes an event in place. Returns the changed event, or null if it has been dropped.</summary>
    public FocusEvent? Update(Guid id, Func<FocusEvent, FocusEvent> change)
    {
        // Nearly always the newest event or the one before, so search from the end.
        var index = events.FindLastIndex(e => e.Id == id);
        if (index < 0) return null;
        events[index] = change(events[index]) with { Id = id };
        return events[index];
    }

    /// <summary>Records how long an app stayed in front, once the next one comes forward or tracking stops.</summary>
    public void Close(Guid id, double heldFor, AppInfo? next, Verdict? nextVerdict) =>
        Update(id, e => e with { HeldFor = Math.Max(0, heldFor), Next = next, NextVerdict = nextVerdict });

    public void RemoveAll() => events.Clear();

    /// <summary>Steals, newest first, leaving out apps you've chosen to ignore.</summary>
    public List<FocusEvent> Steals(ISet<string>? ignoring = null, DateTime? since = null) =>
        Enumerable.Reverse(events)
            .Where(e => e.Verdict == Verdict.Stole && ignoring?.Contains(e.App.Key) != true && (since is null || e.Date >= since))
            .ToList();

    /// <summary>Apps ranked by how often they stole focus: most steals first, then the most recent.</summary>
    public List<Offender> Offenders(ISet<string>? ignoring = null, DateTime? since = null)
    {
        var tallies = new Dictionary<string, (Offender Offender, Dictionary<string, (AppInfo App, int Count)> Victims)>();
        foreach (var e in events)
        {
            if (e.Verdict != Verdict.Stole || ignoring?.Contains(e.App.Key) == true) continue;
            if (since is not null && e.Date < since) continue;
            if (!tallies.TryGetValue(e.App.Key, out var tally))
            {
                tally = (new Offender(e.App, 0, e.Date, null), new Dictionary<string, (AppInfo, int)>());
            }
            var offender = tally.Offender with
            {
                App = e.App,
                Count = tally.Offender.Count + 1,
                LastDate = e.Date > tally.Offender.LastDate ? e.Date : tally.Offender.LastDate,
            };
            if (e.Previous is { } victim)
            {
                var seen = tally.Victims.TryGetValue(victim.Key, out var v) ? v.Count : 0;
                tally.Victims[victim.Key] = (victim, seen + 1);
            }
            tallies[e.App.Key] = (offender, tally.Victims);
        }
        return tallies.Values
            .Select(t => t.Offender with
            {
                UsualVictim = t.Victims.Values
                    .OrderByDescending(v => v.Count)
                    .ThenBy(v => v.App.Name, StringComparer.Ordinal)
                    .Select(v => v.App)
                    .FirstOrDefault(),
            })
            .OrderByDescending(o => o.Count)
            .ThenByDescending(o => o.LastDate)
            .ThenBy(o => o.App.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public bool Equals(FocusLog? other) => other is not null && Limit == other.Limit && events.SequenceEqual(other.events);
    public override bool Equals(object? obj) => Equals(obj as FocusLog);
    public override int GetHashCode() => HashCode.Combine(Limit, events.Count);
}

/// <summary>An app that keeps stealing focus.</summary>
/// <param name="App">As it was the last time it stole focus.</param>
/// <param name="UsualVictim">The app it took focus from most often.</param>
public sealed record Offender(AppInfo App, int Count, DateTime LastDate, AppInfo? UsualVictim);

/// <summary>The log as it's saved to disk: versioned JSON, so a newer app's file is never misread.</summary>
public static class LogFile
{
    public const int Version = 1;

    /// <summary>Written by a newer version of the app.</summary>
    public sealed class NewerVersionException(int version) : Exception($"The log was written by a newer version (format {version}).")
    {
        public int FoundVersion { get; } = version;
    }

    sealed record Envelope([property: JsonRequired] int Version, [property: JsonRequired] List<FocusEvent> Events);

    public static readonly JsonSerializerOptions Options = MakeOptions();

    static JsonSerializerOptions MakeOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        options.Converters.Add(new UnixSecondsConverter());
        options.Converters.Add(new EnumNameConverter<Cause>());
        options.Converters.Add(new EnumNameConverter<Verdict>());
        return options;
    }

    public static byte[] Encode(FocusLog log) =>
        JsonSerializer.SerializeToUtf8Bytes(new Envelope(Version, log.Events.ToList()), Options);

    public static FocusLog Decode(byte[] data, int limit = FocusLog.DefaultLimit)
    {
        using var document = JsonDocument.Parse(data);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("version", out var version) || !version.TryGetInt32(out var found))
        {
            throw new JsonException("The log has no version.");
        }
        if (found > Version) throw new NewerVersionException(found);
        var envelope = JsonSerializer.Deserialize<Envelope>(data, Options) ?? throw new JsonException("The log is empty.");
        if (envelope.Events is null || envelope.Events.Any(e => e is null)) throw new JsonException("The log has no events.");
        return new FocusLog(envelope.Events, limit);
    }

    /// <summary>Dates as seconds since 1970, the same as the Mac app's files.</summary>
    sealed class UnixSecondsConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var seconds = reader.GetDouble();
            // 1970 to 2200: anything else isn't a date this app wrote.
            if (!double.IsFinite(seconds) || seconds < 0 || seconds > 7_258_118_400) throw new JsonException("A date is out of range.");
            return DateTime.UnixEpoch.AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteNumberValue((value.ToUniversalTime() - DateTime.UnixEpoch).Ticks / (double)TimeSpan.TicksPerSecond);
    }
}

/// <summary>Enum values as the Mac app writes them: "stole", "systemUI", "keypress".</summary>
public sealed class EnumNameConverter<T> : JsonConverter<T> where T : struct, Enum
{
    static readonly Dictionary<T, string> Names = Enum.GetValues<T>().ToDictionary(v => v, v => Name(v.ToString()));
    static readonly Dictionary<string, T> Values = Names.ToDictionary(p => p.Value, p => p.Key);

    static string Name(string s) => char.ToLowerInvariant(s[0]) + s[1..];

    public static string NameOf(T value) => Names[value];

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Values.TryGetValue(reader.GetString() ?? "", out var value) ? value : throw new JsonException($"Unknown {typeof(T).Name}.");

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(Names[value]);
}
