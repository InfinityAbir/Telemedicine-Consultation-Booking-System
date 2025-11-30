using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TelemedSystem.Services
{
    /// <summary>
    /// Background service that runs a SQL Server full database backup on a schedule.
    /// </summary>
    public class BackupService : BackgroundService
    {
        private readonly ILogger<BackupService> _logger;
        private readonly IConfiguration _config;
        private readonly string _connectionString;
        private readonly string _backupDir;
        private readonly int _retentionDays;
        private readonly double _scheduleHours;
        private readonly string _databaseName;
        private readonly int _commandTimeoutSeconds;

        public BackupService(ILogger<BackupService> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;

            // Read connection string (use the name you already have in appsettings.json)
            _connectionString = _config.GetConnectionString("DefaultConnection")
                                ?? throw new InvalidOperationException("DefaultConnection not found in configuration.");

            // Read DatabaseBackup section, with defaults
            var dbCfg = _config.GetSection("DatabaseBackup");
            _backupDir = dbCfg.GetValue<string>("BackupDirectory")
                         ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "Backups"); // default fallback
            _retentionDays = dbCfg.GetValue<int?>("RetentionDays") ?? 14;
            _scheduleHours = dbCfg.GetValue<double?>("ScheduleHours") ?? 24.0;
            _databaseName = dbCfg.GetValue<string>("DatabaseName") ?? GetDatabaseNameFromConnectionString(_connectionString);
            _commandTimeoutSeconds = dbCfg.GetValue<int?>("CommandTimeoutSeconds") ?? 60 * 60; // default 1 hour

            _logger.LogInformation("BackupService initialized. Database: {db}, BackupDir: {dir}, ScheduleHours: {hrs}, RetentionDays: {days}",
                _databaseName, _backupDir, _scheduleHours, _retentionDays);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BackupService starting at: {time}", DateTime.UtcNow);

            // Run immediately at startup (optional)
            await RunBackupCycle(stoppingToken);

            // Then loop with delay
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromHours(_scheduleHours), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                await RunBackupCycle(stoppingToken);
            }

            _logger.LogInformation("BackupService stopping at: {time}", DateTime.UtcNow);
        }

        private async Task RunBackupCycle(CancellationToken cancellationToken)
        {
            try
            {
                // Ensure backup directory exists
                if (!Directory.Exists(_backupDir))
                {
                    Directory.CreateDirectory(_backupDir);
                    _logger.LogInformation("Created backup directory: {dir}", _backupDir);
                }

                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                var fileName = $"{_databaseName}_full_{timestamp}.bak";
                var fullPath = Path.Combine(_backupDir, fileName);

                // Build T-SQL for backup
                // NOTE: For some hosting (e.g., Azure SQL), BACKUP DATABASE won't work.
                var backupSql = $@"
BACKUP DATABASE [{_databaseName}]
TO DISK = N'{fullPath}'
WITH FORMAT, INIT, NAME = N'{_databaseName}-FullBackup-{timestamp}';";

                _logger.LogInformation("Starting backup of database {db} to {path}", _databaseName, fullPath);

                using (var conn = new SqlConnection(_connectionString))
                using (var cmd = new SqlCommand(backupSql, conn))
                {
                    cmd.CommandTimeout = _commandTimeoutSeconds;
                    await conn.OpenAsync(cancellationToken);
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                _logger.LogInformation("Backup completed: {file}", fullPath);

                // Prune old backups
                PruneOldBackups();
            }
            catch (OperationCanceledException)
            {
                // cancellation requested - ignore
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during backup cycle");
            }
        }

        private void PruneOldBackups()
        {
            try
            {
                var di = new DirectoryInfo(_backupDir);
                var files = di.GetFiles("*_full_*.bak");
                var threshold = DateTime.UtcNow.AddDays(-_retentionDays);

                foreach (var f in files)
                {
                    if (f.CreationTimeUtc < threshold)
                    {
                        try
                        {
                            f.Delete();
                            _logger.LogInformation("Deleted old backup: {file}", f.FullName);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete old backup {file}", f.FullName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while pruning backups in {dir}", _backupDir);
            }
        }

        private static string GetDatabaseNameFromConnectionString(string conn)
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(conn);
                // Database or Initial Catalog
                return string.IsNullOrWhiteSpace(builder.InitialCatalog) ? "Database" : builder.InitialCatalog;
            }
            catch
            {
                return "Database";
            }
        }
    }
}
