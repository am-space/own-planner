using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OwnPlanner.Application.Auth;
using OwnPlanner.Domain.Users;
using OwnPlanner.Infrastructure.Account;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Repositories;

namespace OwnPlanner.Infrastructure.Tests.Account;

public class AccountDeletionServiceTests
{
	// Foreign Keys=True so ON DELETE CASCADE is enforced for the principal->dependent relationships.
	private static AuthDbContext CreateDb(out SqliteConnection conn)
	{
		conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=True");
		conn.Open();
		var options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(conn).Options;
		var db = new AuthDbContext(options);
		db.Database.EnsureCreated();
		return db;
	}

	private static User SeedUserWithDependents(AuthDbContext db)
	{
		var user = new User("user@example.com", "tester", "hashed-password");
		db.Users.Add(user);
		db.PersonalAccessTokens.Add(new PersonalAccessToken(user.Id, "CLI", "token-hash"));
		db.UserDailyUsages.Add(new UserDailyUsage(user.Id, new DateOnly(2026, 6, 27)));
		db.SaveChanges();
		return user;
	}

	[Fact]
	public async Task DeleteAccountAsync_CorrectPassword_RemovesUserCascadedDataAndPlannerDatabase()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;

		var user = SeedUserWithDependents(db);

		var authService = Substitute.For<IAuthService>();
		authService.VerifyPassword("correct", user.PasswordHash).Returns(true);
		var factory = Substitute.For<IPlannerDbContextFactory>();

		var service = new AccountDeletionService(
			new UserRepository(db),
			authService,
			factory,
			NullLogger<AccountDeletionService>.Instance);

		var result = await service.DeleteAccountAsync(user.Id, "correct", ct);

		result.Success.Should().BeTrue();
		(await db.Users.CountAsync(ct)).Should().Be(0);
		(await db.PersonalAccessTokens.CountAsync(ct)).Should().Be(0);
		(await db.UserDailyUsages.CountAsync(ct)).Should().Be(0);
		await factory.Received(1).DeleteUserDatabaseAsync(user.Id.ToString(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task DeleteAccountAsync_WrongPassword_DeletesNothing()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;

		var user = SeedUserWithDependents(db);

		var authService = Substitute.For<IAuthService>();
		authService.VerifyPassword(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
		var factory = Substitute.For<IPlannerDbContextFactory>();

		var service = new AccountDeletionService(
			new UserRepository(db),
			authService,
			factory,
			NullLogger<AccountDeletionService>.Instance);

		var result = await service.DeleteAccountAsync(user.Id, "wrong", ct);

		result.Success.Should().BeFalse();
		result.ErrorMessage.Should().Be("Password is incorrect.");
		(await db.Users.CountAsync(ct)).Should().Be(1);
		(await db.PersonalAccessTokens.CountAsync(ct)).Should().Be(1);
		await factory.DidNotReceive().DeleteUserDatabaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task DeleteAccountAsync_UnknownUser_ReturnsFailure()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;

		var authService = Substitute.For<IAuthService>();
		var factory = Substitute.For<IPlannerDbContextFactory>();

		var service = new AccountDeletionService(
			new UserRepository(db),
			authService,
			factory,
			NullLogger<AccountDeletionService>.Instance);

		var result = await service.DeleteAccountAsync(Guid.NewGuid(), "whatever", ct);

		result.Success.Should().BeFalse();
		await factory.DidNotReceive().DeleteUserDatabaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}
}
