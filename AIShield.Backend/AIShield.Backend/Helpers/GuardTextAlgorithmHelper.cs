using System.Text;
using System.Text.RegularExpressions;

namespace AIShield.Backend.Services
{
    internal static partial class GuardTextAlgorithmHelper
    {
        // 将文本归一化为适合安全匹配和相似度计算的形式。
        public static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            return WhitespaceRegex().Replace(normalized, " ").Trim();
        }

        // 提取英文单词、数字和中文双字片段，兼顾中英文短文本。
        public static HashSet<string> Tokenize(string? text)
        {
            var normalized = Normalize(text);
            var tokens = new HashSet<string>(StringComparer.Ordinal);  // 使用HashSet做去重

            foreach (Match match in WordRegex().Matches(normalized))
            {
                if (match.Value.Length > 1 && !StopWords.Contains(match.Value))
                {
                    tokens.Add(match.Value);
                }
            }

            var chinese = new string(normalized.Where(IsChinese).ToArray());
            for (var index = 0; index < chinese.Length - 1; index++)
            {
                var token = chinese.Substring(index, 2);
                if (!StopWords.Contains(token))
                {
                    tokens.Add(token);
                }
            }

            return tokens;
        }

        // 计算两个文本集合的 Jaccard 相似度。
        public static double Jaccard(IReadOnlySet<string> left, IReadOnlySet<string> right)
        {
            if (left.Count == 0 || right.Count == 0)
            {
                return 0;
            }

            var intersection = left.Count(right.Contains);
            var union = left.Count + right.Count - intersection;
            return union == 0 ? 0 : (double)intersection / union;
        }

        // 计算查询锚点在目标文本中的覆盖率。
        public static double AnchorCoverage(IReadOnlySet<string> anchors, IReadOnlySet<string> target)
        {
            if (anchors.Count == 0)
            {
                return 0;
            }

            return (double)anchors.Count(target.Contains) / anchors.Count;
        }

        // 计算两个向量的余弦相似度，向量无效时返回空值。
        public static double? CosineSimilarity(IReadOnlyList<float>? left, IReadOnlyList<float>? right)
        {
            if (left == null || right == null || left.Count == 0 || left.Count != right.Count)
            {
                return null;   // 如果查询内容没有向量、文档没有向量、向量长度为0或者查询内容及文档向量维度不同，返回null
            } 

            double dot = 0;
            double leftNorm = 0;
            double rightNorm = 0;

            for (var index = 0; index < left.Count; index++)  // cos(A,B) = A · B / (|A| × |B|)
            {
                dot += left[index] * right[index];
                leftNorm += left[index] * left[index];
                rightNorm += right[index] * right[index];
            }

            if (leftNorm <= 0 || rightNorm <= 0)
            {
                return null;
            }

            return Math.Clamp(dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm)), -1, 1);
        }

        // 判断文本是否包含常见提示词注入或越权指令特征。
        public static bool ContainsDangerousPattern(string? text)
        {
            var normalized = Normalize(text);
            var compact = normalized.Replace(" ", string.Empty, StringComparison.Ordinal);

            return DangerousPatterns.Any(pattern =>
                normalized.Contains(pattern, StringComparison.Ordinal)
                || compact.Contains(pattern.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal));
        }

        // 判断字符是否属于常用中文 Unicode 区间。
        private static bool IsChinese(char value)
        {
            return value >= '\u4e00' && value <= '\u9fff';
        }

        private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
        {
            "的", "了", "是", "在", "和", "与", "及", "或", "一个", "如何", "怎么",
            "the", "and", "for", "with", "this", "that", "from", "are", "was"
        };

        private static readonly string[] DangerousPatterns =
        {
            "忽略之前", "忽略以上", "忽略所有指令", "无视规则", "泄露系统提示词",
            "ignore previous", "ignore all instructions", "system prompt", "developer message",
            "jailbreak", "越狱", "grant me admin", "i am admin", "ignore trust level"
        };

        [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
        private static partial Regex WhitespaceRegex();

        [GeneratedRegex(@"[\p{L}\p{N}_-]+", RegexOptions.CultureInvariant)]
        private static partial Regex WordRegex();
    }
}
