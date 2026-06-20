namespace AIShield.Backend.Dtos
{
    public class SecurityCheckRequest
    {
        // 调用方可选传入的匿名主体哈希，后端不保存真实用户标识
        public string? SubjectHash { get; set; }

        // 待检测的文本内容
        public string Content { get; set; } = string.Empty;
    }
}
