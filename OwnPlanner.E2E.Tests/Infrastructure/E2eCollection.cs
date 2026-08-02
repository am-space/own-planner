namespace OwnPlanner.E2E.Tests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class E2eCollection : ICollectionFixture<E2eWebApplicationFactory>
{
	public const string Name = "OwnPlanner E2E";
}
