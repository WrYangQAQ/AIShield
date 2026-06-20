namespace AIShield.Backend.Enums
{
    public enum MemorySyncActionType
    {
        // 更新用户主记忆库中的置信度。
        UpdateConfidence = 1,

        // 将用户主记忆库中的记忆归档。
        Archive = 2,

        // 恢复用户主记忆库中的归档记忆。
        Restore = 3
    }
}
