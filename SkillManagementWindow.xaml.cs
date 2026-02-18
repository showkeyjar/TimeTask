using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace TimeTask
{
    public partial class SkillManagementWindow : Window
    {
        private List<SkillViewItem> _items = new List<SkillViewItem>();

        public SkillManagementWindow()
        {
            InitializeComponent();
            LoadSkills();
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

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var enabled = _items.Where(x => x.Enabled).Select(x => x.SkillId).ToList();
                SkillManagementService.SaveEnabledSkillIds(enabled);
                MessageBox.Show("Skill 设置已保存。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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
}
