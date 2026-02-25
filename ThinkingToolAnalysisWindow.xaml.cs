using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace TimeTask
{
    public partial class ThinkingToolAnalysisWindow : Window
    {
        public ThinkingToolAnalysisReport Report { get; }
        public List<ThinkingToolActionItem> SelectedActions { get; private set; } = new List<ThinkingToolActionItem>();

        public ThinkingToolAnalysisWindow(ThinkingToolAnalysisReport report)
        {
            InitializeComponent();
            Report = report ?? new ThinkingToolAnalysisReport();
            BindReport();
        }

        private void BindReport()
        {
            TitleText.Text = I18n.Tf("ThinkingAnalysis_TitleFormat", Report.ToolTitle);
            TaskText.Text = I18n.Tf("ThinkingAnalysis_TaskFormat", Report.TaskName);
            WhyText.Text = I18n.Tf("ThinkingAnalysis_WhyFormat", Report.Why);
            DiagnosticText.Text = I18n.Tf("ThinkingAnalysis_DiagnosticFormat", Report.Diagnostic);
            HypothesisText.Text = I18n.Tf("ThinkingAnalysis_HypothesisFormat", Report.Hypothesis);
            DecisionRuleText.Text = I18n.Tf("ThinkingAnalysis_DecisionRuleFormat", Report.DecisionRule);
            RiskItems.ItemsSource = (Report.Risks ?? new List<string>()).Select(r => $"• {r}").ToList();
            ActionsGrid.ItemsSource = Report.Actions ?? new List<ThinkingToolActionItem>();
            ReviewPromptText.Text = I18n.Tf("ThinkingAnalysis_ReviewPromptFormat", Report.ReviewPrompt);
        }

        private void CreateSelected_Click(object sender, RoutedEventArgs e)
        {
            SelectedActions = (Report.Actions ?? new List<ThinkingToolActionItem>())
                .Where(a => a != null && a.Selected && !string.IsNullOrWhiteSpace(a.Text))
                .ToList();
            DialogResult = true;
            Close();
        }

        private void CreateFirst_Click(object sender, RoutedEventArgs e)
        {
            SelectedActions = (Report.Actions ?? new List<ThinkingToolActionItem>())
                .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Text))
                .Take(1)
                .ToList();
            DialogResult = true;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
