using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WhiteSpace.Services;

/// <summary>Локальный список избранных досок на главной.</summary>
public static class FavoriteBoardsStorage
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WhiteSpace",
        "favorite-boards.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Загружает список id избранных досок из AppData.</summary>
    public static List<Guid> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new List<Guid>();
            }

            var json = File.ReadAllText(FilePath);
            var ids = JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions);
            return ids ?? new List<Guid>();
        }
        catch
        {
            return new List<Guid>();
        }
    }

    /// <summary>Быстрая проверка «доска в избранном» без кэша.</summary>
    public static bool IsFavorite(Guid boardId) => Load().Contains(boardId);

    /// <summary>Переключает избранное. Возвращает новое состояние.</summary>
    public static bool Toggle(Guid boardId)
    {
        var ids = Load();
        if (ids.Remove(boardId))
        {
            Save(ids);
            return false;
        }

        ids.Insert(0, boardId);
        Save(ids);
        return true;
    }

    /// <summary>Убирает доску из избранного (если была там).</summary>
    public static void Remove(Guid boardId)
    {
        var ids = Load();
        if (!ids.Remove(boardId))
        {
            return;
        }

        Save(ids);
    }

    /// <summary>Записывает список id в favorite-boards.json.</summary>
    private static void Save(List<Guid> boardIds)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var distinct = boardIds.Distinct().ToList();
        File.WriteAllText(FilePath, JsonSerializer.Serialize(distinct, JsonOptions));
    }
}
