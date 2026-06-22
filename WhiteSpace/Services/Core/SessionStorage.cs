using Supabase.Gotrue;
using System.IO;
using System.Text.Json;

/// <summary>Локальное хранение сессии Supabase между запусками приложения.</summary>
public static class SessionStorage
{
    private static readonly string FilePath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhiteSpace",
            "session.json"
        );

    /// <summary>Сохраняет токены и данные сессии в session.json.</summary>
    public static void SaveSession(Session session)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(session));
    }

    /// <summary>Читает сохранённую сессию или null, если файла нет.</summary>
    public static Session? LoadSession()
    {
        if (!File.Exists(FilePath))
            return null;

        return JsonSerializer.Deserialize<Session>(
            File.ReadAllText(FilePath)
        );
    }

    /// <summary>Удаляет файл сессии при выходе из аккаунта.</summary>
    public static void ClearSession()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }
}
