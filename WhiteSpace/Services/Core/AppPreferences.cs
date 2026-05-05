using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhiteSpace;

public sealed class AppPreferences
{
    public bool UseCompactView { get; set; }
    public string SortMode { get; set; } = "Новые сначала";
    public string LastSection { get; set; } = "MyBoards";
    public bool ConfirmBeforeLogout { get; set; } = true;
    public bool EnableAnimations { get; set; } = true;

    /// <summary>Светлая или тёмная тема интерфейса.</summary>
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "Light";

    /// <summary>Приводит значение к Light или Dark после загрузки из JSON.</summary>
    public void NormalizeTheme()
    {
        Theme = string.Equals(Theme?.Trim(), "Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
    }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WhiteSpace",
        "preferences.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static AppPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppPreferences();
            }

            var json = File.ReadAllText(FilePath);
            var prefs = JsonSerializer.Deserialize<AppPreferences>(json, JsonOptions) ?? new AppPreferences();
            prefs.NormalizeTheme();
            return prefs;
        }
        catch
        {
            return new AppPreferences();
        }
    }

    public void Save()
    {
        TrySave(out _);
    }

    /// <summary>
    /// Загружает актуальный файл с диска, применяет правки и сохраняет — чтобы частичные обновления
    /// (раздел главной, сортировка и т.д.) не перезаписывали тему устаревшим объектом в памяти.
    /// </summary>
    public static bool MutateAndSave(Action<AppPreferences> mutate, out string? errorMessage)
    {
        errorMessage = null;
        try
        {
            var prefs = Load();
            mutate(prefs);
            prefs.NormalizeTheme();
            return prefs.TrySave(out errorMessage);
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Сохраняет настройки атомарно (через временный файл).
    /// </summary>
    public bool TrySave(out string? errorMessage)
    {
        errorMessage = null;
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(this, JsonOptions);
            var tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Copy(tempPath, FilePath, overwrite: true);
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // временный файл можно оставить
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}
