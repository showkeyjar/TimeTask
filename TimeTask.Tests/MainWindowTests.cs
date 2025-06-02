using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using TimeTask; // Assuming MainWindow and ItemGrid are in this namespace
using System.Windows.Controls; // Required for DataGrid, though it's a mock

namespace TimeTask.Tests
{
    [TestClass]
    public class MainWindowTests
    {
        private string _testDataPath;
        private MainWindow _mainWindowInstance; // Keep a reference if needed for instance methods under test

        [TestInitialize]
        public void TestInitialize()
        {
            // Create a unique directory for test CSV files to avoid conflicts
            _testDataPath = Path.Combine(Path.GetTempPath(), "TimeTaskTests", Guid.NewGuid().ToString(), "data");
            Directory.CreateDirectory(_testDataPath);

            // If any methods on MainWindow instance are needed (e.g. update_csv if not static, GetQuadrantNumber)
            // we might need to initialize it. However, ProcessTaskDrop is static.
            // For update_csv, if we pass null DataGrids and rely on ItemsSource, we might not need a full MainWindow.
            // For now, let's assume we can test static/internal logic without a full UI window.
            // _mainWindowInstance = new MainWindow(); // This would try to create a window, which is problematic in tests.
            // Instead, we will call internal methods directly or on a mock/test-specific instance if necessary.
            // We will use a simple object that has an ItemsSource property for the update_csv method.
        }

        [TestCleanup]
        public void TestCleanup()
        {
            if (Directory.Exists(Path.Combine(Path.GetTempPath(), "TimeTaskTests")))
            {
                // Directory.Delete(Path.Combine(Path.GetTempPath(), "TimeTaskTests"), true); // Clean up parent if empty, careful with parallel tests
            }
             // Clean up the specific test run's data path
            if (Directory.Exists(_testDataPath))
            {
                Directory.Delete(_testDataPath, true);
            }
        }

        // Helper to create a mock DataGrid with specified ItemsSource for testing update_csv
        private DataGrid CreateMockDataGrid(List<ItemGrid> items, string name)
        {
            return new DataGrid { ItemsSource = items, Name = name };
        }

        // Overload update_csv for testing (mimicking MainWindow's internal method)
        // This is needed because the actual update_csv is an instance method.
        // We will provide the necessary 'currentPath' equivalent via _testDataPath.
        private void UpdateCsvForTest(DataGrid dgv, string number)
        {
            if (dgv == null || dgv.ItemsSource == null) return;
            var itemsToSave = new List<ItemGrid>();
            if (dgv.ItemsSource is IEnumerable<ItemGrid> items)
            {
                itemsToSave.AddRange(items);
            }
            HelperClass.WriteCsv(itemsToSave, Path.Combine(_testDataPath, number + ".csv"));
        }


        [TestMethod]
        public void Test_DragDrop_TaskMovesToNewQuadrant()
        {
            // Arrange
            var taskToDrag = new ItemGrid { Task = "Test Task 1", Score = 10, Importance = "High", Urgency = "High", CreatedDate = DateTime.Now.AddDays(-1), LastModifiedDate = DateTime.Now.AddDays(-1) };
            var sourceList = new List<ItemGrid> { taskToDrag, new ItemGrid { Task = "Other Task", Score = 5 } };
            var targetList = new List<ItemGrid> { new ItemGrid { Task = "Existing Target Task", Score = 8 } };
            string targetDataGridName = "task2"; // Simulate drop to Quadrant 2

            // Act
            bool result = MainWindow.ProcessTaskDrop(taskToDrag, sourceList, targetList, targetDataGridName);

            // Assert
            Assert.IsTrue(result, "ProcessTaskDrop should return true for a successful move.");
            Assert.IsFalse(sourceList.Contains(taskToDrag), "Task should be removed from the source list.");
            Assert.AreEqual(1, sourceList.Count, "Source list should have one less item.");
            Assert.IsTrue(targetList.Contains(taskToDrag), "Task should be added to the target list.");
            Assert.AreEqual(2, targetList.Count, "Target list should have one more item.");
        }

