using Npgsql;
using System;
using System.Collections.Generic;
using System.Configuration; // For reading App.config
using System.Data; // Required for CommandType

namespace TimeTask // Assuming this is the root namespace
{
    public class DatabaseService
    {
        private string _connectionString;

        public DatabaseService()
        {
            // Connection string will be properly managed later.
            // For now, it might be null or a placeholder.
            // We'll add a setting for this in App.config in a subsequent step.
            _connectionString = ReadConnectionStringFromSettings();
        }

        // Constructor that allows passing a connection string directly (e.g., for testing)
        public DatabaseService(string connectionString)
        {
             if (string.IsNullOrWhiteSpace(connectionString))
             {
                 throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
             }
            _connectionString = connectionString;
        }

        private string ReadConnectionStringFromSettings()
        {
            // This will be improved when App.config settings are formally added.
            // For now, it's a placeholder to make the class compilable.
            // It will look for "TeamTasksDbConnection" which we'll add later.
            string cs = ConfigurationManager.ConnectionStrings["TeamTasksDbConnection"]?.ConnectionString;
            if (string.IsNullOrWhiteSpace(cs))
            {
                Console.WriteLine("DatabaseService: Connection string 'TeamTasksDbConnection' not found in App.config or is empty. Service may not function correctly until configured.");
                // Return a dummy or null to indicate it's not configured,
                // or throw an exception if the service cannot operate without it.
                // For now, let's return null and handle it in methods that use it.
                return null;
            }
            return cs;
        }

        // Placeholder method to fetch tasks.
        // This will be fully implemented in a later step.
        public List<ItemGrid> GetTeamTasks(string userRoleFilter)
        {
            var tasks = new List<ItemGrid>();
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                Console.WriteLine("GetTeamTasks: Connection string is not configured. Cannot fetch tasks.");
                // Optionally, throw new InvalidOperationException("Database connection string is not configured.");
                return tasks; // Return empty list if not configured
            }

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                try
                {
                    conn.Open();
                    // Example query, will be refined.
                    // It should select tasks based on userRoleFilter and avoid already completed/synced tasks.
                    // The schema has AssignedRole for filtering.
                    // We also need a way to track already synced tasks to avoid duplicates if this method is called multiple times.
                    // This might involve checking SourceTaskID against existing tasks in the UI, or adding a local "IsSynced" flag.
                    // For now, a simple select based on role.
                    string query = "SELECT TaskID, SourceTaskID, TaskDescription, TaskType, CreationTime, CompletionTime, CompletionStatus, AssignedRole FROM TeamTasks WHERE CompletionStatus != 'Completed'";
                    if (!string.IsNullOrWhiteSpace(userRoleFilter))
                    {
                        query += " AND (AssignedRole = @UserRole OR AssignedRole IS NULL OR AssignedRole = '')"; // Also fetch tasks not assigned to any specific role
                    }

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        if (!string.IsNullOrWhiteSpace(userRoleFilter))
                        {
                            cmd.Parameters.AddWithValue("@UserRole", userRoleFilter);
                        }

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                tasks.Add(new ItemGrid
                                {
                                    SourceTaskID = reader["SourceTaskID"] as string, // From DB
                                    Task = reader["TaskDescription"] as string, // Map TaskDescription to Task
                                    TaskType = reader["TaskType"] as string,
                                    CreatedDate = reader["CreationTime"] != DBNull.Value ? Convert.ToDateTime(reader["CreationTime"]) : DateTime.MinValue, // Map CreationTime
                                    CompletionTime = reader["CompletionTime"] != DBNull.Value ? Convert.ToDateTime(reader["CompletionTime"]) : (DateTime?)null,
                                    CompletionStatus = reader["CompletionStatus"] as string,
                                    AssignedRole = reader["AssignedRole"] as string,
                                    // ItemGrid specific fields (defaults or decide if they map from DB)
                                    Score = 0, // Default score, can be adjusted
                                    IsActive = (reader["CompletionStatus"] as string) != "Completed", // Assuming "Completed" means not active
                                    Importance = "Unknown", // Default, might be derived or set later
                                    Urgency = "Unknown",    // Default, might be derived or set later
                                    LastModifiedDate = DateTime.Now // Or map from a DB field if available
                                });
                            }
                        }
                    }
                }
                catch (NpgsqlException ex)
                {
                    // Log the error (e.g., Console.WriteLine or a proper logging mechanism)
                    Console.WriteLine($"Database Error in GetTeamTasks: {ex.Message}");
                    // Depending on requirements, re-throw, or return empty list/null
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"General Error in GetTeamTasks: {ex.Message}");
                }
            }
            return tasks;
        }
    }
}
