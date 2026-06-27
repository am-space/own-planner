using OwnPlanner.Infrastructure.Account;

namespace OwnPlanner.Web.Server.Services;

/// <summary>
/// Periodically reaps stale account-export temp artifacts. The normal lifecycle deletes the export
/// archive when the response stream closes (<c>FileOptions.DeleteOnClose</c>) and the working
/// snapshot directory right after packaging, but a process crash, an aborted request, or an
/// OS-level delete failure can leave an orphan in the temp directory. This sweep is the backstop:
/// it removes any <see cref="AccountExportService.TempEntryPrefix"/> entry that has been idle longer
/// than <see cref="MaxAge"/>, so in-flight exports (seconds old) are never touched.
/// </summary>
public sealed class ExportTempFileCleanupService(ILogger<ExportTempFileCleanupService> logger) : BackgroundService
{
	private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(15);

	/// <summary>Entries idle longer than this are considered orphaned and removed.</summary>
	internal static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(SweepInterval);
		do
		{
			try
			{
				CleanupOnce(Path.GetTempPath(), DateTime.UtcNow, logger);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Export temp cleanup sweep failed");
			}
		}
		while (await WaitForNextTickAsync(timer, stoppingToken).ConfigureAwait(false));
	}

	private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
	{
		try
		{
			return await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return false;
		}
	}

	/// <summary>
	/// Deletes every export temp entry (file or directory) under <paramref name="tempDirectory"/>
	/// whose last-write time is older than <see cref="MaxAge"/> relative to <paramref name="utcNow"/>.
	/// Returns the number of entries removed. Best-effort: IO failures are logged and skipped.
	/// </summary>
	internal static int CleanupOnce(string tempDirectory, DateTime utcNow, ILogger logger)
	{
		var cutoff = utcNow - MaxAge;
		var removed = 0;

		foreach (var path in Directory.EnumerateFileSystemEntries(tempDirectory, $"{AccountExportService.TempEntryPrefix}*"))
		{
			try
			{
				// Only reap entries idle past the retention window so a streaming export is never removed.
				if (File.GetLastWriteTimeUtc(path) > cutoff)
				{
					continue;
				}

				if (Directory.Exists(path))
				{
					Directory.Delete(path, recursive: true);
				}
				else
				{
					File.Delete(path);
				}

				removed++;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				logger.LogWarning(ex, "Failed to delete stale export temp entry {Path}", path);
			}
		}

		if (removed > 0)
		{
			logger.LogInformation("Export temp cleanup removed {Count} stale entr{Plural}", removed, removed == 1 ? "y" : "ies");
		}

		return removed;
	}
}
