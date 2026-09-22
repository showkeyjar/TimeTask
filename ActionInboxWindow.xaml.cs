using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NAudio.Wave;

namespace TimeTask
{
    /// <summary>
    /// 行动收件箱：把一场交流/会议中提取出的行动项展示给用户。
    /// 用户勾选并确认后，任务真正进入 TimeTask 四象限。
    /// 这是“现实流→行动”闭环的最后一环。
    ///
    /// 降级设计：即使本地 ASR 模型不可用，录音依然落盘。本窗口支持
    /// (1) 直接回放录音，(2) 手动录入/粘贴文本并复用同一套提取逻辑。
    /// 因此“没有 ASR”也能完成“录音→行动项”的完整闭环。
    /// </summary>
    public partial class ActionInboxWindow : Window
    {
        public class ActionItemVM : INotifyPropertyChanged
        {
            private bool _accepted = true;
            public bool Accepted
            {
                get => _accepted;
                set { _accepted = value; OnChanged(nameof(Accepted)); }
            }
            public string Text { get; set; }
            public string Quadrant { get; set; } = "重要不紧急";
            public DateTime? Reminder { get; set; }
            public float Confidence { get; set; }
            public string ConfidenceDisplay => $"{(Confidence * 100):0}%";
            public event PropertyChangedEventHandler PropertyChanged;
            private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        private static readonly string[] Quadrants =
            { "重要且紧急", "重要不紧急", "不重要但紧急", "不重要不紧急" };

        private readonly ObservableCollection<ActionItemVM> _items = new ObservableCollection<ActionItemVM>();
        private readonly ConversationCaptureService.ConversationCaptureResult _result;

        private readonly string _folder;
        private readonly string _micPath;
        private readonly string _sysPath;

        private WaveOutEvent _waveOut;
        private AudioFileReader _audioReader;

