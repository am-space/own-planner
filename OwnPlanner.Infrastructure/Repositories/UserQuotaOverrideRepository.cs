using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Users;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class UserQuotaOverrideRepository(AuthDbContext context)
	: RepositoryBase<UserQuotaOverride, AuthDbContext>(context), IUserQuotaOverrideRepository
{
	public async Task<UserQuotaOverride?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
		=> await Set.FirstOrDefaultAsync(o => o.UserId == userId, cancellationToken);
}
