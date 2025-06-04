using Microsoft.VisualStudio.TestTools.UnitTesting;
using TimeTask; // Namespace of ItemGrid and HelperClass
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.ObjectModel; // For ObservableCollection in KanbanBoardView tests
using KanbanApp; // Namespace of KanbanBoardView (which now contains the static helpers)

namespace TimeTask.Tests
{
    [TestClass]
    public class KanbanFunctionalityTests
    {
        private string _tempCsvFilePath;

        [TestInitialize]
        public void TestInitialize()
        {
            // Create a temp file for CSV tests that need to read/write
            _tempCsvFilePath = Path.GetTempFileName();
        }

        [TestCleanup]
        public void TestCleanup()
        {
            // Delete the temp file if it exists
            if (File.Exists(_tempCsvFilePath))
            {
                File.Delete(_tempCsvFilePath);
            }
        }

        [TestMethod]
        public void TestItemGrid_DefaultKanbanValues()
        {
            // Arrange
            var item = new ItemGrid();

            // Assert
            Assert.AreEqual("Backlog", item.KanbanStage, "Default KanbanStage should be 'Backlog'.");
            Assert.AreEqual(0, item.KanbanOrder, "Default KanbanOrder should be 0.");
        }

        [TestMethod]
        public void TestReadCsv_WithKanbanFields()
        {
            // Arrange
            string csvContent = "task,score,result,is_completed,importance,urgency,createdDate,lastModifiedDate,kanbanStage,kanbanOrder\n" +
                                "TestTask1,10,res1,False,High,High,2023-01-01T00:00:00Z,2023-01-01T00:00:00Z,In Progress,1\n" +
                                "TestTask2,20,res2,True,Low,Low,2023-01-02T00:00:00Z,2023-01-02T00:00:00Z,Done,2";
            File.WriteAllText(_tempCsvFilePath, csvContent);

            // Act
            var items = HelperClass.ReadCsv(_tempCsvFilePath);

            // Assert
            Assert.IsNotNull(items, "ReadCsv should return a list, not null.");
            Assert.AreEqual(2, items.Count, "ReadCsv should read 2 items.");

            var item1 = items.FirstOrDefault(i => i.Task == "TestTask1");
            Assert.IsNotNull(item1, "TestTask1 not found.");
            Assert.AreEqual("In Progress", item1.KanbanStage);
            Assert.AreEqual(1, item1.KanbanOrder);

            var item2 = items.FirstOrDefault(i => i.Task == "TestTask2");
            Assert.IsNotNull(item2, "TestTask2 not found.");
            Assert.AreEqual("Done", item2.KanbanStage);
            Assert.AreEqual(2, item2.KanbanOrder);
        }

        [TestMethod]
        public void TestReadCsv_WithoutKanbanFields()
        {
            // Arrange
            string csvContent = "task,score,result,is_completed,importance,urgency,createdDate,lastModifiedDate\n" +
                                "TestTaskOld1,10,resOld1,False,High,High,2023-01-01T00:00:00Z,2023-01-01T00:00:00Z\n" +
                                "TestTaskOld2,20,resOld2,True,Low,Low,2023-01-02T00:00:00Z,2023-01-02T00:00:00Z";
            File.WriteAllText(_tempCsvFilePath, csvContent);

            // Act
            var items = HelperClass.ReadCsv(_tempCsvFilePath);

            // Assert
            Assert.IsNotNull(items, "ReadCsv should return a list, not null.");
            Assert.AreEqual(2, items.Count, "ReadCsv should read 2 items.");

            var item1 = items.FirstOrDefault(i => i.Task == "TestTaskOld1");
            Assert.IsNotNull(item1, "TestTaskOld1 not found.");
            Assert.AreEqual("Backlog", item1.KanbanStage, "Default KanbanStage should be 'Backlog' when field is missing.");
            Assert.AreEqual(0, item1.KanbanOrder, "Default KanbanOrder should be 0 when field is missing.");

            var item2 = items.FirstOrDefault(i => i.Task == "TestTaskOld2");
            Assert.IsNotNull(item2, "TestTaskOld2 not found.");
            Assert.AreEqual("Backlog", item2.KanbanStage);
            Assert.AreEqual(0, item2.KanbanOrder);
        }

        [TestMethod]
        public void TestReadCsv_MalformedKanbanOrder()
        {
            // Arrange
            string csvContent = "task,score,result,is_completed,importance,urgency,createdDate,lastModifiedDate,kanbanStage,kanbanOrder\n" +
                                "TestTaskMalformed,10,res1,False,High,High,2023-01-01T00:00:00Z,2023-01-01T00:00:00Z,In Progress,NotANumber";
            File.WriteAllText(_tempCsvFilePath, csvContent);

            // Act
            var items = HelperClass.ReadCsv(_tempCsvFilePath);

            // Assert
            Assert.IsNotNull(items);
            Assert.AreEqual(1, items.Count);
            var item1 = items.First();
            Assert.AreEqual("In Progress", item1.KanbanStage);
            Assert.AreEqual(0, item1.KanbanOrder, "KanbanOrder should default to 0 if malformed.");
        }


