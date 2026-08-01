---
name: develop-ownplanner-feature
description: Plan, review, implement, and verify an OwnPlanner feature or use case from a GitHub issue, issue URL, repository plan, or direct feature request. Use for new product behavior and cross-layer enhancements that may affect Domain, Application, Infrastructure, Web API, MCP, console/stdio, React frontend, tests, migrations, ADRs, or reference documentation. Also use when asked to assess an OwnPlanner feature issue and turn it into an implementation-ready plan; do not use for documentation-only maintenance, pure refactoring, or a tiny isolated fix unless the user explicitly invokes this skill.
---

# Develop an OwnPlanner Feature

Execute an issue-driven feature as a traceable sequence: intake, plan, plan review, implementation, verification, and handoff. Touch only the layers required by the behavior.

## Treat the Issue as the Contract

- Treat the repository issue as the canonical record of the outcome, acceptance criteria, discussion, and implementation history.
- Treat GitHub Project fields such as Status, Priority, Size, and Iteration as planning context, not as a duplicate feature specification.
- Follow parent issues, sub-issues, dependencies, linked plans, ADRs, and prior pull requests when they affect scope.
- Use a Project draft only for discovery. Ask the user to convert it to a repository issue before creating a branch or pull request that must close it.
- If given a direct request instead of an issue, form a temporary feature contract from the request. Do not create an issue or modify GitHub state unless the user authorizes it.

## Follow the Required Workflow

### 1. Read the Issue and Repository State

1. Read the root and applicable scoped `AGENTS.md` files.
2. Inspect the worktree, current branch, and relationship to `master`. Preserve unrelated user changes.
3. Read the issue, including comments and relationships. Prefer `gh issue view <number-or-url> --comments` when GitHub CLI access is available.
4. Extract:
   - user-visible outcome;
   - acceptance criteria and edge cases;
   - explicit exclusions;
   - priority and planning context;
   - dependencies and blockers;
   - unresolved product or technical decisions.
5. Decide whether the issue is ready. Record harmless assumptions. Pause for user direction only when ambiguity would materially change behavior, public contracts, stored data, security, or scope.

Do not silently edit the issue, Project fields, relationships, or comments. External planning changes require authorization.

### 2. Explore Existing Patterns

1. Find the closest implemented feature and its tests.
2. Inspect relevant reference docs and ADRs before proposing a new abstraction.
3. Reuse existing naming, folders, DTOs, errors, authentication, tenant resolution, and test patterns.
4. Read [references/feature-paths.md](references/feature-paths.md) for every potentially affected delivery or persistence path.

### 3. Create the Implementation Plan

Create or update the active task plan before editing feature code. Make every step independently verifiable and include:

- a one-sentence intended outcome;
- an acceptance-criteria-to-change/test mapping;
- an impact map marking Domain, Application, Infrastructure, Web API, MCP, console/stdio, frontend, tests, and documentation as affected or not applicable;
- the implementation order and closest existing pattern;
- migrations or compatibility work;
- focused and full verification;
- exclusions, risks, and assumptions.

Do not force every feature through every layer. Prefer the smallest complete vertical slice.

### 4. Review the Plan Before Implementation

Perform a distinct review pass and revise the plan until all applicable checks pass:

- Every acceptance criterion maps to behavior and test evidence.
- The plan addresses the underlying use case rather than only a transport or UI symptom.
- Clean Architecture dependencies continue to flow inward.
- Business behavior remains in Domain or Application, not controllers, MCP handlers, or React components.
- Per-user data isolation and authenticated tenant resolution remain intact.
- HTTP, MCP, database, and frontend contracts remain compatible or call out intentional changes.
- EF Core migrations are CLI-generated for the correct context and are never handwritten.
- Sensitive data, credentials, prompts, and user content are not exposed through logs or errors.
- Tests and documentation are proportional to the change.
- The plan contains no unrelated cleanup.

Share the reviewed plan concisely while working. Continue without waiting for approval when the issue is clear and the actions are already authorized; request direction for material product choices or expanded scope.

Do not begin feature implementation until this review is complete.

### 5. Implement the Reviewed Plan

1. Work on `feature/<short-description>` for enhancements or `fix/<short-description>` for bugs. Never commit feature work directly to `master`.
2. Implement in dependency order where applicable: Domain, Application, Infrastructure, transports, frontend, documentation.
3. Add or update tests alongside each affected layer.
4. Keep transport adapters thin and reuse Application behavior across HTTP, MCP, console, and stdio paths.
5. Generate required migrations with `dotnet ef migrations add ... --context <context>`.
6. Preserve nullability, async contracts, structured logging, and existing public behavior unless the issue requires a change.
7. Update plan status as work progresses and revise the plan when discovery changes the implementation.

### 6. Verify the Feature

Read [references/validation-matrix.md](references/validation-matrix.md), run focused checks during development, then run the full repository verification before delivery:

```sh
./scripts/verify.sh --all
```

Run `./scripts/setup.sh` first when dependencies changed or the workspace is not prepared. If a check cannot run, explain the exact limitation and retain it as incomplete rather than treating it as passed.

### 7. Review the Completed Diff

Before handoff:

1. Compare the behavior and tests with every acceptance criterion.
2. Inspect the complete diff for accidental files, unrelated cleanup, generated artifacts, secrets, and contract changes.
3. Confirm relevant docs describe the shipped behavior rather than the original plan.
4. Create or update an ADR for a durable architectural decision.
5. Archive a completed `docs/*-plan.md` according to `docs/AGENTS.md`; never delete the historical plan.
6. Re-run affected verification after review fixes.

### 8. Hand Off Through the Issue Lifecycle

Report:

- the implemented behavior;
- acceptance-criteria evidence;
- validation commands and results;
- migrations, contract changes, warnings, risks, and remaining work;
- branch, commit, push, and pull-request state.

Commit, push, create a pull request, or change GitHub Project state only when authorized. When creating the pull request, link the canonical issue with `Closes #<number>` so merge closes it automatically. Prefer Project automation for moving closed work to Done or archiving it; do not manually close the issue before the implementation is merged.

## Definition of Done

Consider the feature complete only when the reviewed issue scope is implemented, affected tests and full verification pass, documentation reflects the resulting behavior, the final diff is clean and scoped, and any unverified or deferred work is explicitly reported.
