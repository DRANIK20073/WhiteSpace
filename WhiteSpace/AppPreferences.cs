using System;
using System.IO;
using System.Text.Json;

namespace WhiteSpace;

public sealed class AppPreferences
{
    public bool UseCompactView { get; set; }
    public string SortMode { get; set; } = "Новые сначала";
    public string LastSection { get; set; } = "MyBoards";
    public bool ConfirmBeforeLogout { get; set; } = true;
    public bool EnableAnimations { get; set; } = true;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WhiteSpace",
        "preferences.json");

    public static AppPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppPreferences();
            }

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppPreferences>(json) ?? new AppPreferences();
        }
        catch
        {
            return new AppPreferences();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}
