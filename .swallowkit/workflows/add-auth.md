# Workflow: Add authentication

Goal: introduce an auth scheme deterministically and finish the manual follow-ups.

## Config ownership (critical)

- `auth.schemes` is **owned by plan/apply auth. Never hand-write scheme entries** —
  a pre-written scheme makes planning fail with `already exists`.
- `auth.authorization.policies` and `swa.allowedProviders` are **hand-edited after
  apply** (the config is an extension point; this is the expected workflow).

## Steps

1. Check what already exists: `pnpm exec swallowkit machine inspect capabilities`
   (authentication section) and the current `auth` block in swallowkit.config.
2. Plan (writes nothing):
   `pnpm exec swallowkit machine plan auth --provider <custom-jwt|swa|external-token> [--scheme <name>] [--allowed-providers github,aad]`
3. Apply:
   `pnpm exec swallowkit machine apply auth --plan <planId>`
4. Follow the returned `nextActions`:
   - Define `auth.authorization.policies` referencing the scheme (hand-edit).
   - swa: confirm `swa.allowedProviders` — generated login URLs use the first entry.
   - external-token: implement the generated verifier stub. It fails closed and
     **verify passes with the stub**, so a green verify does not mean the auth
     flow works (a `auth-verifier-stub` warning is surfaced while unmodified).
5. Add `authPolicy` to models that need guards, then re-run plan/apply scaffold
   for them.
6. Verify: `pnpm exec swallowkit machine verify project`

## Scope guardrail

Generated CRUD enforces authentication and roles but does **not** scope rows to
the caller. Owner-scoped behavior belongs in ai-authored endpoints that derive
the scoping key from the verified principal, never from the request body.
