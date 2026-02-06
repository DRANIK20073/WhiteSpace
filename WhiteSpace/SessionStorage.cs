using Supabase.Gotrue;
using System.IO;
using System.Text.Json;

public static class SessionStorage
{
    private static readonly string FilePath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhiteSpace",
            "session.json"
        );

    public static void SaveSession(Session session)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(session));
    }

    public static Session? LoadSession()
    {
        if (!File.Exists(FilePath))
            return null;

        return JsonSerializer.Deserialize<Session>(
            File.ReadAllText(FilePath)
        );
    }

    public static void ClearSession()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }
}
