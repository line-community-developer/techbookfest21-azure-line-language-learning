using System.Collections.Concurrent;
using System.Diagnostics;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Observability;
using LineLanguageLearn.Learning.Persistence;
using Microsoft.Extensions.Logging;

namespace LineLanguageLearn.Learning.Workflows;

public sealed class WorkflowStepContext(WorkflowRun run, UserContext user)
{
    public WorkflowRun Run { get; } = run;
    public UserContext User { get; } = user;
    public string SubjectId => Run.SubjectId;

    public string Output(string stepName, string key) =>
        Run.Steps.First(s => s.Name == stepName).Output.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"Workflow step '{stepName}' has no output '{key}'.");
}

public sealed record StepOutcome(Dictionary<string, string> Output, bool SkipRemaining = false);

/// <summary>One idempotent unit of work. Steps read earlier outputs from the run, so a retry resumes safely.</summary>
public interface IWorkflowStep
{
    string Name { get; }
    Task<StepOutcome> ExecuteAsync(WorkflowStepContext context, CancellationToken ct);
}

/// <summary>Workflow body (ordered steps) independent of hosting. A Durable orchestrator can call the same steps as activities.</summary>
public interface IWorkflowDefinition
{
    string Name { get; }
    IReadOnlyList<IWorkflowStep> Steps { get; }
}

/// <summary>Hosting boundary. Durable Functions in the Function App; in-process for tests and offline seed generation.</summary>
public interface IWorkflowScheduler
{
    string Mode { get; }
    Task<WorkflowRun> ScheduleAsync(UserContext user, string runId, CancellationToken ct);
}

/// <summary>Result of one step, telling the host whether to continue.</summary>
public enum WorkflowStepResult { Continue, Stop, Failed }

