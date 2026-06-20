using System.Security.Cryptography;
using System.Text;
using AIShield.Backend.Dtos;

namespace AIShield.Backend.Services
{
    public class AuthService
    {
        private readonly IConfiguration _configuration;
        private readonly JwtService _jwtService;

        public AuthService(IConfiguration configuration, JwtService jwtService)
        {
            _configuration = configuration;
            _jwtService = jwtService;
        }

        // 使用本地管理员密码登录管理端
        public AdminLoginResponse Login(AdminLoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Password))
            {
                return Fail("密码不能为空");
            }

            if (!VerifyAdminPassword(request.Password))
            {
                return Fail("密码不正确");
            }

            var token = _jwtService.GenerateAdminToken();
            return new AdminLoginResponse
            {
                Success = true,
                Token = token.Token,
                TokenExpiresAt = token.ExpiresAt,
                Message = "登录成功"
            };
        }

        // 校验输入密码是否与本地配置一致
        private bool VerifyAdminPassword(string password)
        {
            var configuredPassword = _configuration["Admin:Password"];

            if (string.IsNullOrWhiteSpace(configuredPassword))
            {
                configuredPassword = "123456";
            }

            return FixedTimeEquals(password, configuredPassword);
        }

        // 使用固定时间比较密码，避免明显的时序差异
        private static bool FixedTimeEquals(string left, string right)
        {
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(left),
                Encoding.UTF8.GetBytes(right));
        }

        // 构建登录失败响应
        private static AdminLoginResponse Fail(string message)
        {
            return new AdminLoginResponse
            {
                Success = false,
                Message = message
            };
        }
    }
}
