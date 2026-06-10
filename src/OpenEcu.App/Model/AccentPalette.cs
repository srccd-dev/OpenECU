namespace OpenEcu.App.Model;

/// <summary>Maps an accent name to an RGB triple. UI-framework-agnostic (no Avalonia types).</summary>
public static class AccentPalette
{
    public static (byte R, byte G, byte B) Rgb(string accent) => accent switch
    {
        "white" => (245, 245, 245),
        "teal" => (29, 158, 117),
        "blue" => (55, 138, 221),
        "green" => (99, 153, 34),
        "yellow" => (234, 179, 8),
        "red" => (226, 75, 74),
        "black" => (40, 40, 40),
        _ => (29, 158, 117),
    };
}
