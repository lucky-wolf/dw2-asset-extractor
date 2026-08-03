using System.Text.Json;

namespace DistantWorlds2.AssetExtractor;

// Remembers the last-used install/output folders so the user isn't forced to re-browse for them on
// every run. Deliberately does NOT remember bundle/asset-type selections — those are expected to vary
// run to run, unlike the two folders which are typically the same every time for a given user.
internal sealed class UserSettings
{
    private const int DefaultFfmpegTimeoutSeconds = 300;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "dw2extract", "settings.json");

    public string? InstallDir { get; set; }
    public string? OutputDir { get; set; }

    // Generic default timeout for external ffmpeg process calls.
    public int? FfmpegTimeoutSeconds { get; set; }

    // Specific overrides; if unset/invalid they fall back to FfmpegTimeoutSeconds.
    public int? TextureFfmpegTimeoutSeconds { get; set; }
    public int? SoundFfmpegTimeoutSeconds { get; set; }

    public int GetTextureFfmpegTimeoutSeconds() => ResolveTimeout(TextureFfmpegTimeoutSeconds, FfmpegTimeoutSeconds);
    public int GetSoundFfmpegTimeoutSeconds() => ResolveTimeout(SoundFfmpegTimeoutSeconds, FfmpegTimeoutSeconds);

    private static int ResolveTimeout(int? specific, int? generic)
    {
        if (specific is > 0)
            return specific.Value;
        if (generic is > 0)
            return generic.Value;
        return DefaultFfmpegTimeoutSeconds;
    }

    private bool EnsureTimeoutDefaults()
    {
        var changed = false;

        if (FfmpegTimeoutSeconds is null or <= 0)
        {
            FfmpegTimeoutSeconds = DefaultFfmpegTimeoutSeconds;
            changed = true;
        }

        if (TextureFfmpegTimeoutSeconds is null or <= 0)
        {
            TextureFfmpegTimeoutSeconds = FfmpegTimeoutSeconds;
            changed = true;
        }

        if (SoundFfmpegTimeoutSeconds is null or <= 0)
        {
            SoundFfmpegTimeoutSeconds = FfmpegTimeoutSeconds;
            changed = true;
        }

        return changed;
    }

    public static UserSettings Load()
    {
        UserSettings settings;
        var fileExists = File.Exists(SettingsPath);

        try
        {
            settings = fileExists
                ? (JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath)) ?? new UserSettings())
                : new UserSettings();
        }
        catch
        {
            // Missing, corrupt, or unreadable — just start fresh rather than fail the whole run over it.
            settings = new UserSettings();
            fileExists = false;
        }

        var changed = settings.EnsureTimeoutDefaults();
        if (!fileExists || changed)
            settings.Save();

        return settings;
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