        [TestMethod]
        public void Test_DragDrop_TaskPropertiesUpdated()
        {
            // Arrange
            var taskToDrag = new ItemGrid { Task = "Test Task PropUpdate", Score = 10, Importance = "High", Urgency = "High", CreatedDate = DateTime.Now.AddDays(-2), LastModifiedDate = DateTime.Now.AddDays(-2) };
            var originalLastModifiedDate = taskToDrag.LastModifiedDate;
            var sourceList = new List<ItemGrid> { taskToDrag };
            var targetList = new List<ItemGrid>();
            string targetDataGridName = "task2"; // Simulate drop to Quadrant 2 (Important & Not Urgent)

            // Act
            MainWindow.ProcessTaskDrop(taskToDrag, sourceList, targetList, targetDataGridName);

            // Assert
            Assert.AreEqual("High", taskToDrag.Importance, "Importance should be updated to High.");
            Assert.AreEqual("Low", taskToDrag.Urgency, "Urgency should be updated to Low.");
            Assert.IsTrue(taskToDrag.LastModifiedDate > originalLastModifiedDate, "LastModifiedDate should be updated.");
        }

        [TestMethod]
        public void Test_DragDrop_TaskPropertiesUpdated_ToQuadrant3()
        {
            // Arrange
            var taskToDrag = new ItemGrid { Task = "Test Task Q3", Score = 5, Importance = "High", Urgency = "High" };
            var sourceList = new List<ItemGrid> { taskToDrag };
            var targetList = new List<ItemGrid>();
            string targetDataGridName = "task3"; // Simulate drop to Quadrant 3 (Not Important & Urgent)

            // Act
            MainWindow.ProcessTaskDrop(taskToDrag, sourceList, targetList, targetDataGridName);

            // Assert
            Assert.AreEqual("Low", taskToDrag.Importance, "Importance should be updated to Low for Q3.");
            Assert.AreEqual("High", taskToDrag.Urgency, "Urgency should be updated to High for Q3.");
        }


        [TestMethod]
        public void Test_DragDrop_CsvFilesUpdated()
        {
            // Arrange
            var taskToDrag = new ItemGrid { Task = "CSV Test Task", Score = 100, Importance = "High", Urgency = "High", CreatedDate = DateTime.Now, LastModifiedDate = DateTime.Now };
            var otherSourceTask = new ItemGrid { Task = "Remaining Source Task", Score = 50 };

            var initialSourceItems = new List<ItemGrid> { taskToDrag, otherSourceTask };
            var initialTargetItems = new List<ItemGrid> { new ItemGrid { Task = "Existing Target Task", Score = 20 } };

            // Create initial CSV files
            HelperClass.WriteCsv(initialSourceItems, Path.Combine(_testDataPath, "1.csv"));
            HelperClass.WriteCsv(initialTargetItems, Path.Combine(_testDataPath, "2.csv"));

            // These lists will be modified by ProcessTaskDrop
            var currentSourceList = new List<ItemGrid>(initialSourceItems);
            var currentTargetList = new List<ItemGrid>(initialTargetItems);

            DataGrid mockSourceDataGrid = CreateMockDataGrid(currentSourceList, "task1");
            DataGrid mockTargetDataGrid = CreateMockDataGrid(currentTargetList, "task2");

            // Act
            bool success = MainWindow.ProcessTaskDrop(taskToDrag, currentSourceList, currentTargetList, "task2");
            Assert.IsTrue(success, "ProcessTaskDrop failed.");

            // Manually call the test version of update_csv for both lists
            UpdateCsvForTest(mockSourceDataGrid, "1"); // Pass the modified currentSourceList via mock DataGrid
            UpdateCsvForTest(mockTargetDataGrid, "2"); // Pass the modified currentTargetList via mock DataGrid

            // Assert
            var sourceFileContent = HelperClass.ReadCsv(Path.Combine(_testDataPath, "1.csv"));
            var targetFileContent = HelperClass.ReadCsv(Path.Combine(_testDataPath, "2.csv"));

            Assert.IsNotNull(sourceFileContent, "Source CSV file should exist and be readable.");
            Assert.IsNotNull(targetFileContent, "Target CSV file should exist and be readable.");

            Assert.IsFalse(sourceFileContent.Any(t => t.Task == taskToDrag.Task), "Dragged task should be removed from source CSV.");
            Assert.AreEqual(1, sourceFileContent.Count, "Source CSV should contain only the remaining task.");
            Assert.AreEqual(otherSourceTask.Task, sourceFileContent[0].Task, "Remaining task not found in source CSV.");

            Assert.IsTrue(targetFileContent.Any(t => t.Task == taskToDrag.Task), "Dragged task should be added to target CSV.");
            Assert.AreEqual(2, targetFileContent.Count, "Target CSV should contain original and new task.");

            var movedTaskInTargetCsv = targetFileContent.First(t => t.Task == taskToDrag.Task);
            Assert.AreEqual("High", movedTaskInTargetCsv.Importance, "Importance in target CSV is incorrect.");
            Assert.AreEqual("Low", movedTaskInTargetCsv.Urgency, "Urgency in target CSV is incorrect.");
        }
    }
}