        public ActionInboxWindow(ConversationCaptureService.ConversationCaptureResult result)
        {
            _result = result ?? throw new ArgumentNullException(nameof(result));
            InitializeComponent();

            _folder = result.AudioFolderPath;
            _micPath = string.IsNullOrWhiteSpace(_folder) ? null : Path.Combine(_folder, "mic.wav");
            _sysPath = string.IsNullOrWhiteSpace(_folder) ? null : Path.Combine(_folder, "system.wav");

            QuadrantColumn.ItemsSource = Quadrants;
            Grid.ItemsSource = _items;

            TitleText.Text = result.Type == ConversationType.Meeting
                ? "会议记录 · 行动收件箱"
                : "交流记录 · 行动收件箱";

            var dur = result.EndTime - result.StartTime;
            string asrTag = result.AsrAvailable
                ? "实时转写：已启用"
                : "实时转写：未启用（可回放录音手动转写）";
            string refineTag = result.LlmRefined ? " · LLM上下文精炼" : string.Empty;
            MetaText.Text = $"类型：{result.Type}　时长：{dur.TotalMinutes:F0} 分钟　{asrTag}{refineTag}　识别行动项：{result.Actions.Count}";

            if (string.IsNullOrWhiteSpace(result.Summary))
            {
                // 收件箱为空时给出明确原因与可追溯信息，而不是一句“无内容”让人摸不着头脑。
                string reason = result.AsrAvailable
                    ? "本次录音未识别到有效语音内容，未抽取行动项。可检查麦克风/系统音频是否被静音后重新录制。"
                    : "本次录音未启用实时转写（ASR 模型仍在后台加载/下载，或不可用），因此没有可抽取的文字内容。\n" +
                      "不过录音已保存到磁盘：你可以点击上方“录音回放”听回内容，再用“手动录入文本”抽取行动项；或等模型就绪后重新录制。";
                string pathNote = string.IsNullOrWhiteSpace(_folder)
                    ? string.Empty
                    : $"\n\n录音文件已保存：{_folder}";
                TranscriptBox.Text = reason + pathNote;
            }
            else
            {
                TranscriptBox.Text = result.Summary;
            }

            // 音频面板：仅当存在录音文件时显示
            bool hasMic = _micPath != null && File.Exists(_micPath);
            bool hasSys = _sysPath != null && File.Exists(_sysPath);
            if (hasMic || hasSys)
            {
                AudioPanel.Visibility = Visibility.Visible;
                BtnPlayMic.Visibility = hasMic ? Visibility.Visible : Visibility.Collapsed;
                BtnPlaySystem.Visibility = hasSys ? Visibility.Visible : Visibility.Collapsed;
            }

            // 手动转写兜底：默认折叠；当没有任何内容时自动展开，引导用户走手动路径
            bool noContent = result.Actions.Count == 0 &&
                             (string.IsNullOrWhiteSpace(result.Summary) || result.Summary.StartsWith("本次"));
            if (noContent) ManualExpander.IsExpanded = true;

            foreach (var a in result.Actions)
            {
                _items.Add(new ActionItemVM
                {
                    Text = a.CleanedText ?? a.RawText,
                    Quadrant = a.EstimatedQuadrant ?? "重要不紧急",
                    Reminder = a.ReminderTime,
                    Confidence = (float)a.Confidence
                });
            }

            // 结构化会议纪要（会议助手实时提炼的结果）：即使没有文本转写，只要配了 LLM 也有要点
            if (result.MeetingState != null)
            {
                MinutesSummary.Text = string.IsNullOrWhiteSpace(result.MeetingState.Summary)
                    ? "（本次会议未提炼出结构化要点）"
                    : result.MeetingState.Summary;
                MinConcepts.ItemsSource = result.MeetingState.Concepts;
                MinQuestions.ItemsSource = result.MeetingState.Questions;
                MinDecisions.ItemsSource = result.MeetingState.Decisions;
                MinActions.ItemsSource = result.MeetingState.Actions;
            }
            else
            {
                MinutesSummary.Text = "（未启用实时提炼；可回放录音后用“手动录入文本”抽取行动项）";
            }

            if (_items.Count == 0)
            {
                BtnAcceptAll.IsEnabled = false;
                BtnAcceptSelected.IsEnabled = false;
            }

            Closed += (s, e) => StopPlayback();
        }

        // ---------- 录音回放（NAudio，非阻塞）----------

        private void BtnPlayMic_Click(object sender, RoutedEventArgs e) => StartPlayback(_micPath);
        private void BtnPlaySystem_Click(object sender, RoutedEventArgs e) => StartPlayback(_sysPath);
        private void BtnStopPlay_Click(object sender, RoutedEventArgs e) => StopPlayback();

