using System.Text.Json;
using System.Text.Json.Serialization;
using AIShield.Backend.Enums;

namespace AIShield.Backend.Helpers
{
    public class MemorySourceJsonConverter : JsonConverter<MemorySource>
    {
        // 重写反序列化
        public override MemorySource Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if(reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("记忆来源必须是字符串");
            }

            string? value = reader.GetString();

            if (!MemorySourceParser.TryParse(value, out MemorySource source))
            {
                throw new JsonException($"无法识别的记忆来源：{value}");
            }

            return source;
        }

        // 重写序列化
        public override void Write(Utf8JsonWriter writer, MemorySource value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
