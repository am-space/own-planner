using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OwnPlanner.Infrastructure.Account;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Tests.Services;

public sealed class ExportTempFileCleanupServiceTests : IDisposable
{
	private readonly string _dir = Path.Combine(
		Path.GetTempPath(),
		"ownplanner-export-cleanup-tests",
		Guid.NewGuid().ToString("N"));

	public ExportTempFileCleanupServiceTests() => Directory.CreateDirectory(_dir);

	public void Dispose()
	{
		if (Directory.Exists(_dir))
		{
			Directory.Delete(_dir, recursive: true);
		}
	}

	[Fact]
	public void CleanupOnce_RemovesStaleExportEntries_KeepsFreshAndUnrelated()
	{
		var now = DateTime.UtcNow;
		var old = now - ExportTempFileCleanupService.MaxAge - TimeSpan.FromMinutes(5);

		// Stale orphan ZIP (matches prefix, idle past retention) -> removed.
		var staleZip = Path.Combine(_dir, $"{AccountExportService.TempEntryPrefix}aaa.zip");
		File.WriteAllText(staleZip, "x");
		File.SetLastWriteTimeUtc(staleZip, old);

		// Stale orphan working directory -> removed (recursively).
		var staleDir = Path.Combine(_dir, $"{AccountExportService.TempEntryPrefix}bbb");
		Directory.CreateDirectory(staleDir);
		File.WriteAllText(Path.Combine(staleDir, "ownplanner-data.db"), "x");
		Directory.SetLastWriteTimeUtc(staleDir, old);

		// Fresh export still in flight (matches prefix, recent) -> kept.
		var freshZip = Path.Combine(_dir, $"{AccountExportService.TempEntryPrefix}ccc.zip");
		File.WriteAllText(freshZip, "x");

		// Unrelated old file -> kept (does not match the prefix).
		var unrelated = Path.Combine(_dir, "something-else.tmp");
		File.WriteAllText(unrelated, "x");
		File.SetLastWriteTimeUtc(unrelated, old);

		var removed = ExportTempFileCleanupService.CleanupOnce(_dir, now, NullLogger.Instance);

		removed.Should().Be(2);
		File.Exists(staleZip).Should().BeFalse();
		Directory.Exists(staleDir).Should().BeFalse();
		File.Exists(freshZip).Should().BeTrue();
		File.Exists(unrelated).Should().BeTrue();
	}
}
