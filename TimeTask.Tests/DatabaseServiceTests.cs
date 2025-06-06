using Microsoft.VisualStudio.TestTools.UnitTesting;
using TimeTask; // Ensure this matches the main project's namespace
using System;
using System.Collections.Generic; // For List<ItemGrid>

namespace TimeTask.Tests
{
    [TestClass]
    public class DatabaseServiceTests
    {
        [TestMethod]
        public void Constructor_WithValidConnectionString_DoesNotThrow()
        {
            // Arrange
            string validConnectionString = "Host=localhost;Port=5432;Username=user;Password=pass;Database=mydb;";

            // Act
            var dbService = new DatabaseService(validConnectionString);

            // Assert
            Assert.IsNotNull(dbService, "DatabaseService instance should not be null.");
            // Further assertion could involve checking an internal field if exposed for testing,
            // but simply not throwing is a basic check for this constructor.
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Constructor_WithNullConnectionString_ThrowsArgumentException()
        {
            // Act
            new DatabaseService(null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Constructor_WithEmptyConnectionString_ThrowsArgumentException()
        {
            // Act
            new DatabaseService("");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Constructor_WithWhitespaceConnectionString_ThrowsArgumentException()
        {
            // Act
            new DatabaseService("   ");
        }

        [TestMethod]
        public void GetTeamTasks_ServiceConstructedWithNullConnectionStringViaDefaultConstructor_ReturnsEmptyList()
        {
            // This test relies on the default constructor correctly setting _connectionString to null
            // if "TeamTasksDbConnection" is not in App.config (which it isn't by default for tests).
            // The default constructor of DatabaseService attempts to read from App.config.
            // For a unit test environment, App.config of the main project might not be read,
            // or a test App.config might be in play.
            // Assuming it results in a null _connectionString:

            // Arrange
            var dbService = new DatabaseService(); // Uses default constructor
                                               // This will print "Connection string 'TeamTasksDbConnection' not found..." to console.

            // Act
            List<ItemGrid> tasks = dbService.GetTeamTasks("anyRole");

            // Assert
            Assert.IsNotNull(tasks, "Task list should not be null.");
            Assert.AreEqual(0, tasks.Count, "Task list should be empty if connection string is not configured.");
        }

        [TestMethod]
        public void GetTeamTasks_ServiceConstructedWithValidButUnreachableConnectionString_ReturnsEmptyListAndLogsError()
        {
            // Arrange: Use a connection string that is syntactically valid but points to a non-existent server.
            // Npgsql will attempt to connect and fail, typically throwing NpgsqlException or timeout.
            string unreachableConnectionString = "Host=nonexistentserver123abc;Port=5432;Username=test;Password=test;Database=testdb;Timeout=1;No Reset On Close=true";
            // Timeout=1 to make it fail faster. No Reset On Close=true to avoid issues with pooler state in tests.
            var dbService = new DatabaseService(unreachableConnectionString);

            // Act
            // We expect console output for the error. This is hard to assert in a standard unit test.
            // We primarily check that it handles the exception and returns an empty list.
            List<ItemGrid> tasks = dbService.GetTeamTasks("anyRole");

            // Assert
            Assert.IsNotNull(tasks);
            Assert.AreEqual(0, tasks.Count, "Task list should be empty when DB is unreachable.");
            // Manual verification of console output for "Database Error in GetTeamTasks" would be needed.
        }

        [TestMethod]
        public void GetTeamTasks_WhenDbCallSucceeds_MapsDataCorrectly_Placeholder()
        {
            // This test requires significant mocking of NpgsqlConnection, NpgsqlCommand, and NpgsqlDataReader
            // to simulate a successful database call and data retrieval without a live database.
            // Such mocking is complex to set up dynamically in this environment.
            Assert.Inconclusive("Full data mapping test requires Npgsql mocking framework (e.g., Moq) and setup.");
        }
    }

    [TestClass]
    public class SettingsAndConfigurationTests
    {
        // This class would house tests for logic in LlmSettingsWindow.xaml.cs if it were refactored
        // for better testability (e.g., static helper methods for connection string construction).

        [TestMethod]
        public void LlmSettings_ConnectionStringConstruction_Placeholder()
        {
            // Example: If LlmSettingsWindow.xaml.cs had:
            // public static string ConstructTeamTasksConnectionString(string host, string port, string dbName, string user, string password)
            // {
            //     return $"Host={host};Port={port};Database={dbName};Username={user};Password={password};";
            // }
            // Then we could test it here.
            Assert.Inconclusive("Connection string construction logic in LlmSettingsWindow.xaml.cs is not currently refactored for easy unit testing.");
        }
    }
}
