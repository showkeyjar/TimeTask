using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace TimeTask
{
    public partial class ImportPreviewWindow : Window
    {
        private readonly ImportPreviewViewModel _viewModel;

        public ImportPreviewWindow(ImportPreviewResult preview)
        {
            InitializeComponent();
            _viewModel = new ImportPreviewViewModel(preview);
            DataContext = _viewModel;
            PreviewGrid.ItemsSource = _viewModel.Items;
        }

        public List<ImportPreviewItem> GetSelectedItems()
        {
            return _viewModel.Items.Where(i => i.IsSelected && i.IsSelectable).ToList();
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _viewModel.Items)
            {
                if (item.IsSelectable)
                {
                    item.IsSelected = true;
                }
            }
        }

        private void SelectNoneButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _viewModel.Items)
            {
                item.IsSelected = false;
            }
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (GetSelectedItems().Count == 0)
            {
                MessageBox.Show(I18n.T("DraftViewer_ImportPreviewNoneSelected"), I18n.T("Title_Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }

    public class ImportPreviewViewModel
    {
        public ObservableCollection<ImportPreviewItem> Items { get; }
        public string SummaryText { get; }

        public ImportPreviewViewModel(ImportPreviewResult preview)
        {
            preview ??= new ImportPreviewResult();
            Items = new ObservableCollection<ImportPreviewItem>(preview.Items ?? new List<ImportPreviewItem>());

            int selectable = Items.Count(i => i.IsSelectable);
            SummaryText = I18n.Tf("DraftViewer_ImportPreviewSummaryFormat", preview.TotalCandidates, selectable, preview.Filtered, preview.Failed, preview.LlmUsed);
        }
    }
}
