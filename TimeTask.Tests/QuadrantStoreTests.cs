using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TimeTask;

namespace TimeTask.Tests
{
    /// <summary>
    /// QuadrantStore 语义契约：顶部插入、全象限评分、跨象限删除。
    /// 这些规则此前散落在 MainWindow / ActionInboxWindow 各自实现，现在必须锁定为唯一语义。
    /// </summary>
    [TestClass]
    public class QuadrantStoreTests
    {
        private static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "TimeTask.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static ItemGrid NewTask(string title, string sourceId = null)
        {
            return new ItemGrid
            {
                Task = title,
                IsActive = true,
                IsActiveInQuadrant = true,
                CreatedDate = DateTime.Now,
                LastModifiedDate = DateTime.Now,
                SourceTaskID = sourceId
            };
        }

        [TestMethod]
        public void InsertTop_InsertsAtTopAndRescoresAll()
        {
            string dir = TempDir();
            try
            {
                QuadrantStore.Save(2, new System.Collections.Generic.List<ItemGrid> { NewTask("旧1"), NewTask("旧2") }, dir);
                QuadrantStore.InsertTop(2, NewTask("新任务"), dir);

                var loaded = QuadrantStore.Load(2, dir);
                Assert.AreEqual(3, loaded.Count);
                Assert.AreEqual("新任务", loaded[0].Task, "新任务必须在象限顶部");
                Assert.AreEqual(3, loaded[0].Score);
                Assert.AreEqual(2, loaded[1].Score);
                Assert.AreEqual(1, loaded[2].Score, "评分 = 数量 - 序号（显示顺序即优先级）");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [TestMethod]
        public void DeleteFromAll_RemovesOnlyFromTargetQuadrant()
        {
            string dir = TempDir();
            try
            {
                var target = NewTask("要删的", sourceId: "sid-del");
                QuadrantStore.InsertTop(1, target, dir);
                QuadrantStore.InsertTop(3, NewTask("无关任务"), dir);

                var affected = QuadrantStore.DeleteFromAll(target, dir);

                CollectionAssert.AreEqual(new[] { 1 }, affected, "只应影响所在象限");
                Assert.AreEqual(0, QuadrantStore.Load(1, dir).Count);
                Assert.AreEqual(1, QuadrantStore.Load(3, dir).Count, "其他象限不受影响");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [TestMethod]
        public void Load_MissingFile_ReturnsEmptyList()
        {
            string dir = TempDir();
            try
            {
                Assert.AreEqual(0, QuadrantStore.Load(4, dir).Count, "无文件时返回空列表而非 null");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [TestMethod]
        public void PathFor_RejectsInvalidQuadrant()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => QuadrantStore.PathFor(0));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => QuadrantStore.PathFor(5));
        }

        [TestMethod]
        public void Rescore_EmptyAndNull_AreSafe()
        {
            QuadrantStore.Rescore(null);
            QuadrantStore.Rescore(new System.Collections.Generic.List<ItemGrid>());
        }
    }
}
