using System.Text.Json;

namespace OpenEcu.App.Model;

/// <summary>Persisted UI preferences: theme + accent. Default light + teal.</summary>
public sealed class AppSettings
{
    public bool DarkMode { get; set; }
    public string Accent { get; set; } = "teal";
    public string Adapter { get; set; } = "Cable";
    public bool RacingMode { get; set; }

    /// <summary>The accent colors offered in the picker.</summary>
    public static IReadOnlyList<string> Accents { get; } =
        new[] { "white", "teal", "blue", "green", "yellow", "red", "black" };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenECU", "settings.json");

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this));
    }

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch { /* corrupt file -> defaults */ }
        return new AppSettings();
    }
}
