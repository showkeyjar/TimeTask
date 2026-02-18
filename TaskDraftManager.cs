using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace TimeTask
{
    /// <summary>
    /// 任务草稿管理器
    /// - 存储后台识别到的潜在任务
    /// - 支持草稿的添加、读取、删除
    /// - 自动清理过期草稿
    /// </summary>
    public class TaskDraftManager : IDisposable
    {
        public static event Action DraftsChanged;

        private readonly string _draftsFilePath;
        private readonly object _lock = new object();
        private List<TaskDraft> _drafts = new List<TaskDraft>();
        private const int MaxDrafts = 10;          // 最多保留10个草稿
        private const int DraftRetentionDays = 7; // 保留7天

        public TaskDraftManager()
        {
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TimeTask"
            );
            Directory.CreateDirectory(appDataPath);
            _draftsFilePath = Path.Combine(appDataPath, "task_drafts.json");

            LoadDrafts();
        }

        /// <summary>
        /// 添加新草稿
        /// </summary>
        public void AddDraft(TaskDraft draft)
        {
            if (draft == null || string.IsNullOrWhiteSpace(draft.RawText))
                return;

            lock (_lock)
            {
                // 多实例并存时，先同步磁盘中的最新状态，避免旧内存覆盖已处理草稿。
                LoadDrafts();

                // 检查是否已存在相似草稿
                var existing = _drafts.FirstOrDefault(d =>
                    d.RawText.Length > 10 &&
                    (d.RawText.Contains(draft.RawText) || draft.RawText.Contains(d.RawText))
                );

                if (existing != null)
                {
                    // 更新已有草稿的时间戳
                    existing.LastDetected = DateTime.Now;
                    existing.DetectionCount++;
                    SaveDrafts();
                    NotifyDraftsChanged();
                    return;
                }

                // 添加新草稿
                draft.Id = Guid.NewGuid().ToString("N").Substring(0, 12);
                draft.CreatedAt = DateTime.Now;
                draft.LastDetected = DateTime.Now;
                draft.DetectionCount = 1;
                draft.IsProcessed = false;

                _drafts.Insert(0, draft); // 最新在前

                // 限制数量
                if (_drafts.Count > MaxDrafts)
                {
                    _drafts = _drafts.Take(MaxDrafts).ToList();
                }

                // 清理过期草稿
                CleanupOldDrafts();

                SaveDrafts();
                NotifyDraftsChanged();

                // 检查是否需要触发通知
                CheckAndNotify();
            }
        }

        /// <summary>
        /// 获取所有未处理的草稿
        /// </summary>
        public List<TaskDraft> GetUnprocessedDrafts()
        {
            lock (_lock)
            {
                return _drafts.Where(d => !d.IsProcessed).ToList();
            }
        }

        /// <summary>
        /// 标记草稿为已处理
        /// </summary>
        public void MarkAsProcessed(string draftId)
        {
            lock (_lock)
            {
                LoadDrafts();
                var draft = _drafts.FirstOrDefault(d => d.Id == draftId);
                if (draft != null)
                {
                    draft.IsProcessed = true;
                    SaveDrafts();
                    NotifyDraftsChanged();
                }
            }
        }

        /// <summary>
        /// 更新草稿内容（例如 LLM 重算象限后）
        /// </summary>
        public void UpdateDraft(TaskDraft updated)
        {
            if (updated == null || string.IsNullOrWhiteSpace(updated.Id))
                return;

            lock (_lock)
            {
                LoadDrafts();
                var draft = _drafts.FirstOrDefault(d => d.Id == updated.Id);
                if (draft == null)
                    return;

                draft.CleanedText = updated.CleanedText;
                draft.Importance = updated.Importance;
                draft.Urgency = updated.Urgency;
                draft.EstimatedQuadrant = updated.EstimatedQuadrant;
                draft.LastDetected = DateTime.Now;
                SaveDrafts();
                NotifyDraftsChanged();
            }
        }

        /// <summary>
        /// 删除草稿
        /// </summary>
        public void DeleteDraft(string draftId)
        {
            lock (_lock)
            {
                LoadDrafts();
                _drafts = _drafts.Where(d => d.Id != draftId).ToList();
                SaveDrafts();
                NotifyDraftsChanged();
            }
        }

        /// <summary>
        /// 清空所有草稿
        /// </summary>
        public void ClearAll()
        {
            lock (_lock)
            {
                LoadDrafts();
                _drafts.Clear();
                SaveDrafts();
                NotifyDraftsChanged();
            }
        }

        /// <summary>
        /// 获取草稿数量
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _drafts.Count;
                }
            }
        }

        /// <summary>
        /// 获取未处理草稿数量
        /// </summary>
        public int UnprocessedCount
        {
            get
            {
                lock (_lock)
                {
                    return _drafts.Count(d => !d.IsProcessed);
                }
            }
        }

        public void Refresh()
        {
            lock (_lock)
            {
                LoadDrafts();
            }
        }

        private void LoadDrafts()
        {
            try
            {
                if (File.Exists(_draftsFilePath))
                {
                    string json = File.ReadAllText(_draftsFilePath);
                    _drafts = JsonSerializer.Deserialize<List<TaskDraft>>(json) ?? new List<TaskDraft>();

                    // 清理过期草稿
                    CleanupOldDrafts();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TaskDraftManager: Failed to load drafts: {ex.Message}");
                _drafts = new List<TaskDraft>();
            }
        }

        private void SaveDrafts()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_drafts, options);
                File.WriteAllText(_draftsFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TaskDraftManager: Failed to save drafts: {ex.Message}");
            }
        }

        private void CleanupOldDrafts()
        {
            var cutoff = DateTime.Now.AddDays(-DraftRetentionDays);
            _drafts = _drafts.Where(d => d.CreatedAt > cutoff).ToList();
        }

        private void CheckAndNotify()
        {
            int unprocessed = UnprocessedCount;

            // 草稿累积到3个时，通过托盘图标提示
            if (unprocessed >= 3 && unprocessed < MaxDrafts)
            {
                // 通知 NotificationManager
                // 这里只是标记，具体的通知由 NotificationManager 处理
                Console.WriteLine($"TaskDraftManager: {unprocessed} unprocessed drafts accumulated.");
            }
        }

        public void Dispose()
        {
            SaveDrafts();
        }

        private static void NotifyDraftsChanged()
        {
            try
            {
                DraftsChanged?.Invoke();
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// 任务草稿数据模型
    /// </summary>
    public class TaskDraft
    {
        public string Id { get; set; }
        public string RawText { get; set; }          // 原始识别文本
        public string CleanedText { get; set; }      // 清理后的任务描述
        public DateTime? ReminderTime { get; set; }  // 解析出的提醒时间
        public string ReminderHintText { get; set; } // 原始时间短语提示
        public string EstimatedQuadrant { get; set; } // 基于关键词估计的象限
        public string Importance { get; set; }
        public string Urgency { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastDetected { get; set; }
        public int DetectionCount { get; set; }
        public bool IsProcessed { get; set; }        // 是否已被处理（确认或删除）
        public string Source { get; set; }           // 来源：voice, screen, manual
    }
}
