using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace TimeTask
{
    public partial class ImportUserNamePromptWindow : Window
    {
        public bool RememberChoice => RememberCheckBox.IsChecked == true;

        public ImportUserNamePromptWindow(List<string> knownNames)
        {
            InitializeComponent();
            KnownNamesComboBox.ItemsSource = knownNames ?? new List<string>();
        }

        public List<string> GetAliases()
        {
            string text = AliasesTextBox.Text ?? string.Empty;
            var aliases = text.Split(new[] { ',', '，', ';', '；', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim())
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return aliases;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (GetAliases().Count == 0)
            {
                MessageBox.Show(I18n.T("DraftViewer_ImportNeedName"), I18n.T("Title_Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void KnownNamesComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (KnownNamesComboBox.SelectedItem is string selected && !string.IsNullOrWhiteSpace(selected))
            {
                AliasesTextBox.Text = selected;
            }
        }
    }
}
