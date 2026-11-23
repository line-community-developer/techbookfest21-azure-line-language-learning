using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Persistence;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;

namespace LineLanguageLearn.Learning.Workflows;

public sealed record WorkflowInstanceInput(UserContext User, string RunId);

public sealed record WorkflowStepInput(UserContext User, string RunId, string Step);

/// <summary>
/// Durable Functions hosting for workflow runs: the orchestrator calls each step as an activity.
/// Activities record step state in Cosmos DB as well, so the UI can show progress and offer retry.
/// The runner is resolved after reference data is loaded: activities can run on an instance that has
/// never handled an HTTP learning request, and the workflow steps depend on the reference data.
/// </summary>
public sealed class DurableWorkflowFunctions(IServiceProvider services, ReferenceDataLoader references)
{
    public const string Orchestrator = "WorkflowOrchestrator";
    private const string BeginActivity = "WorkflowBegin";
    private const string StepActivity = "WorkflowStep";
    private const string CompleteActivity = "WorkflowComplete";

    private async Task<WorkflowRunner> RunnerAsync(CancellationToken ct)
    {
        await references.LoadAsync(ct);
        return services.GetRequiredService<WorkflowRunner>();
    }

    [Function(Orchestrator)]
    public static async Task RunOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context, WorkflowInstanceInput input)
    {
        var steps = await context.CallActivityAsync<List<string>>(BeginActivity, input);
        foreach (var step in steps)
        {
            var result = await context.CallActivityAsync<WorkflowStepResult>(StepActivity, new WorkflowStepInput(input.User, input.RunId, step));
            if (result == WorkflowStepResult.Failed) return;
            if (result == WorkflowStepResult.Stop) break;
        }
        await context.CallActivityAsync(CompleteActivity, input);
    }

    [Function(BeginActivity)]
    public async Task<List<string>> BeginAsync([ActivityTrigger] WorkflowInstanceInput input, FunctionContext context) =>
        [.. await (await RunnerAsync(context.CancellationToken)).BeginAsync(input.User, input.RunId, context.CancellationToken)];

    [Function(StepActivity)]
    public async Task<WorkflowStepResult> StepAsync([ActivityTrigger] WorkflowStepInput input, FunctionContext context) =>
        await (await RunnerAsync(context.CancellationToken)).ExecuteStepAsync(input.User, input.RunId, input.Step, context.CancellationToken);

    [Function(CompleteActivity)]
    public async Task CompleteAsync([ActivityTrigger] WorkflowInstanceInput input, FunctionContext context) =>
        await (await RunnerAsync(context.CancellationToken)).CompleteAsync(input.User, input.RunId, context.CancellationToken);
}

/// <summary>
/// Starts a workflow run as a Durable orchestration. The client is only available through the
/// [DurableClient] binding, so HTTP functions create this scheduler per request.
/// </summary>
public sealed class DurableWorkflowScheduler(DurableTaskClient client, WorkflowRunner runner) : IWorkflowScheduler
{
    public const string ModeName = "durable-functions";

    public string Mode => ModeName;

    public static string InstanceId(UserContext user, string runId) => $"{user.UserId}:{runId}";

    public async Task<WorkflowRun> ScheduleAsync(UserContext user, string runId, CancellationToken ct)
    {
        var instanceId = InstanceId(user, runId);
        var existing = await client.GetInstanceAsync(instanceId, ct);
        if (existing is not { RuntimeStatus: OrchestrationRuntimeStatus.Pending or OrchestrationRuntimeStatus.Running })
        {
            await client.ScheduleNewOrchestrationInstanceAsync(
                DurableWorkflowFunctions.Orchestrator, new WorkflowInstanceInput(user, runId),
                new StartOrchestrationOptions(instanceId), ct);
        }
        return await runner.GetAsync(user, runId, ct);
    }
}
