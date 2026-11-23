# Workflow: Modify an existing model

Goal: change a schema and re-align every generated artifact.

## Steps

1. Edit `shared/models/<name>.ts` (free-form authoring).
2. Check what drifted:
   `pnpm exec swallowkit machine inspect drift`
   - `schema-drift` findings list the models whose artifacts are stale.
3. Plan the regeneration:
   `pnpm exec swallowkit machine plan scaffold <name>`
   - Conflicts mean generated files were hand-edited after generation. Decide: keep the hand-edit (move the logic to an ai-authored file first) or overwrite with `--approve`.
4. Apply: `pnpm exec swallowkit machine apply scaffold --plan <planId>`
5. Verify: `pnpm exec swallowkit machine verify project`

## Guardrail

Never hand-edit managed artifacts to implement schema changes — always change the model
and regenerate. Hand-edits are detected as drift and block unattended overwrites.
