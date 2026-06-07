using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Users;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class PersonalAccessTokenRepository(AuthDbContext context)
	: RepositoryBase<PersonalAccessToken, AuthDbContext>(context), IPersonalAccessTokenRepository
{
	public async Task<PersonalAccessToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
		=> await Set.FirstOrDefaultAsync(token => token.Id == id, cancellationToken);

	public async Task<IReadOnlyList<PersonalAccessToken>> ListByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
		=> await Set.Where(token => token.UserId == userId).ToListAsync(cancellationToken);

	public async Task<PersonalAccessToken?> FindActiveByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
		=> await Set.FirstOrDefaultAsync(
			token => token.TokenHash == tokenHash && token.RevokedAt == null,
			cancellationToken);

	async Task<PersonalAccessToken> IPersonalAccessTokenRepository.AddAsync(PersonalAccessToken token, CancellationToken cancellationToken)
	{
		Set.Add(token);
		await Db.SaveChangesAsync(cancellationToken);
		return token;
	}

	async Task<PersonalAccessToken> IPersonalAccessTokenRepository.UpdateAsync(PersonalAccessToken token, CancellationToken cancellationToken)
	{
		Set.Update(token);
		await Db.SaveChangesAsync(cancellationToken);
		return token;
	}
}
