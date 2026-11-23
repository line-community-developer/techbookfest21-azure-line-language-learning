# Workflow: Add a new model

Goal: add a data model and derive CRUD/BFF/UI/infra deterministically.

## Responsibility boundary

- **You author freely**: the model schema (`shared/models/<name>.ts`) and any business logic.
- **SwallowKit generates deterministically**: Functions CRUD, BFF routes, UI pages, OpenAPI, Bicep containers.
- Query the full contract: `pnpm exec swallowkit machine inspect boundaries`

## Steps

1. Create the model file (deterministic template):
   `pnpm exec swallowkit machine generate model <name> --overwrite never`
2. Edit `shared/models/<name>.ts` — add domain fields (free-form authoring).
   - Native C#/Python types follow Zod, not names: `z.iso.datetime()` for dates, `z.number().int()` for integers.
   - `<model>Id` fields reference a model only when it exists in `shared/models`; otherwise declare
     `export const references = { fieldId: 'Model' }` (or `false` to opt out).
3. Compute the change plan (writes nothing):
   `pnpm exec swallowkit machine plan scaffold <name>`
   - `status: "requires-human"` → review `data.conflicts`; only proceed with `--approve` if overwriting is intended.
   - Must not change Bicep/infrastructure? Add `--skip-infra` to plan and apply. Nothing under `infra/` changes and
     the plan warns `infra-skipped`; later run `pnpm exec swallowkit migrations add-model <name>` to add only the container migration.
   - Read `data.warnings`. `user-scoped-data-without-auth` means generated CRUD would expose every user's documents;
     add auth (`swallowkit-add-auth`) and owner-scoped ai-authored endpoints before exposing the model.
   - `auth-policy-without-auth` blocks generation before file writes: configure authentication with plan/apply auth,
     then re-plan. Do not remove a required policy to bypass this prerequisite.
     For named `auth.schemes`, define named authorization policies selecting schemes/roles and reference them from
     `authPolicy.policy` or read/write names; role-only declarations block with `auth-policy-requires-named-policy`.
     Single-provider `auth.provider` supports role lists only; named policies block with `auth-named-policy-requires-named-scheme`.
   - Scaffold appends missing exports to `shared/index.ts` for hand-authored models too.
     Manual exports are allowed; no managed markers exist in that file.
4. Apply:
   `pnpm exec swallowkit machine apply scaffold --plan <planId>`
5. Verify:
   `pnpm exec swallowkit machine verify project`
   - `summary.done: false` → `pnpm exec swallowkit machine explain failure` and repair, then re-verify.
6. If `dev-seeds/` exists, update seed JSON for the new model. Seeds are only
   applied by `pnpm exec swallowkit dev --seed-env <environment>` — editing the
   JSON alone does nothing.

## Terminal states

| status | meaning | next |
|--------|---------|------|
| complete | operation finished | continue |
| in-progress | more work remains (e.g. failing checks) | follow nextActions |
| blocked | prerequisite missing (e.g. stale plan) | re-plan |
| requires-human | approval needed | ask the human, or pass --approve when instructed |
| failed | operation failed | inspect error.details |
