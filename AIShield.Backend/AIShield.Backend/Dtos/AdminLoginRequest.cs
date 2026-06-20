namespace AIShield.Backend.Dtos
{
    public class AdminLoginRequest
    {
        // 本地管理员密码，用于打开当前本机管理端
        public string Password { get; set; } = string.Empty;
    }
}