/// <summary>
/// Persists run/step state (Pending → Running → Completed | Failed) after every transition, so failures are
/// inspectable in the UI and a retry resumes from the failed step without repeating completed ones.
/// Begin / ExecuteStep / Complete are the units a Durable orchestrator calls as activities.
/// </summary>
public sealed class WorkflowRunner(LearningStore store, IEnumerable<IWorkflowDefinition> definitions, TimeProvider clock, ILogger<WorkflowRunner> logger)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private readonly Dictionary<string, IWorkflowDefinition> _definitions = definitions.ToDictionary(d => d.Name);

    public static string RunId(string workflowName, string subjectId) => $"wf-{workflowName}-{subjectId}";

    public async Task<WorkflowRun> GetOrCreateAsync(UserContext user, string workflowName, string subjectId, CancellationToken ct)
    {
        var definition = _definitions[workflowName];
        var id = RunId(workflowName, subjectId);
        var run = new WorkflowRun
        {
            Id = id,
            UserId = user.UserId,
            WorkflowName = workflowName,
            SubjectId = subjectId,
            Status = WorkflowStatus.Pending,
            Steps = definition.Steps.Select(s => new WorkflowStepState { Name = s.Name, Status = WorkflowStepStatus.Pending }).ToList(),
        };
        await store.WorkflowRuns.TryCreateAsync(run, ct);
        return (await store.WorkflowRuns.GetAsync(user.UserId, id, ct))!;
    }

    public Task<WorkflowRun> GetAsync(UserContext user, string runId, CancellationToken ct) =>
        store.WorkflowRuns.GetRequiredAsync(user.UserId, runId, "Workflow run", ct);

    public async Task<WorkflowRun> RunAsync(UserContext user, string runId, CancellationToken ct)
    {
        var gate = Locks.GetOrAdd($"{user.UserId}/{runId}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            foreach (var step in await BeginAsync(user, runId, ct))
            {
                var result = await ExecuteStepAsync(user, runId, step, ct);
                if (result == WorkflowStepResult.Failed) return await GetAsync(user, runId, ct);
                if (result == WorkflowStepResult.Stop) break;
            }
            return await CompleteAsync(user, runId, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Marks the run as running and returns the steps still to execute (empty when already completed).</summary>
    public async Task<IReadOnlyList<string>> BeginAsync(UserContext user, string runId, CancellationToken ct)
    {
        var run = await GetAsync(user, runId, ct);
        if (run.Status == WorkflowStatus.Completed) return [];
        run.Status = WorkflowStatus.Running;
        run.RunCount++;
        run.Error = null;
        await store.WorkflowRuns.UpsertAsync(run, ct);
        return run.Steps
            .Where(s => s.Status is not (WorkflowStepStatus.Completed or WorkflowStepStatus.Skipped))
            .Select(s => s.Name)
            .ToList();
    }

    /// <summary>Executes one step and persists its state. Step failures are recorded, not thrown.</summary>
    public async Task<WorkflowStepResult> ExecuteStepAsync(UserContext user, string runId, string stepName, CancellationToken ct)
    {
        var run = await GetAsync(user, runId, ct);
        var step = _definitions[run.WorkflowName].Steps.First(s => s.Name == stepName);
        var state = run.Steps.First(s => s.Name == step.Name);
        if (state.Status is WorkflowStepStatus.Completed or WorkflowStepStatus.Skipped) return WorkflowStepResult.Continue;

        state.Status = WorkflowStepStatus.Running;
        state.Attempts++;
        state.StartedAt = clock.GetUtcNow().ToString("O");
        state.Error = null;
        await store.WorkflowRuns.UpsertAsync(run, ct);

        var started = Stopwatch.GetTimestamp();
        using var activity = LearningTelemetry.StartActivity($"workflow.{run.WorkflowName}.{step.Name}");
        try
        {
            var outcome = await step.ExecuteAsync(new WorkflowStepContext(run, user), ct);
            state.Output = outcome.Output;
            state.Status = WorkflowStepStatus.Completed;
            state.CompletedAt = clock.GetUtcNow().ToString("O");
            logger.WorkflowStep(run.WorkflowName, run.Id, step.Name, "completed", state.Attempts, Stopwatch.GetElapsedTime(started).TotalMilliseconds, null);
            if (outcome.SkipRemaining)
            {
                foreach (var remaining in run.Steps.Where(s => s.Status == WorkflowStepStatus.Pending)) remaining.Status = WorkflowStepStatus.Skipped;
            }
            await store.WorkflowRuns.UpsertAsync(run, ct);
            return outcome.SkipRemaining ? WorkflowStepResult.Stop : WorkflowStepResult.Continue;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var error = ex is DomainException domain
                ? new WorkflowError(domain.Code, domain.Message)
                : new WorkflowError("step-error", $"{ex.GetType().Name} in step {step.Name}");
            state.Status = WorkflowStepStatus.Failed;
            state.Error = error;
            run.Status = WorkflowStatus.Failed;
            run.Error = error;
            await store.WorkflowRuns.UpsertAsync(run, ct);
            logger.WorkflowStep(run.WorkflowName, run.Id, step.Name, "failed", state.Attempts, Stopwatch.GetElapsedTime(started).TotalMilliseconds, error.Code);
            if (ex is not DomainException) logger.LogError(ex, "Workflow step {Step} failed for run {RunId}", step.Name, run.Id);
            return WorkflowStepResult.Failed;
        }
    }

    public async Task<WorkflowRun> CompleteAsync(UserContext user, string runId, CancellationToken ct)
    {
        var run = await GetAsync(user, runId, ct);
        if (run.Status == WorkflowStatus.Completed) return run;
        run.Status = WorkflowStatus.Completed;
        await store.WorkflowRuns.UpsertAsync(run, ct);
        return run;
    }

    public async Task<WorkflowRun> RetryAsync(UserContext user, string runId, IWorkflowScheduler scheduler, CancellationToken ct)
    {
        var run = await GetAsync(user, runId, ct);
        return run.Status is WorkflowStatus.Failed or WorkflowStatus.Pending ? await scheduler.ScheduleAsync(user, runId, ct) : run;
    }
}

/// <summary>Executes the workflow synchronously in the calling process. Used by tests and offline seed generation.</summary>
public sealed class InProcessWorkflowScheduler(WorkflowRunner runner) : IWorkflowScheduler
{
    public string Mode => "in-process";

    public Task<WorkflowRun> ScheduleAsync(UserContext user, string runId, CancellationToken ct) => runner.RunAsync(user, runId, ct);
}
