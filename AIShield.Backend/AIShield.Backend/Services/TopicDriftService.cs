using AIShield.Backend.Dtos;

namespace AIShield.Backend.Services
{
    public class TopicDriftService
    {
        private readonly IConfiguration _configuration;

        public TopicDriftService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // 检测连续生成片段是否偏离原始问题，综合锚点覆盖率和文本片段相似度进行判断。
        public GuardAlgorithmResult<TopicDriftDetails> Check(TopicDriftRequest request)
        {
            var requestId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return BuildFailure(requestId, "原始问题不能为空。", "missing_query");
            }

            if (request.Segments == null)
            {
                return BuildFailure(requestId, "生成片段不能为空。", "missing_segments");
            }

            // 从配置文件中读取相关参数
            var maxConsecutiveDrift = Math.Clamp(  // 连续漂移阈值（最多允许连续出现多少个低相关片段）
                request.MaxConsecutiveDrift
                ?? _configuration.GetValue("GuardAlgorithms:TopicDrift:MaxConsecutiveDrift", 3),
                1,
                20);
            var similarityThreshold = Math.Clamp(  // 相似度最低阈值（片段与用户查询内容相似值）
                request.SimilarityThreshold
                ?? _configuration.GetValue("GuardAlgorithms:TopicDrift:SimilarityThreshold", 0.12d),
                0,
                1);
            var minimumSegmentLength = Math.Max(  // 最短有效片段长度
                1,
                _configuration.GetValue("GuardAlgorithms:TopicDrift:MinimumSegmentLength", 8));

            var anchors = GuardTextAlgorithmHelper.Tokenize(request.Query); // 对用户查询内容做分词处理，返回处理后的token集合
            var details = new TopicDriftDetails
            {
                MaxConsecutiveDrift = maxConsecutiveDrift,
                Anchors = anchors.Take(30).ToList()
            };
            var consecutiveDrift = 0; // 初始化漂移次数

            for (var index = 0; index < request.Segments.Count; index++)
            {
                string segment = request.Segments[index] ?? string.Empty;

                // 对片段分词 → 计算覆盖率（70%权重） → 计算token Jaccard（30%权重）
                var segmentTokens = GuardTextAlgorithmHelper.Tokenize(segment);
                var anchorCoverage = GuardTextAlgorithmHelper.AnchorCoverage(anchors, segmentTokens);  // 锚点覆盖率 = 片段中出现的原问题锚点数量 / 原问题锚点总数 
                var tokenSimilarity = GuardTextAlgorithmHelper.Jaccard(anchors, segmentTokens);

                // 锚点覆盖更能反映是否仍围绕原问题，Jaccard 用于补充衡量整体词元相似度。
                var score = anchorCoverage * 0.7 + tokenSimilarity * 0.3;
                var lowRelevance = segment.Trim().Length >= minimumSegmentLength  // 判定单个片段是否是低相关
                    && score < similarityThreshold;   // 满足条件：1.片段长度至少达到最短长度 2.相关度低于阈值

                if (lowRelevance)
                {
                    consecutiveDrift++;
                }
                else if (segment.Trim().Length >= minimumSegmentLength)
                {
                    consecutiveDrift = 0;  // 如果过出现有效片段则技术清零重新计算
                }
                // ↑ 如果片段过短不增加也不清零计数

                details.Segments.Add(new TopicSegmentScore
                {
                    Index = index,
                    Score = Math.Round(score, 6),
                    LowRelevance = lowRelevance
                }); // 记录每个片段的检测结果

                if (consecutiveDrift >= maxConsecutiveDrift)  // 达到连续阈值后进行阻断
                {
                    details.Drifted = true;
                    details.DriftCount = consecutiveDrift;
                    return new GuardAlgorithmResult<TopicDriftDetails>
                    {
                        Success = false,
                        Message = "blocked_topic_drift",
                        Data = new GuardAlgorithmData<TopicDriftDetails>
                        {
                            RequestId = requestId,
                            Blocked = true,
                            RiskLabel = "topic_drift",
                            Details = details
                        }
                    };
                }
            }

            details.DriftCount = consecutiveDrift;
            return new GuardAlgorithmResult<TopicDriftDetails>
            {
                Success = true,
                Message = "ok",
                Data = new GuardAlgorithmData<TopicDriftDetails>
                {
                    RequestId = requestId,
                    Blocked = false,
                    Details = details
                }
            };
        }

        // 构造主题漂移检测失败结果。
        private static GuardAlgorithmResult<TopicDriftDetails> BuildFailure(
            string requestId,
            string message,
            string riskLabel)
        {
            return new GuardAlgorithmResult<TopicDriftDetails>
            {
                Success = false,
                Message = message,
                Data = new GuardAlgorithmData<TopicDriftDetails>
                {
                    RequestId = requestId,
                    Blocked = true,
                    RiskLabel = riskLabel
                }
            };
        }
    }
}
