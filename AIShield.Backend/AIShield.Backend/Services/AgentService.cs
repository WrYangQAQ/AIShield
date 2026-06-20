using System.Security.Cryptography;
using System.Text;
using AIShield.Backend.Dtos;
using AIShield.Backend.Models;
using AIShield.Backend.Repositories;

namespace AIShield.Backend.Services
{
    public class AgentService
    {
        private readonly IAgentRepository _agentRepository;
        private readonly RuleConfigService _ruleConfigService;

        public AgentService(IAgentRepository agentRepository, RuleConfigService ruleConfigService)
        {
            _agentRepository = agentRepository;
            _ruleConfigService = ruleConfigService;
        }

        // 注册新的 Agent，并返回只展示一次的 Agent Key
        public async Task<RegisterAgentResponse> RegisterAsync(RegisterAgentRequest request)
        {
            ValidateRegisterRequest(request);

            await _ruleConfigService.EnsureSeedRulesAsync();

            var agentKey = GenerateAgentKey();
            var agentKeyPreview = ToAgentKeyPreview(agentKey);
            var agentKeySalt = GenerateSalt();
            var agent = new AgentApp
            {
                AgentName = request.AgentName.Trim(),
                Scenario = request.Scenario.Trim(),
                AgentKeyHash = HashSecret(agentKey, agentKeySalt),
                AgentKeyFingerprint = HashFingerprint(agentKey),
                AgentKeySalt = agentKeySalt,
                Enabled = true,
                CreatedAt = DateTime.Now,
                AgentKeyPreview = agentKeyPreview
            };

            await _agentRepository.AddAsync(agent);

            // 新 Agent 默认绑定当前全部规则，后续可在规则管理页按 Agent 调整启用状态。
            await _ruleConfigService.BindAllRulesToAgentAsync(agent.Id);

            return new RegisterAgentResponse
            {
                AgentId = agent.Id,
                AgentName = agent.AgentName,
                AgentKey = agentKey,
                Message = "Agent 注册成功，请妥善保存 Agent Key，后续无法再次查看明文"
            };
        }

        // 查询全部 Agent，供管理端选择当前操作对象
        public async Task<List<AgentSummaryResponse>> ListAsync()
        {
            var agents = await _agentRepository.ListAsync();
            return agents.Select(ToSummaryResponse).ToList();
        }

        // 根据主键查询单个 Agent
        public async Task<AgentSummaryResponse?> GetAsync(long agentId)
        {
            var agent = await _agentRepository.GetByIdAsync(agentId);
            return agent == null ? null : ToSummaryResponse(agent);
        }

        // 启用或禁用 Agent
        public async Task<AgentSummaryResponse?> UpdateEnabledAsync(long agentId, bool enabled)
        {
            var agent = await _agentRepository.GetByIdAsync(agentId);
            if (agent == null)
            {
                return null;
            }

            agent.Enabled = enabled;
            await _agentRepository.SaveChangesAsync();

            return ToSummaryResponse(agent);
        }

        // 删除 Agent
        public async Task<bool> DeleteAsync(long agentId)
        {
            var agent = await _agentRepository.GetByIdAsync(agentId);
            if (agent == null)
            {
                return false;
            }
            await _agentRepository.DeleteAsync(agent);
            return true;
        }

        // 修改 Agent 的相关信息
        public async Task<AgentSummaryResponse?> ModifyAsync(ModifyAgentRequest request)
        {
            var agent = await _agentRepository.GetByIdAsync(request.Id);
            if (agent == null)
            {
                return null;
            }
            if (!string.IsNullOrWhiteSpace(request.AgentName))
            {
                agent.AgentName = request.AgentName.Trim();
            }
            if (!string.IsNullOrWhiteSpace(request.Scenario))
            {
                agent.Scenario = request.Scenario.Trim();
            }
            await _agentRepository.SaveChangesAsync();
            return ToSummaryResponse(agent);
        }

        // 根据请求头中的 Agent Key 执行完整鉴权
        public async Task<AgentAuthenticationResult> AuthenticateByAgentKeyAsync(string agentKey)
        {
            var agent = await FindAgentByKeyAsync(agentKey);

            if (agent == null)
            {
                return new AgentAuthenticationResult
                {
                    Succeeded = false,
                    Message = "API Key 不正确"
                };
            }

            if (!agent.Enabled)
            {
                return new AgentAuthenticationResult
                {
                    Succeeded = false,
                    Message = "Agent 已被禁用"
                };
            }

            agent.LastUsedAt = DateTime.Now;
            await _agentRepository.SaveChangesAsync();

            return new AgentAuthenticationResult
            {
                Succeeded = true,
                Agent = agent,
                Message = "鉴权通过"
            };
        }

