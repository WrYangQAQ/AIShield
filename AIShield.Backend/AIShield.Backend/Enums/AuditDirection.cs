namespace AIShield.Backend.Enums
{
    public enum AuditDirection
    {
        // 进入 Agent 前的用户输入或外部内容
        Input,

        // 返回用户前的模型或 Agent 输出
        Output,

        // Agent 执行工具前的工具调用请求
        ToolCall
    }
}
