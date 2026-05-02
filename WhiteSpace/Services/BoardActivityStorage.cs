using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace WhiteSpace.Services;

/// <summary>
/// Локальное время последнего захода на доску / сохранения (UTC) для подписи на карточках главной.
/// </summary>
public static class BoardActivityStorage
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WhiteSpace",
        "board_activity.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static void Touch(Guid boardId)
    {
        try
        {
            var map = LoadRaw();
            map[boardId.ToString("N")] = DateTime.UtcNow;
            SaveRaw(map);
        }
        catch
        {
            // без подписи на карточке — не критично
        }
    }

    /// <summary>Возвращает UTC-момент последней активности или null.</summary>
    public static DateTime? TryGetLastActivityUtc(Guid boardId)
    {
        try
        {
            var map = LoadRaw();
            return map.TryGetValue(boardId.ToString("N"), out var t) ? t : null;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, DateTime> LoadRaw()
    {
        if (!File.Exists(FilePath))
        {
            return new Dictionary<string, DateTime>();
        }

        var json = File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json, JsonOptions)
               ?? new Dictionary<string, DateTime>();
    }

    private static void SaveRaw(Dictionary<string, DateTime> map)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(map, JsonOptions);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Copy(tmp, FilePath, overwrite: true);
        try
        {
            File.Delete(tmp);
        }
        catch
        {
            // ignore
        }
    }
}
