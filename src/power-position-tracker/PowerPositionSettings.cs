namespace power_position_tracker
{
    public class PowerPositionSettings
    {
        /// <summary>
        /// Interval in minutes between extractions (default: 5)
        /// </summary>
        public int IntervalMinutes { get; set; } = 5;

        /// <summary>
        /// Directory path for position report CSV files (e.g., "C:\PowerReports\Output")
        /// </summary>
        public string OutputDirectory { get; set; } = "Output";

        /// <summary>
        /// Directory path for execution audit log files (e.g., "C:\PowerReports\Audit")
        /// </summary>
        public string AuditDirectory { get; set; } = "Audit";

        /// <summary>
        /// Directory path for dlq (e.g., "C:\PowerReports\Dlq")
        /// </summary>
        public string DlqDirectory { get; set; } = "Dlq";

        /// <summary>
        /// Time zone ID for London Local Time (default: "Europe/London")
        /// </summary>
        public string TimeZoneId { get; set; } = "Europe/London";

        /// <summary>
        /// Optional: Runtime for adhoc reruns (UTC DateTime)
        /// If not set, Service uses current UTC time
        /// Can be overridden by environment variable DOTNET_RUNTIME
        /// Format: ISO 8601 (e.g., "2025-12-10T14:30:00Z")
        /// </summary>
        public DateTime? RunTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Number of retry within the same extraction interval in case of failure (default: 3)
        /// </summary>
        public int RetryAttempts { get; set; } = 3;

        /// <summary>
        /// Delay in seconds between retry attempts (default: 10)
        /// </summary>
        public int RetryDelaySeconds { get; set; } = 10;

        /// <summary>
        /// Maximum total retry attempts including DLQ processing (default: 5)
        /// After this limit, extraction requires human intervention
        /// </summary>
        public int MaxDlqRetryAttempts { get; set; } = 5;
    }
}
