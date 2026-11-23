---
name: swallowkit-provision
description: Provision Azure resources (Static Web Apps, Functions, Cosmos DB) for a SwallowKit project using the two-phase plan/apply flow with a mandatory human approval gate. Use when asked to deploy infrastructure, provision Azure, create resource groups, or run Bicep deployments.
metadata:
  generator: swallowkit
---

# Workflow: Provision Azure resources

Goal: bootstrap Azure once, then apply only pending versioned infrastructure migrations with an explicit human approval gate.

## Rules

- Provisioning **always** requires human approval — `plan provision` returns
  `status: "requires-human"` and `requiresApproval: true` by design.
- Never pass `--approve` to `apply provision` unless a human explicitly
  instructed you to provision in this session.
- Inspect infra locally first — no `az` calls are needed for inspection.
- Never edit an applied migration. Create a corrective migration instead.
- Never use `--reconcile` for an ordinary model or resource addition.

## Steps

1. Inspect the infrastructure definition (pure local Bicep analysis):
   `pnpm exec swallowkit machine inspect infra`
   - review parameters, modules, outputs, and container wiring warnings.
2. Compute the provisioning plan (no resources are created):
   `pnpm exec swallowkit machine plan provision -g <resource-group> --location <region>`
  - the plan reports `bootstrap`, `migrate`, `noop`, or `adoption-required`.
  - it lists only pending migrations and the exact `az` commands that would run.
  - for an older untagged environment, first run `pnpm exec swallowkit migrations init --legacy-baseline`, then plan with `--adopt-existing --baseline 0 --what-if`.
  - only intentional baseline changes use `--reconcile --what-if`; this can reset template-managed settings.
3. Present the plan to the human and wait for explicit approval.
4. Only after approval:
   `pnpm exec swallowkit machine apply provision --plan <planId> --approve`
5. Verify the deployment outputs and report them.

## Terminal states

`apply provision` without `--approve` fails with `approval-required` —
this is the guardrail working as intended, not an error to work around.
