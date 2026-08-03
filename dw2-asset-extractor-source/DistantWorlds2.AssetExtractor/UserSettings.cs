using System.Text.Json;

namespace DistantWorlds2.AssetExtractor;

// Remembers the last-used install/output folders so the user isn't forced to re-browse for them on
// every run. Deliberately does NOT remember bundle/asset-type selections — those are expected to vary
// run to run, unlike the two folders which are typically the same every time for a given user.
internal sealed class UserSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "dw2extract", "settings.json");

    public string? InstallDir { get; set; }
    public string? OutputDir { get; set; }

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath)) ?? new UserSettings();
        }
        catch
        {
            // Missing, corrupt, or unreadable — just start fresh rather than fail the whole run over it.
        }
        return new UserSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this));
        }
        catch
        {
            // Non-fatal — worst case the user gets asked to browse again next run.
        }
    }
}
