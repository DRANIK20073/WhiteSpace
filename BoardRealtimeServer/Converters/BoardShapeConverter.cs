using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

public class BoardShapeConverter : JsonConverter<BoardShape>
{
    public override BoardShape Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return JsonSerializer.Deserialize<BoardShape>(ref reader, options);
    }

    public override void Write(Utf8JsonWriter writer, BoardShape value, JsonSerializerOptions options)
    {
        // Сериализуем все свойства, кроме PrimaryKey
        var jsonObject = JsonSerializer.SerializeToNode(value, options) as JsonObject;
        jsonObject?.Remove("PrimaryKey"); // Убираем атрибут PrimaryKey

        jsonObject?.WriteTo(writer);
    }
}