        private void StartPlayback(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                PlayStatus.Text = "找不到对应的录音文件。";
                return;
            }
            try
            {
                StopPlayback();
                _audioReader = new AudioFileReader(path);
                _waveOut = new WaveOutEvent();
                _waveOut.PlaybackStopped += (s, ev) =>
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        BtnStopPlay.IsEnabled = false;
                        PlayStatus.Text = "播放结束。";
                    }));
                _waveOut.Init(_audioReader);
                _waveOut.Play();
                BtnStopPlay.IsEnabled = true;
                PlayStatus.Text = "正在播放：" + Path.GetFileName(path);
            }
            catch (Exception ex)
            {
                PlayStatus.Text = "播放失败：" + ex.Message;
            }
        }

        private void StopPlayback()
        {
            try { _waveOut?.Stop(); } catch { }
            try { _waveOut?.Dispose(); } catch { }
            try { _audioReader?.Dispose(); } catch { }
            _waveOut = null;
            _audioReader = null;
            BtnStopPlay.IsEnabled = false;
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_folder) || !Directory.Exists(_folder)) return;
            try { Process.Start(new ProcessStartInfo(_folder) { UseShellExecute = true }); }
            catch { }
        }

        // ---------- 手动转写兜底（离线、复用同一套提取逻辑）----------

        private void BtnClearManual_Click(object sender, RoutedEventArgs e)
        {
            ManualTextBox.Text = string.Empty;
        }

        private void BtnExtractFromText_Click(object sender, RoutedEventArgs e)
        {
            string text = ManualTextBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("请先输入或粘贴转写文本", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var drafts = App.Instance?.CaptureService?.ExtractActionsFromText(text) ?? new List<TaskDraft>();
            if (drafts.Count == 0)
            {
                MessageBox.Show("未从文本中识别出可抽取的行动项。可尝试按“一句一行”输入更明确的待办描述。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var d in drafts)
            {
                _items.Add(new ActionItemVM
                {
                    Text = d.CleanedText ?? d.RawText,
                    Quadrant = d.EstimatedQuadrant ?? "重要不紧急",
                    Reminder = d.ReminderTime,
                    Confidence = (float)d.Confidence
                });
            }

            BtnAcceptAll.IsEnabled = true;
            BtnAcceptSelected.IsEnabled = true;
            TranscriptBox.Text = text.Trim();
            PlayStatus.Text = $"已从手动文本抽取 {drafts.Count} 个行动项，请勾选后接受。";
        }

        // ---------- 接受 / 忽略（与自动转写完全一致）----------

        private void BtnAcceptAll_Click(object sender, RoutedEventArgs e) => Accept(acceptSelectedOnly: false);
        private void BtnAcceptSelected_Click(object sender, RoutedEventArgs e) => Accept(acceptSelectedOnly: true);

        private void Accept(bool acceptSelectedOnly)
        {
            var toAdd = acceptSelectedOnly
                ? _items.Where(i => i.Accepted).ToList()
                : _items.ToList();

            if (toAdd.Count == 0)
            {
                MessageBox.Show("没有可接受的行动项", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int added = 0;

            foreach (var item in toAdd)
            {
                try
                {
                    int csvNumber = QuadrantToCsvNumber(item.Quadrant);
                    var (imp, urg) = QuadrantToImportanceUrgency(item.Quadrant);
                    DateTime? reminder = item.Reminder.HasValue
                        ? item.Reminder.Value.Date.AddHours(9)
                        : (DateTime?)null;

                    var newItem = new ItemGrid
                    {
                        Task = item.Text ?? string.Empty,
                        Importance = imp,
                        Urgency = urg,
                        IsActive = true,
                        CreatedDate = DateTime.Now,
                        LastModifiedDate = DateTime.Now,
                        ReminderTime = reminder,
                        IsActiveInQuadrant = true,
                        InactiveWarningCount = 0,
                        Result = string.Empty,
                        SourceTaskID = "action-inbox"
                    };

                    // 统一走 QuadrantStore：顶部插入 + 全象限评分规则 + 原子落盘，与主窗口完全一致
                    QuadrantStore.InsertTop(csvNumber, newItem);
                    added++;
                }
                catch (Exception ex)
                {
                    VoiceRuntimeLog.Error($"行动项写入四象限失败：{item.Text}", ex);
                }
            }

            RefreshMainGrid();
            MessageBox.Show($"已接受 {added} 个行动项并加入四象限。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }

        private void BtnDismiss_Click(object sender, RoutedEventArgs e) => Close();

        private static int QuadrantToCsvNumber(string quadrant)
        {
            switch (quadrant)
            {
                case "重要且紧急": return 1;
                case "重要不紧急": return 2;
                case "不重要但紧急": return 3;
                default: return 4;
            }
        }

        private static (string imp, string urg) QuadrantToImportanceUrgency(string quadrant)
        {
            switch (quadrant)
            {
                case "重要且紧急": return ("High", "High");
                case "重要不紧急": return ("High", "Low");
                case "不重要但紧急": return ("Low", "High");
                default: return ("Low", "Low");
            }
        }

        private static void RefreshMainGrid()
        {
            try
            {
                var mw = System.Windows.Application.Current?.Windows
                    .OfType<MainWindow>().FirstOrDefault();
                mw?.loadDataGridView();
            }
            catch { }
        }
    }
}
