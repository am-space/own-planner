# Web Client Guidance

This directory is the React 19, TypeScript, Vite, React Router, and MUI client.

## Implementation

- Extend existing components, contexts, hooks, `src/services/api.ts`, and shared API types before
  introducing a new pattern or dependency. Do not add a state-management library unless requested.
- Keep TypeScript request/response types aligned with server DTOs, including optionality, error
  handling, and authentication semantics. Do not expose secrets or retain sensitive account data in
  browser storage or diagnostic logs.
- Follow the local MUI composition, routing, responsive-layout, and theme patterns. Preserve loading,
  empty, error, keyboard, and accessibility behavior in touched flows.

## Validation

Run from this directory:

```sh
npm run lint
npm run build
```

There is no frontend test command currently. For behavior changes, also exercise the affected flow
against the server when practical, especially authentication and API contract changes.
