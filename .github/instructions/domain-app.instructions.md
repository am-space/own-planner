---
applyTo: "OwnPlanner.Domain/**/*.cs,OwnPlanner.Application/**/*.cs"
---

# Domain and Application layer Instructions

## Coding rules

- Add comments to explain the intent and reasoning behind Domain entities and Application services.
- Add tests for critical domain logic and application use-cases, especially edge cases and failure modes.
- Preserve existing patterns for entities, value objects, aggregates, repositories, services, and DTOs.
- Use EntityBase class as a base class for entities with an Id.