        // 根据 AgentId 校验 Agent 是否存在且启用，供管理端测试安全接口时使用
        public async Task<AgentAuthenticationResult> AuthenticateByAgentIdAsync(long agentId)
        {
            var agent = await _agentRepository.GetByIdAsync(agentId);

            if (agent == null)
            {
                return new AgentAuthenticationResult
                {
                    Succeeded = false,
                    Message = "Agent 不存在"
                };
            }

            if (!agent.Enabled)
            {
                return new AgentAuthenticationResult
                {
                    Succeeded = false,
                    Message = "Agent 已被禁用"
                };
            }

            return new AgentAuthenticationResult
            {
                Succeeded = true,
                Agent = agent,
                Message = "鉴权通过"
            };
        }

        // 根据 Agent Key 指纹定位候选行，再用带盐哈希做最终校验
        private async Task<AgentApp?> FindAgentByKeyAsync(string agentKey)
        {
            var fingerprint = HashFingerprint(agentKey);
            var agent = await _agentRepository.GetByKeyFingerprintAsync(fingerprint);

            if (agent == null)
            {
                return null;
            }

            // 指纹只用于索引定位，最终仍用带盐哈希确认明文 Key 是否正确。
            return VerifySecret(agentKey, agent.AgentKeySalt, agent.AgentKeyHash)
                ? agent
                : null;
        }

        // 校验注册请求必填字段
        private static void ValidateRegisterRequest(RegisterAgentRequest request)
        {
            if (request == null)
            {
                throw new ArgumentException("请求体不能为空");
            }

            if (string.IsNullOrWhiteSpace(request.AgentName))
            {
                throw new ArgumentException("Agent 名称不能为空");
            }
        }

        // 生成以 ak_ 为前缀的随机 Agent Key
        private static string GenerateAgentKey()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);

            return $"ak_{ToBase64Url(bytes)}";
        }

        // 生成随机盐值
        private static string GenerateSalt()
        {
            Span<byte> bytes = stackalloc byte[16];
            RandomNumberGenerator.Fill(bytes);

            return ToBase64Url(bytes);
        }

        // 使用 SHA256 对 salt:secret 格式的字符串进行哈希
        private static string HashSecret(string secret, string salt)
        {
            var bytes = Encoding.UTF8.GetBytes($"{salt}:{secret}");
            var hash = SHA256.HashData(bytes);

            return Convert.ToHexString(hash);
        }

        // 使用 SHA256 对 Agent Key 明文生成可索引指纹
        private static string HashFingerprint(string secret)
        {
            var bytes = Encoding.UTF8.GetBytes(secret);
            var hash = SHA256.HashData(bytes);

            return Convert.ToHexString(hash);
        }

        // 验证明文密钥与盐值组合后的哈希是否与预期一致
        private static bool VerifySecret(string secret, string salt, string expectedHash)
        {
            var actualHash = HashSecret(secret, salt);

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(actualHash),
                Encoding.UTF8.GetBytes(expectedHash));
        }

        // 将字节数组转换为 Base64Url 编码字符串
        private static string ToBase64Url(ReadOnlySpan<byte> bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        // 将 Agent 实体转换为前端展示 DTO
        private static AgentSummaryResponse ToSummaryResponse(AgentApp agent)
        {
            return new AgentSummaryResponse
            {
                AgentId = agent.Id,
                AgentName = agent.AgentName,
                Scenario = agent.Scenario,
                Enabled = agent.Enabled,
                CreatedAt = agent.CreatedAt,
                LastUsedAt = agent.LastUsedAt,
                AgentKeyPreview = agent.AgentKeyPreview
            };
        }

        // 将 Agent Key 明文转换为只展示前后4位的预览字符串
        private static string ToAgentKeyPreview(string agentKey)
        {
            const string prefix = "ak_";

            if (!agentKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return "**********";
            }

            var body = agentKey[prefix.Length..];

            if (body.Length <= 7)
            {
                return $"{prefix}**********";
            }

            return $"{prefix}{body[..4]}**********{body[^3..]}";
        }
    }
}
