namespace AIShield.Backend.Dtos
{
    public class AdminLoginResponse
    {
        // 登录是否成功
        public bool Success { get; set; }

        // 管理端访问令牌
        public string Token { get; set; } = string.Empty;

        // 令牌过期时间
        public DateTimeOffset? TokenExpiresAt { get; set; }

        // 操作结果提示
        public string Message { get; set; } = string.Empty;
    }
}
