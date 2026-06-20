namespace AIShield.Backend.Models
{
    public class ToolPolicy
    {
        // 默认禁止或高风险的工具名称集合
        public List<string> DangerousTools { get; set; } = new();

        // 工具参数中的危险内容匹配规则
        public List<string> DangerousArgumentPatterns { get; set; } = new();

        // 每个 Agent 允许调用的工具白名单，当前仅用于兼容旧 JSON 种子结构
        public Dictionary<string, List<string>> AppToolAllowList { get; set; } = new();
    }
}
