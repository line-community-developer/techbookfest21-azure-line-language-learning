using LineLanguageLearn.Learning.Ai;
using LineLanguageLearn.Learning.Analysis;
using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.SeedData;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Practice;
using LineLanguageLearn.Learning.Tutor;
using LineLanguageLearn.Learning.Workflows;

namespace LineLanguageLearn.Tests;

public class AnalysisAndWorkflowTests
{
    private static async Task<TutorSession> ConversationAsync(TestApp app, bool end = false)
    {
        var tutor = app.Get<TutorService>();
        var session = await tutor.StartSessionAsync(TestApp.Learner, "週末", TutorChannel.LocalSimulator, default);
        for (var i = 0; i < ScenarioFixtureBuilder.DemoConversation.Length; i++)
        {
            await tutor.SendMessageAsync(TestApp.Learner, session.Id, $"e{i}", ScenarioFixtureBuilder.DemoConversation[i], default);
        }
        if (end) await tutor.EndSessionAsync(TestApp.Learner, session.Id, app.Get<IWorkflowScheduler>(), default);
        return (await app.Store.TutorSessions.GetAsync(TestApp.Learner.UserId, session.Id, default))!;
    }

    [Fact]
    public async Task Analysis_links_findings_to_learner_messages_and_rejects_unsupported_ones()
    {
        using var app = new TestApp();
        var session = await ConversationAsync(app);
        var analysis = app.Get<ConversationAnalysisService>();
        var tutorMessageId = session.Messages.First(m => m.Role == MessageRole.Tutor).Id;
        var learner = session.Messages.First(m => m.Role == MessageRole.Learner);

        var findings = new List<ConversationFinding>
        {
            new(FindingKind.GrammarError, "it-passato-prossimo-auxiliary", learner.Id, "ho andato", "sono andato/a", "x", 0.9),
            new(FindingKind.CorrectUse, "it-piacere", learner.Id, "mi piacciono", null, "never said", 0.9),
            new(FindingKind.CorrectUse, "it-andare-a", tutorMessageId, "Ciao", null, "tutor text", 0.9),
            new(FindingKind.GrammarError, "it-unknown", learner.Id, "ho andato", null, "x", 0.9),
            new(FindingKind.GrammarError, "it-passato-prossimo-auxiliary", learner.Id, "al mare", null, "x", 0.2),
            new(FindingKind.GrammarError, "it-passato-prossimo-auxiliary", "msg-999", "ho andato", null, "x", 0.9),
        };
        var accepted = analysis.ValidateFindings(session, findings, new ProviderInfo(ProviderKind.Mock, "t"), out var rejected);

        var observation = Assert.Single(accepted);
        Assert.Equal(learner.Id, observation.SourceItemId);
        Assert.Equal("ho andato", observation.Evidence.Text);
        Assert.Equal(ObservationCertainty.AiInferred, observation.Certainty);
        Assert.Equal(["evidence-not-in-message", "not-learner-message", "unknown-topic", "low-confidence", "unknown-message"], rejected.Select(r => r.Reason));
    }

    [Fact]
    public async Task Mock_analysis_output_matches_the_structured_output_contract()
    {
        using var app = new TestApp();
        var session = await ConversationAsync(app);
        var model = app.Get<ILearningModel>();
        var response = await model.AnalyzeConversationAsync(new ConversationAnalysisPrompt(session, [], TargetLanguage.It, CefrLevel.A2), default);
        var output = StructuredOutput.Parse<ConversationAnalysisOutput>(SchemaNames.ConversationAnalysis, response.Json);
        Assert.Contains(output.Findings, f => f.Kind == FindingKind.GrammarError && f.GrammarTopicId == "it-verb-di-infinitive");
        Assert.Contains(output.Findings, f => f.Kind == FindingKind.CorrectUse && f.GrammarTopicId == "it-avere-voglia-di");
        Assert.DoesNotContain(output.Findings, f => f.GrammarTopicId == "it-piacere");
    }

