using System.Text.Json;

namespace AIShield.Backend.Dtos
{
    public class ToolCallCheckRequest
    {
        // 调用方可选传入的匿名主体哈希，后端不保存真实用户标识
        public string? SubjectHash { get; set; }

        // 待调用的工具名称
        public string ToolName { get; set; } = string.Empty;

        // 工具调用参数
        public Dictionary<string, JsonElement> Arguments { get; set; } = new();
    }
}
