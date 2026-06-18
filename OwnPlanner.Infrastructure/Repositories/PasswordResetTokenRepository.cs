using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Users;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class PasswordResetTokenRepository(AuthDbContext context)
	: RepositoryBase<PasswordResetToken, AuthDbContext>(context), IPasswordResetTokenRepository
{
	public async Task<PasswordResetToken?> FindActiveByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
	{
		var now = DateTime.UtcNow;
		return await Set.FirstOrDefaultAsync(
			token => token.TokenHash == tokenHash && token.ConsumedAt == null && token.ExpiresAt > now,
			cancellationToken);
	}

	async Task<PasswordResetToken> IPasswordResetTokenRepository.AddAsync(PasswordResetToken token, CancellationToken cancellationToken)
	{
		Set.Add(token);
		await Db.SaveChangesAsync(cancellationToken);
		return token;
	}

	async Task<PasswordResetToken> IPasswordResetTokenRepository.UpdateAsync(PasswordResetToken token, CancellationToken cancellationToken)
	{
		Set.Update(token);
		await Db.SaveChangesAsync(cancellationToken);
		return token;
	}

	public async Task InvalidateActiveForUserAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		var now = DateTime.UtcNow;
		var activeTokens = await Set
			.Where(token => token.UserId == userId && token.ConsumedAt == null && token.ExpiresAt > now)
			.ToListAsync(cancellationToken);

		if (activeTokens.Count == 0)
		{
			return;
		}

		foreach (var token in activeTokens)
		{
			token.Consume();
		}

		await Db.SaveChangesAsync(cancellationToken);
	}
}
