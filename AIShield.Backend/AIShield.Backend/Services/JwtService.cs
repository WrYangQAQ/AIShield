using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIShield.Backend.Services
{
    public class JwtService
    {
        private const string AdminSubject = "local-admin";
        private readonly IConfiguration _configuration;

        public JwtService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // 为本地管理端生成短期 JWT
        public JwtTokenResult GenerateAdminToken()
        {
            var expiresAt = DateTimeOffset.UtcNow.AddHours(GetExpireHours());
            var header = new Dictionary<string, object>
            {
                ["alg"] = "HS256",
                ["typ"] = "JWT"
            };

            var payload = new Dictionary<string, object>
            {
                ["sub"] = AdminSubject,
                ["scope"] = "admin",
                ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["exp"] = expiresAt.ToUnixTimeSeconds()
            };

            var encodedHeader = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
            var encodedPayload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
            var signature = Sign($"{encodedHeader}.{encodedPayload}");

            return new JwtTokenResult
            {
                Token = $"{encodedHeader}.{encodedPayload}.{signature}",
                ExpiresAt = expiresAt
            };
        }

        // 校验管理端 JWT 的签名、过期时间和权限范围
        public JwtValidationResult ValidateAdminToken(string token)
        {
            var parts = token.Split('.');

            if (parts.Length != 3)
            {
                return JwtValidationResult.Fail("Token 格式不正确");
            }

            var expectedSignature = Sign($"{parts[0]}.{parts[1]}");
            if (!FixedTimeEquals(parts[2], expectedSignature))
            {
                return JwtValidationResult.Fail("Token 签名无效");
            }

            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson);

            if (payload == null
                || !payload.TryGetValue("sub", out var sub)
                || !payload.TryGetValue("scope", out var scope)
                || !payload.TryGetValue("exp", out var exp))
            {
                return JwtValidationResult.Fail("Token 载荷无效");
            }

            if (!string.Equals(sub.GetString(), AdminSubject, StringComparison.Ordinal)
                || !string.Equals(scope.GetString(), "admin", StringComparison.Ordinal))
            {
                return JwtValidationResult.Fail("Token 权限不足");
            }

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= exp.GetInt64())
            {
                return JwtValidationResult.Fail("Token 已过期");
            }

            return JwtValidationResult.Success();
        }

        // 使用 HMAC-SHA256 对 JWT 头部和载荷签名
        private string Sign(string content)
        {
            var secret = _configuration["Jwt:Secret"];

            if (string.IsNullOrWhiteSpace(secret))
            {
                secret = "AIShield-Development-Jwt-Secret-Please-Change";
            }

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(content)));
        }

        // 读取 Token 过期小时数，未配置时默认 8 小时
        private int GetExpireHours()
        {
            return int.TryParse(_configuration["Jwt:ExpireHours"], out var hours) && hours > 0
                ? hours
                : 8;
        }

        // 将字节数组转换为 Base64Url 字符串
        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        // 将 Base64Url 字符串还原为字节数组
        private static byte[] Base64UrlDecode(string value)
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            var padding = 4 - base64.Length % 4;

            if (padding is > 0 and < 4)
            {
                base64 = base64.PadRight(base64.Length + padding, '=');
            }

            return Convert.FromBase64String(base64);
        }

        // 使用固定时间比较，降低签名比较的时序侧信道风险
        private static bool FixedTimeEquals(string left, string right)
        {
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(left),
                Encoding.UTF8.GetBytes(right));
        }
    }

    public class JwtTokenResult
    {
        // JWT 字符串
        public string Token { get; set; } = string.Empty;

        // JWT 过期时间
        public DateTimeOffset ExpiresAt { get; set; }
    }

    public class JwtValidationResult
    {
        // Token 是否有效
        public bool Succeeded { get; set; }

        // 校验失败时返回的原因
        public string Message { get; set; } = string.Empty;

        // 构建成功结果
        public static JwtValidationResult Success()
        {
            return new JwtValidationResult
            {
                Succeeded = true
            };
        }

        // 构建失败结果
        public static JwtValidationResult Fail(string message)
        {
            return new JwtValidationResult
            {
                Succeeded = false,
                Message = message
            };
        }
    }
}