    [Fact]
    public async Task Ending_a_session_runs_the_workflow_to_a_validated_review_session()
    {
        using var app = new TestApp();
        var session = await ConversationAsync(app, end: true);

        var run = await app.Get<WorkflowRunner>().GetAsync(TestApp.Learner, session.AnalysisWorkflowRunId!, default);
        Assert.Equal(WorkflowStatus.Completed, run.Status);
        Assert.Equal(
            ["load-conversation", "extract-observations", "update-profile", "plan-review", "generate-questions", "validate-questions", "save-practice-session"],
            run.Steps.Select(s => s.Name));
        Assert.All(run.Steps, s => Assert.Equal(WorkflowStepStatus.Completed, s.Status));
        ContractSchemas.EnsureValid(SchemaNames.WorkflowRun, LearningJson.ToElement(run));

        var plan = Assert.Single(await app.Store.ReviewPlans.ListAsync(TestApp.Learner.UserId, default));
        Assert.Equal(session.Id, plan.SourceId);
        Assert.Equal(
            new[] { "it-passato-prossimo-auxiliary", "it-verb-a-infinitive", "it-verb-di-infinitive" }.Order(),
            plan.Items.Select(i => i.TopicId).Order());
        Assert.All(plan.Items, i => Assert.NotEmpty(i.EvidenceObservationIds));

        var review = (await app.Store.PracticeSessions.ListAsync(TestApp.Learner.UserId, default)).Single(s => s.Mode == PracticeMode.Review);
        Assert.Equal(plan.Id, review.ReviewPlanId);
        Assert.Contains(review.Id, plan.PracticeSessionIds);
        Assert.Equal(PracticeOriginKind.ReviewWorkflow, review.Origin.Kind);
        Assert.All(review.Questions, q => Assert.Equal(ValidationStatus.Passed, q.Validation.Status));
        Assert.All(review.Questions, q => Assert.Contains(q.GrammarTopicId, plan.Items.Select(i => i.TopicId)));

        var profile = await app.Get<LearningProfileService>().GetOrCreateAsync(TestApp.Learner, default);
        Assert.Contains("it-verb-a-infinitive", profile.MasteryByTopic.Keys);
    }

    [Fact]
    public async Task Repeated_end_does_not_duplicate_learning_data()
    {
        using var app = new TestApp();
        var session = await ConversationAsync(app, end: true);
        var before = (await app.Store.Observations.ListAsync(TestApp.Learner.UserId, default)).Count;

        var (_, run) = await app.Get<TutorService>().EndSessionAsync(TestApp.Learner, session.Id, app.Get<IWorkflowScheduler>(), default);
        await app.Get<WorkflowRunner>().RunAsync(TestApp.Learner, run.Id, default);

        Assert.Equal(before, (await app.Store.Observations.ListAsync(TestApp.Learner.UserId, default)).Count);
        Assert.Single(await app.Store.ReviewPlans.ListAsync(TestApp.Learner.UserId, default));
        Assert.Single(await app.Store.PracticeSessions.ListAsync(TestApp.Learner.UserId, default), s => s.Mode == PracticeMode.Review);
        Assert.Equal(1, run.RunCount);
        // Tool-recorded observations and workflow-extracted observations share ids, so nothing was double counted.
        var profile = await app.Get<LearningProfileService>().GetOrCreateAsync(TestApp.Learner, default);
        Assert.Equal(before, profile.ObservationCount);
    }

    [Fact]
    public async Task Failed_step_is_recorded_and_retry_resumes_from_it()
    {
        using var app = new TestApp(TestApp.WithScriptedModel);
        app.Get<ScriptedModel>().AnalyzeFailuresRemaining = 1;
        var session = await ConversationAsync(app, end: true);
        var runner = app.Get<WorkflowRunner>();

        var failed = await runner.GetAsync(TestApp.Learner, session.AnalysisWorkflowRunId!, default);
        Assert.Equal(WorkflowStatus.Failed, failed.Status);
        var failedStep = failed.Steps.Single(s => s.Status == WorkflowStepStatus.Failed);
        Assert.Equal("extract-observations", failedStep.Name);
        Assert.Equal("step-error", failedStep.Error!.Code);
        Assert.Equal(WorkflowStepStatus.Completed, failed.Steps[0].Status);
        Assert.Empty(await app.Store.ReviewPlans.ListAsync(TestApp.Learner.UserId, default));

        var retried = await runner.RetryAsync(TestApp.Learner, failed.Id, app.Get<IWorkflowScheduler>(), default);

        Assert.Equal(WorkflowStatus.Completed, retried.Status);
        Assert.Equal(2, retried.RunCount);
        Assert.Equal(1, retried.Steps[0].Attempts);
        Assert.Equal(2, retried.Steps[1].Attempts);
        Assert.Equal(2, app.Get<ScriptedModel>().AnalyzeCalls);
        Assert.Single(await app.Store.ReviewPlans.ListAsync(TestApp.Learner.UserId, default));
    }

