# Infrastructure Guidance

This directory owns persistence, filesystem access, external SDK adapters, and the two EF Core
contexts. Keep domain rules and use-case orchestration in the inward layers.

## Persistence and isolation

- Use `AuthDbContext` only for accounts, credentials, sessions, quotas, and user-to-database mapping.
- Resolve planning repositories through a user-bound `AppDbContext` or `IPlannerDbContextFactory`.
  Do not accept a database path or tenant identity from untrusted input.
- Preserve cancellation-token flow and async disposal for database, filesystem, and network I/O.
- Keep logs structured and exclude credentials, token material, prompts containing personal data,
  and exported content.

## EF Core migrations

Never hand-write migrations. Create them with the CLI and name the context explicitly:

```sh
dotnet ef migrations add <Name> --project OwnPlanner.Infrastructure --context AppDbContext
dotnet ef migrations add <Name> --project OwnPlanner.Infrastructure --context AuthDbContext
```

Append `--startup-project <path>` only when the startup project cannot be inferred. Review the
generated migration and snapshot, and verify that it targets only the intended database.

## Validation

- Put repository, adapter, and context tests in `OwnPlanner.Infrastructure.Tests`.
- For changes to context selection or account export/deletion, cover two users and prove that one
  cannot read, mutate, export, or delete the other's data.
- Run `dotnet test OwnPlanner.Infrastructure.Tests/OwnPlanner.Infrastructure.Tests.csproj` and build
  every directly affected entry point.
