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
                    Scenario = d.Scenario,
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
                    Filter = I18n.T("Import_Filter"),
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
                    MessageBox.Show(I18n.T("Skill_Message_ImportParseFailed"), I18n.T("Title_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                SkillManagementService.SaveEnabledSkillIds(package.EnabledSkillIds);
                LoadSkills();
                MessageBox.Show(I18n.T("Skill_Message_Imported"), I18n.T("Title_Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(I18n.Tf("Skill_Message_ImportFailedFormat", ex.Message), I18n.T("Title_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = I18n.T("Export_Filter"),
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
                MessageBox.Show(I18n.T("Skill_Message_Exported"), I18n.T("Title_Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(I18n.Tf("Skill_Message_ExportFailedFormat", ex.Message), I18n.T("Title_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var enabled = _items.Where(x => x.Enabled).Select(x => x.SkillId).ToList();
                SkillManagementService.SaveEnabledSkillIds(enabled);
                MessageBox.Show(I18n.T("Skill_Message_Saved"), I18n.T("Title_Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(I18n.Tf("Skill_Message_SaveFailedFormat", ex.Message), I18n.T("Title_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
        public string Scenario { get; set; }
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
