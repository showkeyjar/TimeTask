using System;
using System.Configuration;
using System.Threading;
using System.Threading.Tasks;

namespace TimeTask
{
    /// <summary>
    /// LLM 调用的瞬时失败重试策略（设计评审路线图第 3 项遗留：此前完全没有 retry——
    /// 会议中每 60s 一次的状态精修、停止后的行动项抽取，遇到一次网络抖动就整次丢失）。
    ///
    /// LlmService 的既有契约是「失败返回 Error 字符串、不抛异常」，因此按结果文本分类：
    /// - 只重试瞬时类失败：超时 / 断连 / 限流 / 服务端 5xx；
    /// - 绝不重试：调用方取消、鉴权失败（401/403）、响应解析失败、配置错误——重试必然复现；
    /// - 未知错误默认不重试（宁可保守，避免把 bug 放大成三倍调用量）。
    /// 正常返回的内容（哪怕碰巧含「超时/连接」字样）绝不重试——只有形如
    /// "Error from …" / "LLM dummy response …" 的错误壳才会进入判定。
    /// </summary>
    public static class LlmRetryPolicy
    {
        /// <summary>豁免标记（先于瞬时判定）：这些出现即不重试。</summary>
        private static readonly string[] NonRetryableMarkers =
        {
            "cancelled by caller",      // Betalgo 路径调用方取消
            "request cancelled",        // 智谱路径调用方取消
            "被调用方取消",
            "401", "403",               // 鉴权失败：重试无意义
            "invalid response format",  // 响应格式不符：网关/模型行为，重试大概率复现
            "could not parse",          // 解析失败
            "configuration error",      // 配置缺失（如 Key 占位符）
            "api key"                   // Key 类问题
        };

        /// <summary>瞬时失败标记：值得再试一次。</summary>
        private static readonly string[] TransientMarkers =
        {
            "timed out", "timeout", "超时",                          // 超时（HttpClient 超时归入此类）
            "http 408", "http 429",                                  // 请求超时 / 限流
            "http 5",                                                // 5xx 服务端错误（500/502/503/504/520-599 全兜住）
            "httprequestexception", "socketexception",
            "connection refused", "connection reset", "connection closed",
            "unable to connect", "no such host", "name or service not known",
            "发送请求时出错", "连接", "网络",                          // 中文本地化的网络异常文案
            "server_error", "server error", "temporarily unavailable", "overloaded"
        };

        /// <summary>重试次数上限（含首次）：App.config 的 LlmRetryMaxAttempts，默认 3，钳制 1..6。</summary>
        public static int ReadMaxAttempts()
        {
            try
            {
                var raw = ConfigurationManager.AppSettings["LlmRetryMaxAttempts"];
                if (int.TryParse(raw, out int n))
                {
                    return Math.Max(1, Math.Min(6, n));
                }
            }
            catch { }
            return 3;
        }

        /// <summary>
        /// 判定一次 LLM 调用结果是否值得重试。null/空白视为偶发截断（可重试）；
        /// 非「错误壳」开头的正常内容一律不重试。
        /// </summary>
        public static bool IsRetryable(string result)
        {
            if (string.IsNullOrWhiteSpace(result)) return true;

            string s = result.Trim();
            bool looksLikeError = s.StartsWith("Error from", StringComparison.OrdinalIgnoreCase)
                               || s.StartsWith("LLM dummy response", StringComparison.OrdinalIgnoreCase);
            if (!looksLikeError) return false; // 正常内容（哪怕含“超时”字样）绝不重试

            string lower = s.ToLowerInvariant();
            foreach (var marker in NonRetryableMarkers)
            {
                if (lower.Contains(marker)) return false;
            }
            foreach (var marker in TransientMarkers)
            {
                if (lower.Contains(marker)) return true;
            }
            return false; // 未知错误：保守起见不重试
        }

        /// <summary>第 failedAttempts 次失败后的退避：1s、2s、4s…（上限 8s）+ 少量抖动防同步重试。</summary>
        public static TimeSpan DelayForAttempt(int failedAttempts)
        {
            int shift = Math.Max(0, Math.Min(3, failedAttempts - 1)); // 1→1s, 2→2s, 3→4s, ≥4→8s
            double seconds = Math.Min(8, Math.Pow(2, shift));
            double jitter = (failedAttempts * 0.137) % 0.3; // 确定性抖动（可测试），≤300ms
            return TimeSpan.FromSeconds(seconds + jitter);
        }

        /// <summary>
        /// 执行带重试的调用。attempt 返回 Error 字符串或正常内容；重试判定见 IsRetryable。
        /// 取消信号在两次尝试之间生效（延迟等待被打断时立即返回最后一次结果，不抛异常）。
        /// delayer 供测试注入零延迟。
        /// </summary>
        public static async Task<string> ExecuteAsync(
            Func<CancellationToken, Task<string>> attempt,
            int maxAttempts,
            CancellationToken cancellationToken,
            Func<TimeSpan, CancellationToken, Task> delayer = null)
        {
            if (attempt == null) throw new ArgumentNullException(nameof(attempt));
            if (delayer == null) delayer = (d, ct) => Task.Delay(d, ct);
            if (maxAttempts < 1) maxAttempts = 1;

            string last = null;
            for (int attemptNo = 1; ; attemptNo++)
            {
                last = await attempt(cancellationToken).ConfigureAwait(false);
                if (attemptNo >= maxAttempts || !IsRetryable(last))
                {
                    return last;
                }

                VoiceRuntimeLog.Info($"LLM 瞬时失败，准备第 {attemptNo + 1}/{maxAttempts} 次尝试：{Truncate(last)}");

                try
                {
                    await delayer(DelayForAttempt(attemptNo), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 等待期间调用方取消：立即返回最后一次结果（保持「不抛异常」契约）
                    return last;
                }
            }
        }

        private static string Truncate(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= 120 ? s : s.Substring(0, 120) + "…";
        }
    }
}
