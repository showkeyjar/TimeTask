using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace TimeTask
{
    public partial class SkillTreeWindow : Window
    {
        private readonly string _dataPath;

        public SkillTreeWindow(string dataPath)
        {
            InitializeComponent();
            _dataPath = dataPath;
            DataContext = new SkillTreeViewModel(BuildTree());
        }

        private List<SkillTreeNode> BuildTree()
        {
            string goalsPath = Path.Combine(_dataPath, "long_term_goals.csv");
            var goals = HelperClass.ReadLongTermGoalsCsv(goalsPath) ?? new List<LongTermGoal>();
            var allTasks = LoadAllTasks();

            var nodes = new List<SkillTreeNode>();
            foreach (var goal in goals.Where(g => g != null && g.IsActive))
            {
                nodes.Add(BuildGoalNode(goal, allTasks));
            }

            if (nodes.Count == 0)
            {
                nodes.Add(new SkillTreeNode
                {
                    Title = I18n.T("Strategy_NoActiveGoal"),
                    Status = SkillTreeNodeStatus.NotStarted
                });
            }

            return nodes;
        }

        private List<ItemGrid> LoadAllTasks()
        {
            var tasks = new List<ItemGrid>();
            for (int i = 1; i <= 4; i++)
            {
                string path = Path.Combine(_dataPath, $"{i}.csv");
                tasks.AddRange(HelperClass.ReadCsv(path) ?? new List<ItemGrid>());
            }
            return tasks;
        }

        private SkillTreeNode BuildGoalNode(LongTermGoal goal, List<ItemGrid> allTasks)
        {
            var node = new SkillTreeNode
            {
                Title = goal.IsLearningPlan
                    ? string.Format("{0} ({1})", goal.Subject, goal.Description)
                    : goal.Description,
                Subtitle = goal.TotalDuration,
                Children = new List<SkillTreeNode>()
            };

            if (goal.IsLearningPlan)
            {
                string milestonesPath = Path.Combine(_dataPath, $"learning_milestones_{goal.Id}.csv");
                var milestones = HelperClass.ReadLearningMilestonesCsv(milestonesPath) ?? new List<LearningMilestone>();
                foreach (var milestone in milestones.OrderBy(m => m.StageNumber))
                {
                    node.Children.Add(BuildMilestoneNode(milestone));
                }
            }
            else
            {
                var related = allTasks.Where(t => string.Equals(t.LongTermGoalId, goal.Id, StringComparison.OrdinalIgnoreCase)).ToList();
                var grouped = related
                    .GroupBy(t => t.OriginalScheduledDay > 0 ? ((t.OriginalScheduledDay - 1) / 30) + 1 : 0)
                    .OrderBy(g => g.Key);

                foreach (var group in grouped)
                {
                    string stageTitle = group.Key == 0
                        ? I18n.T("SkillTree_Group_Unscheduled")
                        : I18n.Tf("SkillTree_Group_StageFormat", group.Key);
                    var stageNode = new SkillTreeNode
                    {
                        Title = stageTitle,
                        Children = group.Select(BuildTaskNode).ToList()
                    };
                    stageNode.Status = AggregateStatus(stageNode.Children);
                    stageNode.ProgressText = BuildProgressText(stageNode.Children);
                    node.Children.Add(stageNode);
                }
            }

            node.Status = AggregateStatus(node.Children);
            node.ProgressText = BuildProgressText(node.Children);
            return node;
        }

        private static SkillTreeNode BuildMilestoneNode(LearningMilestone milestone)
        {
            var status = milestone.IsCompleted ? SkillTreeNodeStatus.Completed : SkillTreeNodeStatus.NotStarted;
            return new SkillTreeNode
            {
                Title = $"{milestone.StageNumber}. {milestone.StageName}",
                Subtitle = milestone.Description,
                Status = status,
                ProgressText = milestone.TargetDate.HasValue ? milestone.TargetDate.Value.ToString("yyyy-MM-dd") : string.Empty
            };
        }

        private static SkillTreeNode BuildTaskNode(ItemGrid task)
        {
            var status = task.IsActive ? SkillTreeNodeStatus.InProgress : SkillTreeNodeStatus.Completed;
            return new SkillTreeNode
            {
                Title = string.IsNullOrWhiteSpace(task.Task) ? I18n.T("SkillTree_UnnamedTask") : task.Task,
                Status = status
            };
        }

        private static SkillTreeNodeStatus AggregateStatus(List<SkillTreeNode> children)
        {
            if (children == null || children.Count == 0)
            {
                return SkillTreeNodeStatus.NotStarted;
            }
            if (children.All(c => c.Status == SkillTreeNodeStatus.Completed))
            {
                return SkillTreeNodeStatus.Completed;
            }
            if (children.Any(c => c.Status == SkillTreeNodeStatus.InProgress || c.Status == SkillTreeNodeStatus.Completed))
            {
                return SkillTreeNodeStatus.InProgress;
            }
            return SkillTreeNodeStatus.NotStarted;
        }

        private static string BuildProgressText(List<SkillTreeNode> children)
        {
            if (children == null || children.Count == 0) return string.Empty;
            int completed = children.Count(c => c.Status == SkillTreeNodeStatus.Completed);
            return $"({completed}/{children.Count})";
        }
    }

    public enum SkillTreeNodeStatus
    {
        NotStarted,
        InProgress,
        Completed
    }

    public class SkillTreeNode
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string ProgressText { get; set; }
        public SkillTreeNodeStatus Status { get; set; }
        public List<SkillTreeNode> Children { get; set; } = new List<SkillTreeNode>();

        public Brush StatusColor
        {
            get
            {
                switch (Status)
                {
                    case SkillTreeNodeStatus.Completed:
                        return new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                    case SkillTreeNodeStatus.InProgress:
                        return new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                    default:
                        return new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
                }
            }
        }
    }

    public class SkillTreeViewModel
    {
        public List<SkillTreeNode> Nodes { get; }

        public SkillTreeViewModel(List<SkillTreeNode> nodes)
        {
            Nodes = nodes ?? new List<SkillTreeNode>();
        }
    }
}
