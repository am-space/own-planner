using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Account;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Account;

/// <summary>
/// Builds an account data export by snapshotting the per-user SQLite planner database with
/// SQLite <c>VACUUM INTO</c> — a transactionally consistent copy that folds in any WAL contents —
/// and packing it, together with a README, into a ZIP archive. The central auth database is
/// intentionally excluded: it holds credentials and other users' rows and is not the user's
/// planning data.
/// </summary>
public sealed class AccountExportService(IPlannerDbContextFactory dbContextFactory) : IAccountExportService
{
	internal const string DatabaseEntryName = "ownplanner-data.db";
	internal const string ReadmeEntryName = "README.txt";

	/// <summary>
	/// Prefix shared by every temp artifact this service creates (the working directory and the ZIP).
	/// A background sweeper keys off this to reap any orphan left by a crash. Keep them in sync.
	/// </summary>
	public const string TempEntryPrefix = "ownplanner-export-";

	public async Task<AccountExport> CreateExportAsync(CancellationToken cancellationToken = default)
	{
		// Snapshot into a unique working directory so the VACUUM INTO target never pre-exists
		// (SQLite refuses to overwrite an existing file), then pack and remove it.
		var workingDirectory = Path.Combine(Path.GetTempPath(), $"{TempEntryPrefix}{Guid.NewGuid():N}");
		Directory.CreateDirectory(workingDirectory);
		var snapshotPath = Path.Combine(workingDirectory, DatabaseEntryName);
		var zipPath = Path.Combine(Path.GetTempPath(), $"{TempEntryPrefix}{Guid.NewGuid():N}.zip");

		try
		{
			await using (var db = await dbContextFactory.CreateAsync(cancellationToken).ConfigureAwait(false))
			{
				// VACUUM INTO writes a clean, self-contained copy of the live database. The target
				// path is server-generated (a temp GUID path), but VACUUM cannot take a bound
				// parameter, so single-quotes are doubled defensively before interpolation.
				var target = snapshotPath.Replace("'", "''");
				var sql = $"VACUUM INTO '{target}'";
				await db.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
			}

			using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
			{
				archive.CreateEntryFromFile(snapshotPath, DatabaseEntryName, CompressionLevel.Optimal);

				var readmeEntry = archive.CreateEntry(ReadmeEntryName, CompressionLevel.Optimal);
				await using var writer = new StreamWriter(readmeEntry.Open());
				await writer.WriteAsync(ReadmeContent).ConfigureAwait(false);
			}
		}
		catch
		{
			TryDeleteFile(zipPath);
			throw;
		}
		finally
		{
			// The snapshot now lives inside the ZIP; drop the intermediate working copy either way.
			TryDeleteDirectory(workingDirectory);
		}

		var fileName = $"ownplanner-export-{DateTime.UtcNow:yyyyMMdd}.zip";
		return new AccountExport(zipPath, fileName, "application/zip");
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch
		{
			// Best effort: a leftover temp directory is harmless and will be reclaimed by the OS.
		}
	}

	private static void TryDeleteFile(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
			// Best effort.
		}
	}

	private const string ReadmeContent =
		"""
		OwnPlanner — your data export
		=============================

		This archive contains a complete, self-contained copy of your OwnPlanner planning data,
		exported at the time you requested it.

		Files
		-----
		- ownplanner-data.db : Your planning data as a standard SQLite database.
		- README.txt         : This file.

		What's inside ownplanner-data.db
		--------------------------------
		The database holds only your own data, in these tables:
		  - PlanningContexts : your planning contexts (work, personal, etc.)
		  - Goals            : your goals
		  - TaskLists        : your task lists
		  - TaskItems        : the tasks within those lists
		  - NoteLists        : your note lists
		  - NoteItems        : the notes within those lists

		Opening it
		----------
		ownplanner-data.db is a regular SQLite 3 database. You can open it with:
		  - the `sqlite3` command-line tool, e.g.  sqlite3 ownplanner-data.db ".tables"
		  - DB Browser for SQLite (https://sqlitebrowser.org/), a free graphical viewer
		  - any library or tool that reads SQLite, in your language of choice

		Note: this export does not include your account credentials or AI usage statistics,
		which are not part of your planning data.
		""";
}
