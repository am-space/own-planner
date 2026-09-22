# Supported dependency baselines

The dependency migration in [#31](https://github.com/am-space/own-planner/issues/31) landed primarily
in [#50](https://github.com/am-space/own-planner/pull/50), followed by the native test-runner migration
in [#65](https://github.com/am-space/own-planner/pull/65). The decisions are recorded in
[ADR-0017](adr/0017-supported-dependency-baselines.md) and
[ADR-0025](adr/0025-microsoft-testing-platform.md); historical records retain their original versions.

## Supported majors and exclusions

Audited on 2026-09-22:

| Area | Supported baseline | Compatibility decision |
|---|---|---|
| Application runtime | .NET / ASP.NET Core 10 | No runtime-major upgrade is required by this migration. |
| Frontend runtime | Node 24 LTS; `@types/node` 24 | `.node-version`, CI, and Docker agree. Node 26 types are deliberately excluded. |
| Frontend build | Vite 8, React plugin 6 | Existing strict TypeScript configuration is retained. |
| Frontend lint | ESLint 10, globals 17, React Refresh plugin 0.5 | No rule suppression or forced peer overrides are needed. |
| UI | React 19, MUI Material/icons 9 | Slots and responsive inspectors are covered by browser checks. |
| MCP | All five direct SDK references at 2.2.0 | HTTP is explicitly stateless; bearer and tenant boundaries remain unchanged. |
| SQLite | SQLitePCLRaw bundle 3.0.5 | Bundled `e_sqlite3` native provider; no application schema migration is needed for the package change. |
| Tests | xUnit.net v3 package 4.0.1, MTP 2.4, Coverlet MTP 10 | The former xUnit exclusion was resolved by #65. |
| TypeScript | 5.9 | TypeScript 7 remains deferred to [#48](https://github.com/am-space/own-planner/issues/48). |
| OpenAPI.NET | 2.12 | Version 3 remains deferred to [#49](https://github.com/am-space/own-planner/issues/49). |

The official [typescript-eslint support range](https://typescript-eslint.io/users/dependency-versions/)
is `>=4.8.4 <6.1.0`. The [OpenAPI.NET compatibility note](https://github.com/microsoft/OpenAPI.NET/blob/main/CHANGELOG.md#300-2025-11-11)
directs ASP.NET Core 10 consumers to 2.x; the installed `Microsoft.AspNetCore.OpenApi` 10.0.10 package
also depends on the 2.x API. Recheck primary documentation and consuming-package constraints before
resolving either exclusion. Do not force registry `latest` versions into unsupported combinations.

Routine minor/patch updates are outside #31 except where needed for compatibility or security.
The completion audit updates the transitive `@humanfs/node` package from 0.16.7 to 0.16.8 and its
required dependencies to resolve [GHSA-p498-v437-472g](https://github.com/advisories/GHSA-p498-v437-472g).
It introduces no direct dependency pin or override.

## SQLite packaging

The installed bundle's NuGet manifest resolves `SQLitePCLRaw.config.e_sqlite3` 3.0.5 and native
`SQLite` 3.53.4. The managed wrapper/configuration packages use Apache-2.0; the native package's
`LICENSE.txt` declares SQLite public domain. This uses the ordinary unencrypted build and does not
require a paid encryption build. Upstream explains the provider/bundle split in the
[SQLitePCLRaw documentation](https://github.com/ericsink/SQLitePCL.raw).

The application still uses EF migrations for actual schema changes. A native bundle upgrade alone
must preserve existing auth and per-user database files. Local repository tests exercise the provider;
stdio restart coverage checks existing files, and the disposable Docker smoke check verifies the
published native assets can initialize auth and planner storage.

The 2026-09-22 completion audit also ran a disposable-container compatibility experiment: synthetic
auth and planner databases (current schema and test data) were recreated through the native library
from `SQLitePCLRaw.lib.e_sqlite3` 2.1.12, then reopened by the v3 production image. Login, an existing
personal access token, and task reads survived; both files passed `PRAGMA integrity_check` and their
EF migration histories were unchanged. The running container loaded `libe_sqlite3.so`. This isolates
native-file compatibility; it is not a test of migrating every historical application schema.

## Repeat the audit

Use Node 24 before installing and verifying:

```sh
./scripts/setup.sh
./scripts/verify.sh --all
./scripts/docker-smoke-test.sh
npm --prefix OwnPlanner.Web/ownplanner.web.client audit
npm --prefix OwnPlanner.Web/ownplanner.web.client ls --all
npm --prefix OwnPlanner.Web/ownplanner.web.client outdated
```

`npm outdated` exits 1 when updates exist, including allowed minor/patch updates and the documented
major exclusions. `verify.sh --all` covers frontend, backend, and E2E; the separate Docker smoke
wrapper builds the image and creates/removes its own disposable container and volumes.

The solution-wide `dotnet list OwnPlanner.sln package` command can stop at the JavaScript `.esproj`
with a `packages.config` diagnostic, leaving later projects unaudited. Audit every C# project instead:

```sh
while IFS= read -r project; do
  dotnet list "$project" package --vulnerable --include-transitive --no-restore || exit 1
  dotnet list "$project" package --outdated --no-restore || exit 1
done < <(rg --files -g '*.csproj' | sort)
```

At the completion audit, every C# project has no reported vulnerable direct/transitive dependencies.
The only outstanding .NET direct major candidate is OpenAPI.NET 3. Frontend major candidates are
TypeScript 7 and Node 26 types, both excluded above. Package audits are time-sensitive: these results
are a dated baseline, not a guarantee about future advisories.
