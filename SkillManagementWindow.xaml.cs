using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using System.IO;
using System.Text;
using System.Text.Json;

namespace TimeTask
{
    public partial class SkillManagementWindow : Window
    {
        private List<SkillViewItem> _items = new List<SkillViewItem>();

        public SkillManagementWindow()
        {
            InitializeComponent();
            LoadSkills();
            ShowFirstTimeHintIfNeeded();
        }

        private void LoadSkills()
        {
            var defs = SkillManagementService.GetSkillDefinitions();
            var performance = new UserProfileManager()
                .GetActionPerformance(30)
                .ToDictionary(x => x.ActionId, StringComparer.OrdinalIgnoreCase);

            _items = defs.Select(d =>
            {
                performance.TryGetValue(d.SkillId, out var stat);
                return new SkillViewItem
                {
                    SkillId = d.SkillId,
                    Title = d.Title,
                    Description = d.Description,
                    Enabled = d.Enabled,
                    Shown = stat?.Shown ?? 0,
                    Accepted = stat?.Accepted ?? 0,
                    Deferred = stat?.Deferred ?? 0,
                    Rejected = stat?.Rejected ?? 0
                };
            }).ToList();

            SkillsDataGrid.ItemsSource = _items;
        }

        private void EnableAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items)
            {
                item.Enabled = true;
            }
            SkillsDataGrid.Items.Refresh();
        }

        private void DisableAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items)
            {
                item.Enabled = false;
            }
            SkillsDataGrid.Items.Refresh();
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "JSON文件|*.json|所有文件|*.*",
                    DefaultExt = "json"
                };

                if (openDialog.ShowDialog() != true)
                {
                    return;
                }

                string json = File.ReadAllText(openDialog.FileName, Encoding.UTF8);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var package = JsonSerializer.Deserialize<SkillExportPackage>(json, options);
                if (package == null || package.EnabledSkillIds == null)
                {
                    MessageBox.Show("导入失败：文件内容无法解析。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                SkillManagementService.SaveEnabledSkillIds(package.EnabledSkillIds);
                LoadSkills();
                MessageBox.Show("Skill 设置已导入。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON文件|*.json|所有文件|*.*",
                    DefaultExt = "json",
                    FileName = $"TimeTask_Skills_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };

                if (saveDialog.ShowDialog() != true)
                {
                    return;
                }

                var package = new SkillExportPackage
                {
                    ExportedAt = DateTime.Now,
                    EnabledSkillIds = SkillManagementService.LoadEnabledSkillIds().ToList()
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(package, options);
                File.WriteAllText(saveDialog.FileName, json, Encoding.UTF8);
                MessageBox.Show("Skill 设置已导出。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var enabled = _items.Where(x => x.Enabled).Select(x => x.SkillId).ToList();
                SkillManagementService.SaveEnabledSkillIds(enabled);
                MessageBox.Show("Skill 设置已保存，将在下一次任务提醒中生效。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ShowFirstTimeHintIfNeeded()
        {
            if (Properties.Settings.Default.SkillHintShown)
            {
                return;
            }

            FirstTimeHintBanner.Visibility = Visibility.Visible;
            Properties.Settings.Default.SkillHintShown = true;
            Properties.Settings.Default.Save();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                FirstTimeHintBanner.Visibility = Visibility.Collapsed;
            };
            timer.Start();
        }
    }

    public class SkillViewItem
    {
        public string SkillId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool Enabled { get; set; }
        public int Shown { get; set; }
        public int Accepted { get; set; }
        public int Deferred { get; set; }
        public int Rejected { get; set; }
    }

    public class SkillExportPackage
    {
        public int Version { get; set; } = 1;
        public DateTime ExportedAt { get; set; } = DateTime.Now;
        public List<string> EnabledSkillIds { get; set; } = new List<string>();
    }
}
