namespace AIShield.Backend.Enums
{
    public enum MemorySyncActionStatus
    {
        // 等待用户 Agent 拉取和执行。
        Pending = 1,

        // 用户 Agent 已确认执行成功。
        Confirmed = 2,

        // 用户 Agent 明确报告执行失败。
        Failed = 3
    }
}
