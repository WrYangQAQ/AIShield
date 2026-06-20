using AIShield.Backend.Dtos;

namespace AIShield.Backend.Services
{
    public class RagRerankService
    {
        private readonly IConfiguration _configuration;

        public RagRerankService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // 对 RAG 候选执行注入过滤、混合相关性评分、信任度加权和降序重排。
        public GuardAlgorithmResult<RagRerankDetails> Rerank(RagRerankRequest request)
        {
            var requestId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return BuildFailure(requestId, "查询内容不能为空。", "missing_query");
            }

            var minimumScore = Math.Clamp(         // 从请求体获取文档最低保留分数，如没有则从环境json变量读取，如果未配置json则默认为0.2
                request.MinimumScore
                ?? _configuration.GetValue("GuardAlgorithms:Rag:MinimumScore", 0.2d),   
                0,
                1); // 将最低分数限制在[0,1]区间
            var maxResults = Math.Clamp(request.MaxResults, 1, 100);     // 从请求体读取最多返回候选数量，并限制在[1,100]区间，确定最终返回多少条
            var queryTokens = GuardTextAlgorithmHelper.Tokenize(request.Query);   // 对用户查询文本做格式化及token分词，返回去重后的token集合
            var results = new List<RagRerankItem>();
            var filteredUnsafeCount = 0;                      // 过滤的不安全文档数量
            var hasSemanticScore = false;

            foreach (var candidate in request.Candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate.Content)     // 判断候选文档内容是否为空及是否投毒
                    || GuardTextAlgorithmHelper.ContainsDangerousPattern(candidate.Content))
                {
                    filteredUnsafeCount++;
                    continue;
                }

                // 文本相关性作为所有场景都可用的基础分，向量缺失时不会直接丢弃候选。
                var lexicalScore = GuardTextAlgorithmHelper.Jaccard(        // 交集数 / 并集数
                    queryTokens,
                    GuardTextAlgorithmHelper.Tokenize(candidate.Content));

                // 计算向量语义相关性（单用Jaccard会导致过严的匹配机制）
                var semanticScore = GuardTextAlgorithmHelper.CosineSimilarity(
                    request.QueryEmbedding,
                    candidate.Embedding);

                var trustScore = GetTrustScore(candidate.Source);  // 将来源转化为相应置信度的值
                double finalScore;

                if (semanticScore.HasValue)
                {
                    hasSemanticScore = true;   // 成功计算一次向量分数
                    var normalizedSemantic = Math.Clamp((semanticScore.Value + 1) / 2, 0, 1);    // 将向量分数限制在[0,1]区间
                    finalScore = normalizedSemantic * 0.7 + lexicalScore * 0.2 + trustScore * 0.1;  // 计算召回文档最终得分（有向量）
                    //           语义得分70%                  词法得分20%          来源信任度得分10%
                }
                else
                {
                    // 无向量时明确进入词法降级模式，避免伪装成完整语义重排。
                    finalScore = lexicalScore * 0.85 + trustScore * 0.15;
                }

                if (finalScore < minimumScore)
                {
                    continue;
                }

                results.Add(new RagRerankItem
                {
                    Id = candidate.Id,
                    Content = candidate.Content,
                    Source = NormalizeSource(candidate.Source),
                    Score = Math.Round(finalScore, 6),
                    SemanticScore = semanticScore.HasValue
                        ? Math.Round(semanticScore.Value, 6)
                        : null,
                    LexicalScore = Math.Round(lexicalScore, 6)
                });
            }

            var details = new RagRerankDetails
            {
                Mode = hasSemanticScore ? "hybrid" : "lexical_fallback",
                FilteredUnsafeCount = filteredUnsafeCount,        // 过滤的不安全文档数量
                Kept = results                                    // Linq查询
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Id, StringComparer.Ordinal)
                    .Take(maxResults)                             // 最大返回条数，只取 maxResults 条
                    .ToList()
            };

            return new GuardAlgorithmResult<RagRerankDetails>
            {
                Success = true,
                Message = "ok",
                Data = new GuardAlgorithmData<RagRerankDetails>
                {
                    RequestId = requestId,
                    Blocked = false,
                    Details = details
                }
            };
        }

        // 根据服务端配置取得来源信任分，调用方不能直接传入任意权重。
        private double GetTrustScore(string? source)
        {
            var normalized = NormalizeSource(source);
            var defaultScore = normalized switch
            {
                "system" => 1.0,
                "admin" => 0.9,
                "user" => 0.7,
                _ => 0.5
            };

            return Math.Clamp(
                _configuration.GetValue($"GuardAlgorithms:Rag:TrustScores:{normalized}", defaultScore),
                0,
                1);
        }

        // 归一化候选来源，未知值统一按 unknown 处理。
        private static string NormalizeSource(string? source)
        {
            var normalized = source?.Trim().ToLowerInvariant();
            return normalized is "system" or "admin" or "user" ? normalized : "unknown";
        }

        // 构造重排失败结果。
        private static GuardAlgorithmResult<RagRerankDetails> BuildFailure(
            string requestId,
            string message,
            string riskLabel)
        {
            return new GuardAlgorithmResult<RagRerankDetails>
            {
                Success = false,
                Message = message,
                Data = new GuardAlgorithmData<RagRerankDetails>
                {
                    RequestId = requestId,
                    Blocked = true,
                    RiskLabel = riskLabel
                }
            };
        }
    }
}
