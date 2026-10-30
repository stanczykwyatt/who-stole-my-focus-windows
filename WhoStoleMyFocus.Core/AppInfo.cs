using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WhoStoleMyFocus.Core;

/// <summary>An app as it was when it came to the front.</summary>
/// <param name="Name">What the app calls itself: its file description, or the executable's name.</param>
/// <param name="Path">The executable.</param>
/// <param name="Product">The product it belongs to, from its version information, like "CloudSync" for a helper called "SyncHelper".</param>
public sealed partial record AppInfo(string Name, string? Path = null, string? Product = null)
{
    /// <summary>
    /// Groups events from the same app, even after an update moves it to a new version folder.
    /// Parts of Windows are keyed by their file name alone, since where they live changes between versions.
    /// </summary>
    [JsonIgnore]
    public string Key
    {
        get
        {
            if (Path is null) return Name;
            if (SystemApps.IsWindowsPart(this)) return "system:" + FileName;
            var parts = Path.Replace('/', '\\').ToLowerInvariant().Split('\\');
            for (var i = 0; i < parts.Length; i++)
            {
                if (UpdaterFolder().IsMatch(parts[i])) parts[i] = "app-*";
                else if (VersionFolder().IsMatch(parts[i])) parts[i] = "*";
            }
            return string.Join('\\', parts);
        }
    }

    /// <summary>The executable's file name, lowercased, like "consent.exe".</summary>
    [JsonIgnore]
    public string FileName
    {
        get
        {
            if (Path is null) return Name.ToLowerInvariant();
            var slash = Path.LastIndexOfAny(['\\', '/']);
            return Path[(slash + 1)..].ToLowerInvariant();
        }
    }

    /// <summary>
    /// The product this one ships as part of, when that's different from its own name. Helpers often
    /// have names nobody recognizes.
    /// </summary>
    [JsonIgnore]
    public string? ContainingAppName
    {
        get
        {
            var product = Product?.Trim();
            if (string.IsNullOrEmpty(product)) return null;
            if (string.Equals(product, Name.Trim(), StringComparison.OrdinalIgnoreCase)) return null;
            // "Microsoft® Windows® Operating System" is the product of every part of Windows: not helpful.
            if (product.Contains("Windows", StringComparison.OrdinalIgnoreCase) && product.Contains("Operating System", StringComparison.OrdinalIgnoreCase)) return null;
            return product;
        }
    }

    /// <summary>Folders like "app-1.0.9" that apps updating themselves install each version into.</summary>
    [GeneratedRegex(@"^app-\d+(\.\d+)*$")]
    private static partial Regex UpdaterFolder();

    /// <summary>Folders named only by a version, like "128.0.6613.120".</summary>
    [GeneratedRegex(@"^\d+(\.\d+){1,3}$")]
    private static partial Regex VersionFolder();
}