    [Fact]
    public async Task Durable_activity_units_stop_on_failure_and_resume_from_the_failed_step()
    {
        using var app = new TestApp(TestApp.WithScriptedModel);
        app.Get<ScriptedModel>().AnalyzeFailuresRemaining = 1;
        var session = await ConversationAsync(app);
        var tutor = app.Get<TutorService>();
        var runner = app.Get<WorkflowRunner>();
        var pending = new PendingScheduler(runner);

        var (_, created) = await tutor.EndSessionAsync(TestApp.Learner, session.Id, pending, default);
        Assert.Equal(WorkflowStatus.Pending, created.Status);
        Assert.Equal(1, pending.Scheduled);

        // Same sequence as WorkflowOrchestrator: Begin → Step... (stop on Failed) → Complete.
        async Task<WorkflowStepResult?> OrchestrateAsync()
        {
            foreach (var step in await runner.BeginAsync(TestApp.Learner, created.Id, default))
            {
                var result = await runner.ExecuteStepAsync(TestApp.Learner, created.Id, step, default);
                if (result == WorkflowStepResult.Failed) return result;
                if (result == WorkflowStepResult.Stop) break;
            }
            await runner.CompleteAsync(TestApp.Learner, created.Id, default);
            return null;
        }

        Assert.Equal(WorkflowStepResult.Failed, await OrchestrateAsync());
        var failed = await runner.GetAsync(TestApp.Learner, created.Id, default);
        Assert.Equal(WorkflowStatus.Failed, failed.Status);
        Assert.Equal("extract-observations", failed.Steps.Single(s => s.Status == WorkflowStepStatus.Failed).Name);

        Assert.Null(await OrchestrateAsync());
        var completed = await runner.GetAsync(TestApp.Learner, created.Id, default);
        Assert.Equal(WorkflowStatus.Completed, completed.Status);
        Assert.Equal(2, completed.RunCount);
        Assert.Equal(1, completed.Steps[0].Attempts);
        Assert.Equal(2, completed.Steps[1].Attempts);
        Assert.Empty(await runner.BeginAsync(TestApp.Learner, created.Id, default));
        Assert.Single(await app.Store.ReviewPlans.ListAsync(TestApp.Learner.UserId, default));
    }

    /// <summary>Records scheduling without running, like the Durable scheduler returning before the orchestration starts.</summary>
    private sealed class PendingScheduler(WorkflowRunner runner) : IWorkflowScheduler
    {
        public int Scheduled { get; private set; }
        public string Mode => "test-pending";

        public Task<WorkflowRun> ScheduleAsync(LineLanguageLearn.Learning.Identity.UserContext user, string runId, CancellationToken ct)
        {
            Scheduled++;
            return runner.GetAsync(user, runId, ct);
        }
    }

    [Fact]
    public async Task Invalid_generated_questions_are_never_saved()
    {
        using var app = new TestApp(TestApp.WithScriptedModel);
        app.Get<ScriptedModel>().QuestionOverride = prompt => LearningJson.Serialize(new GeneratedQuestionBatch(prompt.TopicIds.Select(t => new GeneratedQuestion(
            QuestionType.MultipleChoice, t, 2, "x", "Ho ___ fame.", ["fatto", "fatto", "avuto"], "sono", ["sono"], "spiegazione", [$"{t}-r1"], [t])).ToList()));
        var session = await ConversationAsync(app, end: true);

        var run = await app.Get<WorkflowRunner>().GetAsync(TestApp.Learner, session.AnalysisWorkflowRunId!, default);
        Assert.Equal(WorkflowStatus.Failed, run.Status);
        Assert.Equal("validate-questions", run.Steps.Single(s => s.Status == WorkflowStepStatus.Failed).Name);
        Assert.Equal("question-validation-failed", run.Error!.Code);
        Assert.DoesNotContain(await app.Store.PracticeSessions.ListAsync(TestApp.Learner.UserId, default), s => s.Mode == PracticeMode.Review);
    }

    [Fact]
    public void Review_planner_prioritises_conversation_errors()
    {
        var planner = new ReviewPlanner(ReferenceDataFixtures.Load().Grammar);
        var profile = new LearningProfile { UserId = "u", WeakTopics = ["it-andare-in"] };
        profile.MasteryByTopic["it-andare-in"] = new TopicMastery { TopicId = "it-andare-in", Score = 0.3 };
        profile.MasteryByTopic["it-verb-di-infinitive"] = new TopicMastery { TopicId = "it-verb-di-infinitive", Score = 0.4 };
        var error = new LearningObservation
        {
            Id = "o1", UserId = "u", GrammarTopicId = "it-verb-di-infinitive", ObservationType = ObservationType.GrammarError,
            Evidence = new ObservationEvidence("finito a lavorare", "finito di lavorare", "finito a lavorare"),
        };
        var items = planner.PlanItems(profile, [error, LearningJson.Clone(error) is var e2 ? WithId(e2, "o2") : error], [error]);

        Assert.Equal("it-verb-di-infinitive", items[0].TopicId);
        Assert.Equal(ReviewPriority.High, items[0].Priority);
        Assert.Contains("finito di lavorare", items[0].ReasonJa);
        Assert.Contains(items, i => i.TopicId == "it-andare-in");
    }

    private static LearningObservation WithId(LearningObservation o, string id)
    {
        o.Id = id;
        return o;
    }
}
