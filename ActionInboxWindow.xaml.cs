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
    /// 交互原则（用户反馈“太繁琐/概念不懂/不知道下一步”后确立）：
    /// 首屏只回答「找到了几件事、加不加」，一个绿色主按钮收口；
    /// 回放/转写全文/会议纪要全部折叠、大白话命名；不露 ASR/LLM/置信度等术语。
    ///
    /// 降级设计：即使本地 ASR 模型不可用，录音依然落盘。本窗口支持
    /// (1) 直接回放录音，(2) 手动录入/粘贴文本并复用同一套提取逻辑。
    /// 因此“没有 ASR”也能完成“录音→行动项”的完整闭环。
    ///
    /// i18n 说明：象限在 VM 内以索引 0..3 表示；展示名/列头全走资源键。
    /// 服务端给出的中文象限名（LLM 输出的数据语言）由 QuadrantNameToIndex 归一。
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
            /// <summary>象限索引 0..3（重要且紧急/重要不紧急/不重要但紧急/不重要不紧急）。</summary>
            public int QuadrantIndex { get; set; } = 1;
            public DateTime? Reminder { get; set; }
            public float Confidence { get; set; }
            public string ConfidenceDisplay => $"{(Confidence * 100):0}%";
            public event PropertyChangedEventHandler PropertyChanged;
            private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        /// <summary>服务端象限名的规范顺序（LLM/提取器输出的数据语言，非 UI 文案）。</summary>
        private static readonly string[] CanonicalQuadrants =
            { "重要且紧急", "重要不紧急", "不重要但紧急", "不重要不紧急" };

        private static readonly (string imp, string urg)[] QuadrantImportanceUrgency =
            { ("High", "High"), ("High", "Low"), ("Low", "High"), ("Low", "Low") };

        /// <summary>把服务端中文象限名归一为索引；未知值默认「重要不紧急」(1)。</summary>
        private static int QuadrantNameToIndex(string name)
        {
            for (int i = 0; i < CanonicalQuadrants.Length; i++)
            {
                if (string.Equals(CanonicalQuadrants[i], name, StringComparison.Ordinal)) return i;
            }
            return 1;
        }

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

            QuadrantColumn.ItemsSource = new[]
            {
                new { Index = 0, Name = I18n.T("Quadrant_ImportantUrgent") },
                new { Index = 1, Name = I18n.T("Quadrant_ImportantNotUrgent") },
                new { Index = 2, Name = I18n.T("Quadrant_NotImportantUrgent") },
                new { Index = 3, Name = I18n.T("Quadrant_NotImportantNotUrgent") }
            };
            Grid.ItemsSource = _items;

            // ---------- 人话文案：只讲结论和数字，不露术语 ----------
            var dur = result.EndTime - result.StartTime;
            TitleText.Text = result.Type == ConversationType.Meeting
                ? I18n.T("ActionInbox_TitleMeeting")
                : I18n.T("ActionInbox_TitleRecording");
            string speechState = result.AsrAvailable ? I18n.T("ActionInbox_SpeechConverted") : I18n.T("ActionInbox_SpeechNotConverted");
            MetaText.Text = I18n.Tf("ActionInbox_MetaFormat", Math.Max(dur.TotalMinutes, 0).ToString("F0"), speechState, result.Actions.Count);

            if (string.IsNullOrWhiteSpace(result.Summary))
            {
                // 空态：直接说人话 + 给两条可走的路（听回放 / 手动补记），不给一段术语墙
                EmptyReasonText.Text = result.AsrAvailable
                    ? I18n.T("ActionInbox_EmptyReasonAsr")
                    : I18n.T("ActionInbox_EmptyReasonNoAsr");
                if (!string.IsNullOrWhiteSpace(_folder))
                {
                    EmptyReasonText.Text += I18n.Tf("ActionInbox_SavedAtFormat", _folder);
                }
            }
            else
            {
                TranscriptBox.Text = result.Summary;
            }

            // ---------- 空态 / 有内容的分叉 ----------
            foreach (var a in result.Actions)
            {
                var vm = new ActionItemVM
                {
                    Text = a.CleanedText ?? a.RawText,
                    QuadrantIndex = QuadrantNameToIndex(a.EstimatedQuadrant),
                    Reminder = a.ReminderTime,
                    Confidence = (float)a.Confidence
                };
                vm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(ActionItemVM.Accepted)) UpdateAcceptButton(); };
                _items.Add(vm);
            }

            if (_items.Count == 0)
            {
                ActionsCard.Visibility = Visibility.Collapsed;
                ActionHintText.Visibility = Visibility.Collapsed;
                EmptyCard.Visibility = Visibility.Visible;
                // 没有可提取内容时：展开转写区引导查看；完全没有文字时再展开手动补记
                TranscriptExpander.IsExpanded = true;
                if (string.IsNullOrWhiteSpace(result.Summary))
                {
                    ManualExpander.IsExpanded = true;
                }
            }
            UpdateAcceptButton();

            // ---------- 录音回放（折叠区）：只在确实有录音文件时保留入口 ----------
            bool hasMic = _micPath != null && File.Exists(_micPath);
            bool hasSys = _sysPath != null && File.Exists(_sysPath);
            if (hasMic || hasSys)
            {
                BtnPlayMic.Visibility = hasMic ? Visibility.Visible : Visibility.Collapsed;
                BtnPlaySystem.Visibility = hasSys ? Visibility.Visible : Visibility.Collapsed;
                int days = RecordingRetention.GetRetentionDays();
                RetentionNote.Text = RecordingRetention.IsEnabled(days)
                    ? I18n.Tf("ActionInbox_RetentionFormat", days)
                    : I18n.T("ActionInbox_RetentionDisabled");
            }
            else
            {
                AudioExpander.Visibility = Visibility.Collapsed;
            }

            // ---------- 会议纪要（折叠区）：有结构化内容时才给入口 ----------
            if (result.MeetingState != null)
            {
                MinutesSummary.Text = string.IsNullOrWhiteSpace(result.MeetingState.Summary)
                    ? I18n.T("ActionInbox_MinutesEmpty")
                    : result.MeetingState.Summary;
                MinConcepts.ItemsSource = result.MeetingState.Concepts;
                MinQuestions.ItemsSource = result.MeetingState.Questions;
                MinDecisions.ItemsSource = result.MeetingState.Decisions;
                MinActions.ItemsSource = result.MeetingState.Actions;
            }
            else
            {
                MinutesSummary.Text = I18n.T("ActionInbox_MinutesUnavailable");
            }

            Closed += (s, e) => StopPlayback();
        }

        /// <summary>主按钮文案与可用性 = 当前勾选数（所见即所得，不再区分“全部接受/接受选中”）。</summary>
        private void UpdateAcceptButton()
        {
            int n = _items.Count(i => i.Accepted);
            BtnAcceptAll.Content = n > 0 ? I18n.Tf("ActionInbox_AcceptCountFormat", n) : I18n.T("ActionInbox_Accept");
            BtnAcceptAll.IsEnabled = n > 0;
        }

        // ---------- 空态引导：两条路一目了然 ----------

        private void BtnEmptyListen_Click(object sender, RoutedEventArgs e)
        {
            AudioExpander.IsExpanded = true;
            if (_micPath != null && File.Exists(_micPath)) StartPlayback(_micPath);
            else if (_sysPath != null && File.Exists(_sysPath)) StartPlayback(_sysPath);
        }

        private void BtnEmptyManual_Click(object sender, RoutedEventArgs e)
        {
            TranscriptExpander.IsExpanded = true;
            ManualExpander.IsExpanded = true;
            ManualTextBox.Focus();
        }

        // ---------- 录音回放（NAudio，非阻塞）----------

        private void BtnPlayMic_Click(object sender, RoutedEventArgs e) => StartPlayback(_micPath);
        private void BtnPlaySystem_Click(object sender, RoutedEventArgs e) => StartPlayback(_sysPath);
        private void BtnStopPlay_Click(object sender, RoutedEventArgs e) => StopPlayback();

        private void StartPlayback(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                PlayStatus.Text = I18n.T("ActionInbox_PlayNotFound");
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
                        PlayStatus.Text = I18n.T("ActionInbox_PlayEnded");
                    }));
                _waveOut.Init(_audioReader);
                _waveOut.Play();
                BtnStopPlay.IsEnabled = true;
                PlayStatus.Text = I18n.Tf("ActionInbox_PlayingFormat", Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                PlayStatus.Text = I18n.Tf("ActionInbox_PlayErrorFormat", ex.Message);
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
                MessageBox.Show(I18n.T("ActionInbox_ExtractNeedText"), I18n.T("Title_Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var drafts = App.Instance?.CaptureService?.ExtractActionsFromText(text) ?? new List<TaskDraft>();
            if (drafts.Count == 0)
            {
                MessageBox.Show(I18n.T("ActionInbox_ExtractNoneFound"),
                    I18n.T("Title_Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var d in drafts)
            {
                var vm = new ActionItemVM
                {
                    Text = d.CleanedText ?? d.RawText,
                    QuadrantIndex = QuadrantNameToIndex(d.EstimatedQuadrant),
                    Reminder = d.ReminderTime,
                    Confidence = (float)d.Confidence
                };
                vm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(ActionItemVM.Accepted)) UpdateAcceptButton(); };
                _items.Add(vm);
            }

            // 手动补记产生了内容：从空态切回列表态
            EmptyCard.Visibility = Visibility.Collapsed;
            ActionsCard.Visibility = Visibility.Visible;
            ActionHintText.Visibility = Visibility.Visible;
            UpdateAcceptButton();
            TranscriptBox.Text = text.Trim();
            StatusText.Text = I18n.Tf("ActionInbox_ExtractedCountFormat", drafts.Count);
        }

        // ---------- 加入 / 忽略 ----------

        private void BtnAcceptAll_Click(object sender, RoutedEventArgs e) => Accept();

        /// <summary>
        /// 把勾选的事项加入四象限。勾选即所见即所得（默认全勾），
        /// 完成后直接关窗——主窗口会自动刷新，任务就在象限顶部，不再叠加确认弹窗。
        /// </summary>
        private void Accept()
        {
            var toAdd = _items.Where(i => i.Accepted).ToList();

            if (toAdd.Count == 0)
            {
                return;
            }

            int added = 0;

            foreach (var item in toAdd)
            {
                try
                {
                    int csvNumber = item.QuadrantIndex + 1;
                    var (imp, urg) = QuadrantImportanceUrgency[item.QuadrantIndex];
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

            // 全部失败才打扰用户；成功时不弹确认框（少一次点击，任务已出现在主窗口象限顶部）
            if (added == 0)
            {
                MessageBox.Show(I18n.T("ActionInbox_AcceptFailed"), I18n.T("Title_Prompt"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Close();
        }

        private void BtnDismiss_Click(object sender, RoutedEventArgs e) => Close();

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
