using LineLanguageLearn.Learning.Ai;
using LineLanguageLearn.Learning.Analysis;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Persistence;
using LineLanguageLearn.Learning.Practice;
using Microsoft.Extensions.Options;

namespace LineLanguageLearn.Learning.Workflows;

/// <summary>
/// After a tutor session ends: load conversation → extract observations → update profile → plan review →
/// generate questions → validate questions → save practice session. Every step is idempotent
/// (deterministic ids, create-if-absent), so replays and retries never duplicate data.
/// </summary>
public sealed class PostConversationWorkflow : IWorkflowDefinition
{
    public const string WorkflowName = "post-conversation-analysis";
    public const int ReviewQuestionCount = 5;
    public const int MinimumValidQuestions = 3;

    public PostConversationWorkflow(
        LearningStore store,
        LearningProfileService profiles,
        ConversationAnalysisService analysis,
        ReviewPlanner planner,
        QuestionGenerationService generator,
        PracticeService practice,
        IOptions<LearningOptions> options)
    {
        Steps =
        [
            new Step("load-conversation", async (ctx, ct) =>
            {
                var session = await store.TutorSessions.GetRequiredAsync(ctx.User.UserId, ctx.SubjectId, "Tutor session", ct);
                if (session.Status != TutorSessionStatus.Ended) throw DomainException.Conflict("session-not-ended", "The tutor session must be ended before analysis.");
                return Out(("messages", session.Messages.Count.ToString()), ("learnerMessages", session.Messages.Count(m => m.Role == MessageRole.Learner).ToString()));
            }),
            new Step("extract-observations", async (ctx, ct) =>
            {
                var session = await store.TutorSessions.GetRequiredAsync(ctx.User.UserId, ctx.SubjectId, "Tutor session", ct);
                var profile = await profiles.GetOrCreateAsync(ctx.User, ct);
                var result = await analysis.AnalyzeAsync(session, profile, ct);
                return Out(
                    ("accepted", result.Observations.Count.ToString()),
                    ("rejected", result.Rejected.Count.ToString()),
                    ("provider", result.Provider.Label),
                    ("summaryJa", result.SummaryJa),
                    ("observations", LearningJson.Serialize(result.Observations)));
            }),
            new Step("update-profile", async (ctx, ct) =>
            {
                var observations = LearningJson.Deserialize<List<LearningObservation>>(ctx.Output("extract-observations", "observations"));
                var write = await profiles.RecordObservationsAsync(ctx.User.UserId, observations, ct);
                return Out(("created", write.CreatedIds.Count.ToString()), ("duplicates", write.DuplicateIds.Count.ToString()), ("weakTopics", string.Join(',', write.Profile.WeakTopics)));
            }),
            new Step("plan-review", async (ctx, ct) =>
            {
                var profile = await profiles.GetOrCreateAsync(ctx.User, ct);
                var all = await profiles.ListObservationsAsync(ctx.User.UserId, ct);
                // Includes observations recorded live by the tutor's tool calls for this session.
                var fromSession = all.Where(o => o.SourceType == ObservationSource.Conversation && o.SourceId == ctx.SubjectId).ToList();
                var items = planner.PlanItems(profile, fromSession, all);
                if (items.Count == 0) return new StepOutcome(new() { ["reviewPlanId"] = "" }, SkipRemaining: true);

                var plan = new ReviewPlan
                {
                    Id = ReviewPlanner.PlanIdForConversation(ctx.SubjectId),
                    UserId = ctx.User.UserId,
                    SourceType = ObservationSource.Conversation,
                    SourceId = ctx.SubjectId,
                    Status = ReviewPlanStatus.Active,
                    Items = items,
                };
                await store.ReviewPlans.TryCreateAsync(plan, ct);
                var stored = (await store.ReviewPlans.GetAsync(ctx.User.UserId, plan.Id, ct))!;
                return Out(("reviewPlanId", stored.Id), ("topicIds", string.Join(',', stored.Items.Select(i => i.TopicId))));
            }),
            new Step("generate-questions", async (ctx, ct) =>
            {
                var profile = await profiles.GetOrCreateAsync(ctx.User, ct);
                var spec = ReviewSpec(profile, ctx);
                var batch = await generator.GenerateCandidatesAsync(profile, spec, ReviewQuestionCount, [], 0, ct);
                return Out(("generationId", batch.GenerationId), ("provider", LearningJson.Serialize(batch.Provider)), ("candidates", LearningJson.Serialize(batch.Questions)));
            }),
            new Step("validate-questions", async (ctx, ct) =>
            {
                var profile = await profiles.GetOrCreateAsync(ctx.User, ct);
                var spec = ReviewSpec(profile, ctx);
                var candidates = LearningJson.Deserialize<List<GeneratedQuestion>>(ctx.Output("generate-questions", "candidates"));
                var provider = LearningJson.Deserialize<ProviderInfo>(ctx.Output("generate-questions", "provider"));
                var context = await generator.RetrieveContextAsync(spec, ct);
                var evaluated = await generator.EvaluateAsync(new CandidateBatch(ctx.Output("generate-questions", "generationId"), provider, candidates, context), ct);
                var accepted = evaluated.Where(e => e.Validation.Status == ValidationStatus.Passed).ToList();
                var rejected = evaluated.Count - accepted.Count;

                // Regenerate only the shortfall; failed questions are never saved.
                for (var attempt = 1; accepted.Count < ReviewQuestionCount && attempt < options.Value.QuestionGeneration.MaxGenerationAttempts; attempt++)
                {
                    var avoid = evaluated.Select(e => e.Question.Prompt).ToHashSet(StringComparer.Ordinal);
                    var retry = await generator.GenerateCandidatesAsync(profile, spec, ReviewQuestionCount - accepted.Count, avoid, attempt, ct);
                    if (retry.Questions.Count == 0) break;
                    var more = await generator.EvaluateAsync(retry, ct);
                    evaluated.AddRange(more);
                    accepted.AddRange(more.Where(e => e.Validation.Status == ValidationStatus.Passed && accepted.All(a => a.Question.Prompt != e.Question.Prompt)));
                    rejected += more.Count(e => e.Validation.Status != ValidationStatus.Passed);
                }
                if (accepted.Count < MinimumValidQuestions)
                {
                    throw new DomainException(DomainErrorKind.Unavailable, "question-validation-failed", $"Only {accepted.Count} generated questions passed validation.");
                }
                var sessionId = ReviewSessionId(ctx.Output("plan-review", "reviewPlanId"));
                var questions = QuestionGenerationService.ToPracticeQuestions(sessionId, accepted.Take(ReviewQuestionCount));
                return Out(("passed", accepted.Count.ToString()), ("rejected", rejected.ToString()), ("practiceSessionId", sessionId), ("questions", LearningJson.Serialize(questions)));
            }),
            new Step("save-practice-session", async (ctx, ct) =>
            {
                var profile = await profiles.GetOrCreateAsync(ctx.User, ct);
                var reviewPlanId = ctx.Output("plan-review", "reviewPlanId");
                var sessionId = ctx.Output("validate-questions", "practiceSessionId");
                var questions = LearningJson.Deserialize<List<PracticeQuestion>>(ctx.Output("validate-questions", "questions"));
                var spec = new PracticeSessionSpec(sessionId, PracticeMode.Review, questions.Select(q => q.GrammarTopicId).Distinct().ToList(), profile.CurrentLevel,
                    questions.Count, new PracticeOrigin(PracticeOriginKind.ReviewWorkflow, ctx.Run.Id), reviewPlanId);
                var creation = await practice.SaveNewSessionAsync(ctx.User.UserId, sessionId, spec, questions, ct);
                await practice.LinkToReviewPlanAsync(ctx.User.UserId, reviewPlanId, sessionId, ct);
                return Out(("practiceSessionId", creation.Session.Id), ("created", creation.Created.ToString().ToLowerInvariant()));
            }),
        ];
    }

    public string Name => WorkflowName;
    public IReadOnlyList<IWorkflowStep> Steps { get; }

    public static string ReviewSessionId(string reviewPlanId) => $"ps-review-{reviewPlanId}-1";

    private static QuestionGenerationSpec ReviewSpec(LearningProfile profile, WorkflowStepContext ctx) => new(
        profile.TargetLanguage,
        profile.CurrentLevel,
        ctx.Output("plan-review", "topicIds").Split(',', StringSplitOptions.RemoveEmptyEntries),
        ReviewQuestionCount,
        "review",
        ReviewSessionId(ctx.Output("plan-review", "reviewPlanId")));

    private static StepOutcome Out(params (string Key, string Value)[] values) => new(values.ToDictionary(v => v.Key, v => v.Value));

    private sealed class Step(string name, Func<WorkflowStepContext, CancellationToken, Task<StepOutcome>> body) : IWorkflowStep
    {
        public string Name => name;
        public Task<StepOutcome> ExecuteAsync(WorkflowStepContext context, CancellationToken ct) => body(context, ct);
    }
}
