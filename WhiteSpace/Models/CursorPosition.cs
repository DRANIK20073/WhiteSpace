using Supabase.Postgrest.Models;

public class CursorPosition : BaseModel
{
    public Guid BoardId { get; set; }  // Идентификатор доски
    public Guid UserId { get; set; }   // Идентификатор пользователя
    public double X { get; set; }      // Координата X курсора
    public double Y { get; set; }      // Координата Y курсора
}