        [TestMethod]
        public void TestWriteCsv_IncludesKanbanFields()
        {
            // Arrange
            var items = new List<ItemGrid>
            {
                new ItemGrid
                {
                    Task = "WriteTest1", Score = 15, Result = "resWrite1", IsActive = true,
                    Importance = "High", Urgency = "Low",
                    CreatedDate = new System.DateTime(2023, 1, 3, 0, 0, 0, System.DateTimeKind.Utc),
                    LastModifiedDate = new System.DateTime(2023, 1, 4, 0, 0, 0, System.DateTimeKind.Utc),
                    KanbanStage = "In Progress", KanbanOrder = 5
                }
            };

            // Act
            HelperClass.WriteCsv(items, _tempCsvFilePath);
            var lines = File.ReadAllLines(_tempCsvFilePath);

            // Assert
            Assert.IsTrue(lines.Length >= 2, "CSV should have at least a header and a data row.");
            Assert.IsTrue(lines[0].Contains(",kanbanStage,kanbanOrder"), "Header row should contain kanbanStage and kanbanOrder.");
            Assert.IsTrue(lines[1].Contains(",In Progress,5"), "Data row should contain the correct KanbanStage and KanbanOrder values.");
        }

        [TestMethod]
        public void TestGetCsvFileNameForItem()
        {
            // Arrange
            var item1 = new ItemGrid { Importance = "High", Urgency = "High" };
            var item2 = new ItemGrid { Importance = "High", Urgency = "Low" };
            var item3 = new ItemGrid { Importance = "Low", Urgency = "High" };
            var item4 = new ItemGrid { Importance = "Low", Urgency = "Low" };
            var itemNull = (ItemGrid)null;
            var itemUnknown = new ItemGrid { Importance = "Unknown", Urgency = "Unknown" };


            // Act & Assert
            // Accessing static method from KanbanBoardView, which is in KanbanApp namespace
            Assert.AreEqual("1.csv", KanbanBoardView.GetCsvFileNameForItem(item1));
            Assert.AreEqual("2.csv", KanbanBoardView.GetCsvFileNameForItem(item2));
            Assert.AreEqual("3.csv", KanbanBoardView.GetCsvFileNameForItem(item3));
            Assert.AreEqual("4.csv", KanbanBoardView.GetCsvFileNameForItem(item4));
            Assert.AreEqual("1.csv", KanbanBoardView.GetCsvFileNameForItem(itemNull), "Null item should fallback to 1.csv");
            Assert.AreEqual("1.csv", KanbanBoardView.GetCsvFileNameForItem(itemUnknown), "Unknown/other values should fallback to 1.csv");
        }

        [TestMethod]
        public void TestGetAllTasksForOriginalQuadrant()
        {
            // Arrange
            var backlog = new ObservableCollection<ItemGrid>
            {
                new ItemGrid { Task = "B1", Importance = "High", Urgency = "High", KanbanStage = "Backlog" },
                new ItemGrid { Task = "B2", Importance = "High", Urgency = "Low", KanbanStage = "Backlog" }
            };
            var todo = new ObservableCollection<ItemGrid>
            {
                new ItemGrid { Task = "T1", Importance = "High", Urgency = "High", KanbanStage = "To Do" },
                new ItemGrid { Task = "T2", Importance = "Low", Urgency = "High", KanbanStage = "To Do" }
            };
            var inProgress = new ObservableCollection<ItemGrid>
            {
                new ItemGrid { Task = "IP1", Importance = "High", Urgency = "High", KanbanStage = "In Progress" }
            };
            var done = new ObservableCollection<ItemGrid>
            {
                new ItemGrid { Task = "D1", Importance = "Low", Urgency = "Low", KanbanStage = "Done" },
                new ItemGrid { Task = "D2", Importance = "High", Urgency = "High", KanbanStage = "Done" } // Another High/High
            };

            // Act: Test for Quadrant 1 (High Importance, High Urgency)
            // Accessing static method from KanbanBoardView
            var quadrant1Tasks = KanbanBoardView.GetAllTasksForOriginalQuadrant("High", "High", backlog, todo, inProgress, done);

            // Assert
            Assert.AreEqual(4, quadrant1Tasks.Count, "Should find 4 tasks in High/High quadrant across all stages.");
            Assert.IsTrue(quadrant1Tasks.Any(t => t.Task == "B1"), "Task B1 missing.");
            Assert.IsTrue(quadrant1Tasks.Any(t => t.Task == "T1"), "Task T1 missing.");
            Assert.IsTrue(quadrant1Tasks.Any(t => t.Task == "IP1"), "Task IP1 missing.");
            Assert.IsTrue(quadrant1Tasks.Any(t => t.Task == "D2"), "Task D2 missing.");

            // Act: Test for Quadrant 2 (High Importance, Low Urgency)
            var quadrant2Tasks = KanbanBoardView.GetAllTasksForOriginalQuadrant("High", "Low", backlog, todo, inProgress, done);
            Assert.AreEqual(1, quadrant2Tasks.Count, "Should find 1 task in High/Low quadrant.");
            Assert.IsTrue(quadrant2Tasks.Any(t => t.Task == "B2"), "Task B2 missing.");

            // Act: Test for an empty quadrant
            var emptyQuadrantTasks = KanbanBoardView.GetAllTasksForOriginalQuadrant("NonExistent", "NonExistent", backlog, todo, inProgress, done);
            Assert.AreEqual(0, emptyQuadrantTasks.Count, "Should find 0 tasks for a non-existent quadrant.");

            // Act: Test with null collections
             var nullQuadTasks = KanbanBoardView.GetAllTasksForOriginalQuadrant("High", "High", null, null, null, null);
            Assert.AreEqual(0, nullQuadTasks.Count, "Should handle null collections gracefully and return 0 tasks.");
        }
    }
}
