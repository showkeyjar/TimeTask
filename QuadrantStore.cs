using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TimeTask
{
    /// <summary>
    /// 四象限任务数据的持久化语义（唯一的 Owner）。
    /// 此前「顶部插入 + 重排序评分 + CSV 落盘」的规则散落在 MainWindow、ActionInboxWindow
    /// 等多处各自实现，规则漂移就会出现「同一任务在不同入口写入的分数不一致」。
    /// 这里统一：象限编号 1..4 → data\N.csv；列表评分 = Count - i（显示顺序即优先级）；
    /// 新任务一律插到象限顶部。UI 层（DataGrid 绑定与刷新）仍留在窗口里，逐步迁入。
    /// </summary>
    public static class QuadrantStore
    {
        /// <summary>加载一个象限的全部任务（含未显示的）；无文件或损坏时返回空列表。
        /// dataDir 供测试注入临时目录；默认走 AppPaths 统一解析。</summary>
        public static List<ItemGrid> Load(int quadrant, string dataDir = null)
        {
            ValidateQuadrant(quadrant);
            return HelperClass.ReadCsv(PathFor(quadrant, dataDir)) ?? new List<ItemGrid>();
        }

        /// <summary>保存一个象限（原子写 + .bak 备份）。</summary>
        public static void Save(int quadrant, List<ItemGrid> items, string dataDir = null)
        {
            ValidateQuadrant(quadrant);
            if (items == null)
            {
                return;
            }
            HelperClass.WriteCsv(items, PathFor(quadrant, dataDir));
        }

        /// <summary>
        /// 把新任务插到象限顶部并按「显示顺序即优先级」重排评分后落盘。
        /// 返回落盘后的完整列表（调用方若需要同步 UI 可直接用）。
        /// </summary>
        public static List<ItemGrid> InsertTop(int quadrant, ItemGrid item, string dataDir = null)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }
            var list = Load(quadrant, dataDir);
            list.Insert(0, item);
            Rescore(list);
            Save(quadrant, list, dataDir);
            return list;
        }

        /// <summary>
        /// 从所有象限中删除一条任务（按引用或按追踪键匹配）。
        /// 返回受影响的象限编号列表（通常为空或一个元素）。
        /// </summary>
        public static List<int> DeleteFromAll(ItemGrid item, string dataDir = null)
        {
            if (item == null)
            {
                return new List<int>();
            }

            string trackingKey = GetTrackingKey(item);
            var affected = new List<int>();
            for (int q = 1; q <= 4; q++)
            {
                var list = Load(q, dataDir);
                int removed = list.RemoveAll(t => t == item || GetTrackingKey(t) == trackingKey);
                if (removed > 0)
                {
                    Save(q, list, dataDir);
                    affected.Add(q);
                }
            }
            return affected;
        }

        /// <summary>按「显示顺序即优先级」重排评分：第一行分数最高。</summary>
        public static void Rescore(List<ItemGrid> items)
        {
            if (items == null)
            {
                return;
            }
            for (int i = 0; i < items.Count; i++)
            {
                items[i].Score = items.Count - i;
            }
        }

        /// <summary>象限编号 → CSV 路径（数据目录由 AppPaths 统一解析，测试可注入）。</summary>
        public static string PathFor(int quadrant, string dataDir = null)
        {
            ValidateQuadrant(quadrant);
            return dataDir != null
                ? Path.Combine(dataDir, quadrant + ".csv")
                : AppPaths.GetDataFile(quadrant + ".csv");
        }

        private static void ValidateQuadrant(int quadrant)
        {
            if (quadrant < 1 || quadrant > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(quadrant), quadrant, "象限编号必须是 1..4");
            }
        }

        /// <summary>任务的业务标识：优先 SourceTaskID，否则创建时间+标题（与行为记录的键一致）。</summary>
        private static string GetTrackingKey(ItemGrid task)
        {
            string id = string.IsNullOrWhiteSpace(task.SourceTaskID) ? string.Empty : task.SourceTaskID.Trim();
            if (!string.IsNullOrWhiteSpace(id))
            {
                return "sid:" + id;
            }
            return task.CreatedDate.ToString("o") + "|" + task.Task;
        }
    }
}
