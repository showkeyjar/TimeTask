using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Web.Script.Serialization;

namespace TimeTask
{
    public sealed class VoiceRecognitionQualityReport
    {
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
        public int SessionCount { get; set; }
        public int SegmentCount { get; set; }
        public int AcceptedTaskLikeSegments { get; set; }
        public int ReminderLikeSegments { get; set; }
        public int QuestionLikeSegments { get; set; }
        public int LongFreeformSegments { get; set; }
        public int ActiveTaskCount { get; set; }
        public int LikelyNoisyActiveTaskCount { get; set; }
        public List<string> AcceptedExamples { get; set; } = new List<string>();
        public List<string> RejectedExamples { get; set; } = new List<string>();
        public List<string> LikelyNoisyTaskExamples { get; set; } = new List<string>();
    }

    public sealed class VoiceRecognitionQualityEngine
    {
        private readonly string _sessionsPath;
        private readonly string _jsonPath;
        private readonly string _mdPath;

        public VoiceRecognitionQualityEngine(string dataPath)
        {
            string strategyPath = Path.Combine(dataPath, "strategy");
            Directory.CreateDirectory(strategyPath);
            _sessionsPath = Path.Combine(dataPath, "conversations", "sessions.json");
            _jsonPath = Path.Combine(strategyPath, "voice_recognition_quality.json");
            _mdPath = Path.Combine(strategyPath, "voice_recognition_quality.md");
        }

        public VoiceRecognitionQualityReport BuildAndPersist(List<ItemGrid> allTasks, DateTime now)
        {
            var report = BuildReport(allTasks, now);
            Persist(report);
            return report;
        }

        public VoiceRecognitionQualityReport BuildReport(List<ItemGrid> allTasks, DateTime now)
        {
            var sessions = LoadSessions();
            var recognizer = new IntentRecognizer();
            var analyses = sessions
                .SelectMany(s => s.Segments ?? new List<ConversationSegment>())
                .Select(seg => new
                {
                    Text = (seg?.RecognizedText ?? string.Empty).Trim(),
                    Analysis = TaskTextQualityHelper.AnalyzeVoiceTaskCandidate(seg?.RecognizedText),
                    IsPotentialTask = recognizer.IsPotentialTask(seg?.RecognizedText ?? string.Empty),
                    ReminderLike = recognizer.IsReminderLike(seg?.RecognizedText ?? string.Empty)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .ToList();

            var activeTasks = (allTasks ?? new List<ItemGrid>())
                .Where(t => t != null && t.IsActive)
                .ToList();

            var likelyNoisyTasks = activeTasks
                .Where(t => !TaskTextQualityHelper.IsMeaningfulTaskText(t.Task))
                .Select(t => t.Task)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();

            return new VoiceRecognitionQualityReport
            {
                GeneratedAt = now,
                SessionCount = sessions.Count,
                SegmentCount = analyses.Count,
                AcceptedTaskLikeSegments = analyses.Count(x => x.Analysis.IsMeaningfulTask && (x.IsPotentialTask || x.ReminderLike || x.Analysis.StructureScore >= 0.45)),
                ReminderLikeSegments = analyses.Count(x => x.ReminderLike || x.Analysis.IsReminderCandidate),
                QuestionLikeSegments = analyses.Count(x => x.Analysis.IsQuestionLike),
                LongFreeformSegments = analyses.Count(x => x.Analysis.IsLongFreeformSpeech),
                ActiveTaskCount = activeTasks.Count,
                LikelyNoisyActiveTaskCount = activeTasks.Count(t => !TaskTextQualityHelper.IsMeaningfulTaskText(t.Task)),
                AcceptedExamples = analyses
                    .Where(x => x.Analysis.IsMeaningfulTask && (x.IsPotentialTask || x.ReminderLike || x.Analysis.StructureScore >= 0.45))
                    .Select(x => x.Text)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(8)
                    .ToList(),
                RejectedExamples = analyses
                    .Where(x => !x.Analysis.IsMeaningfulTask)
                    .Select(x => x.Text)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(8)
                    .ToList(),
                LikelyNoisyTaskExamples = likelyNoisyTasks
            };
        }

        private List<ConversationSession> LoadSessions()
        {
            try
            {
                if (!File.Exists(_sessionsPath))
                {
                    return new List<ConversationSession>();
                }

                string json = File.ReadAllText(_sessionsPath);
                var serializer = new JavaScriptSerializer();
                return serializer.Deserialize<List<ConversationSession>>(json) ?? new List<ConversationSession>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"VoiceRecognitionQualityEngine load failed: {ex.Message}");
                return new List<ConversationSession>();
            }
        }

        private void Persist(VoiceRecognitionQualityReport report)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_jsonPath, JsonSerializer.Serialize(report, options));

                var sb = new StringBuilder();
                sb.AppendLine("# 语音识别质量报告");
                sb.AppendLine();
                sb.AppendLine($"- 生成时间: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"- 会话数: {report.SessionCount}");
                sb.AppendLine($"- 识别片段数: {report.SegmentCount}");
                sb.AppendLine($"- 当前规则判定为任务/提醒的片段: {report.AcceptedTaskLikeSegments}");
                sb.AppendLine($"- 提醒类片段: {report.ReminderLikeSegments}");
                sb.AppendLine($"- 疑问句片段: {report.QuestionLikeSegments}");
                sb.AppendLine($"- 长段自由表达片段: {report.LongFreeformSegments}");
                sb.AppendLine($"- 活跃任务数: {report.ActiveTaskCount}");
                sb.AppendLine($"- 疑似噪声活跃任务数: {report.LikelyNoisyActiveTaskCount}");
                sb.AppendLine();
                sb.AppendLine("## 通过样例");
                foreach (string item in report.AcceptedExamples)
                {
                    sb.AppendLine($"- {item}");
                }
                sb.AppendLine();
                sb.AppendLine("## 拒绝样例");
                foreach (string item in report.RejectedExamples)
                {
                    sb.AppendLine($"- {item}");
                }
                sb.AppendLine();
                sb.AppendLine("## 疑似噪声活跃任务");
                foreach (string item in report.LikelyNoisyTaskExamples)
                {
                    sb.AppendLine($"- {item}");
                }

                File.WriteAllText(_mdPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"VoiceRecognitionQualityEngine persist failed: {ex.Message}");
            }
        }
    }
}
