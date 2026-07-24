using System.IO;
using System.Text.Json;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Coordinates between the main WPF app and the background Agent
    /// via a shared agent_status.json file in the app data folder.
    /// 
    /// Both app and agent read/write this file to avoid stepping on
    /// each other (e.g., both trying to update prices simultaneously).
    /// 
    /// SQLite WAL mode handles database concurrency — this service
    /// only handles awareness of what the other process is doing.
    /// </summary>
    public class AgentCoordinator
    {
        private static string StatusFilePath =>
            Path.Combine(AppFolderService.RootFolder, "agent_status.json");

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Read the current agent status. Returns defaults if file doesn't exist.
        /// </summary>
        public AgentStatus ReadStatus()
        {
            try
            {
                if (!File.Exists(StatusFilePath))
                    return new AgentStatus();

                string json = File.ReadAllText(StatusFilePath);
                return JsonSerializer.Deserialize<AgentStatus>(json, _jsonOptions)
                    ?? new AgentStatus();
            }
            catch
            {
                // File may be mid-write by the other process — return defaults
                return new AgentStatus();
            }
        }

        /// <summary>
        /// Write the current status to disk.
        /// </summary>
        public void WriteStatus(AgentStatus status)
        {
            try
            {
                string dir = Path.GetDirectoryName(StatusFilePath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(status, _jsonOptions);
                File.WriteAllText(StatusFilePath, json);
            }
            catch
            {
                // Best-effort — don't crash if write fails
            }
        }

        /// <summary>
        /// Check if the agent is currently running an update.
        /// If the status has been non-idle for more than 15 minutes,
        /// assume the process crashed and treat it as idle.
        /// </summary>
        public bool IsAgentUpdating()
        {
            var status = ReadStatus();
            if (status.Status == "idle") return false;

            // If updating started more than 15 minutes ago, it's stale
            if (status.UpdateStartedAt.HasValue &&
                (DateTime.UtcNow - status.UpdateStartedAt.Value).TotalMinutes > 15)
            {
                // Reset stale status
                SetIdle();
                return false;
            }

            return true;
        }

        /// <summary>
        /// Mark the app as currently updating (so the agent skips its run).
        /// </summary>
        public void SetAppUpdating(string task = "updating_pool")
        {
            var status = ReadStatus();
            status.Status = task;
            status.UpdateStartedAt = DateTime.UtcNow;
            WriteStatus(status);
        }

        /// <summary>
        /// Mark as idle (done updating).
        /// </summary>
        public void SetIdle()
        {
            var status = ReadStatus();
            status.Status = "idle";
            WriteStatus(status);
        }

        /// <summary>
        /// Record that a full pool update completed.
        /// </summary>
        public void RecordPoolUpdate()
        {
            var status = ReadStatus();
            status.Status = "idle";
            status.LastPoolUpdate = DateTime.UtcNow;
            WriteStatus(status);
        }

        /// <summary>
        /// Record that a price update completed.
        /// </summary>
        public void RecordPriceUpdate()
        {
            var status = ReadStatus();
            status.Status = "idle";
            status.LastPriceUpdate = DateTime.UtcNow;
            WriteStatus(status);
        }

        /// <summary>
        /// Record that a backup completed.
        /// </summary>
        public void RecordBackup()
        {
            var status = ReadStatus();
            status.LastBackup = DateTime.UtcNow;
            WriteStatus(status);
        }

        /// <summary>
        /// Check how long since the last price update.
        /// Returns null if never updated.
        /// </summary>
        public TimeSpan? TimeSinceLastPriceUpdate()
        {
            var status = ReadStatus();
            if (status.LastPriceUpdate == null) return null;
            return DateTime.UtcNow - status.LastPriceUpdate.Value;
        }
    }

    /// <summary>
    /// Status model stored in agent_status.json.
    /// Shared between the main app and the background agent.
    /// </summary>
    public class AgentStatus
    {
        /// <summary>
        /// Current status: "idle", "updating_pool", "updating_prices", "backing_up"
        /// </summary>
        public string Status { get; set; } = "idle";

        /// <summary>When the current non-idle status started. Used for staleness detection.</summary>
        public DateTime? UpdateStartedAt { get; set; }

        /// <summary>When the pool database was last fully updated.</summary>
        public DateTime? LastPoolUpdate { get; set; }

        /// <summary>When prices were last refreshed.</summary>
        public DateTime? LastPriceUpdate { get; set; }

        /// <summary>When the last backup was created.</summary>
        public DateTime? LastBackup { get; set; }
    }
}