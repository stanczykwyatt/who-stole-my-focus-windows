using WhoStoleMyFocus.Core;

namespace WhoStoleMyFocus;

/// <summary>
/// The log and settings, in the app's own folder under %LOCALAPPDATA%. The only files it ever writes.
/// Each is written to a temporary file first and then moved into place, so a crash can't leave half a file.
/// </summary>
internal sealed class LogStore
{
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WhoStoleMyFocus");
    static string LogPath => Path.Combine(Folder, "log.json");
    static string SettingsPath => Path.Combine(Folder, "settings.json");

    public static FocusLog LoadLog()
    {
        try
        {
            return File.Exists(LogPath) ? LogFile.Decode(File.ReadAllBytes(LogPath)) : new FocusLog();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A damaged or newer file is kept aside rather than overwritten.
            TryMoveAside(LogPath);
            return new FocusLog();
        }
    }

    public static AppSettings LoadSettings()
    {
        try
        {
            return AppSettings.Load(File.Exists(SettingsPath) ? File.ReadAllBytes(SettingsPath) : null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public static void Save(FocusLog log) => Write(LogPath, LogFile.Encode(log));

    public static void Save(AppSettings settings) => Write(SettingsPath, settings.Save());

    static void Write(string path, byte[] data)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var temporary = path + ".tmp";
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Not worth interrupting anyone over; the next change tries again.
        }
    }

    static void TryMoveAside(string path)
    {
        try
        {
            File.Move(path, path + $".unreadable-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
