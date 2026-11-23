# Workflow: Verify and repair

Goal: converge the project to a verified state after any change.

## Loop

1. `pnpm exec swallowkit machine verify project`
   - default checks: structure, drift, typecheck, backend-build (+ project-specific checks from swallowkit.config `verify.checks`)
   - backend-build: standalone C# `dotnet build`, TypeScript `tsc --noEmit`, Python 3 `compileall` (syntax only).
     Missing toolchains are skipped with a reason, not proof of a working backend. Restore them and re-run.
   - opt-in heavier checks: `--checks "structure,drift,typecheck,backend-build,build,lint,test"` (quote CSV in PowerShell)
2. If `summary.done` is false:
   - `pnpm exec swallowkit machine explain failure` → evidence (logTail, findings, violations) and suggestedActions per check.
   - Repair:
     - `drift` failures → follow each finding's `repairAction` (usually re-run plan/apply scaffold).
     - `structure` failures → follow `suggestedFix` per violation.
     - `typecheck`/`backend-build`/`build`/`lint`/`test` failures → fix the source code, not generated artifacts.
3. Repeat until `summary.done: true`.

## Exit criteria

Stop and ask a human when:
- the same check fails 3 times in a row with the same evidence, or
- a repair would require overwriting hand-edited files (`requires-human`).
