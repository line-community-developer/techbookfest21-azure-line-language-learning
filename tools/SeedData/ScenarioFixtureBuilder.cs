using LineLanguageLearn.Learning.Ai.Mock;
using LineLanguageLearn.Learning.Api;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Identity;
using LineLanguageLearn.Learning.Practice;
using LineLanguageLearn.Learning.Tutor;
using LineLanguageLearn.Learning.Workflows;

namespace LineLanguageLearn.SeedData;

/// <summary>
/// Creates the "Demo Learner" (A2; strong: articles / present tense, weak: a/di, passato prossimo auxiliary)
/// by driving the real services: practice sessions are answered through PracticeService and a prepared
/// conversation goes through TutorService + the post-conversation workflow (mock AI).
/// </summary>
public sealed class ScenarioFixtureBuilder(
    LearningProfileService profiles,
    PracticeService practice,
    TutorService tutor,
    WorkflowRunner workflows)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static readonly string[] DemoConversation =
    [
        "Ciao Giulia! Sabato ho andato al mare con i miei amici.",
        "Sì, e ho mangiato una pizza buonissima. Domenica ho finito a studiare alle cinque.",
        "Adesso comincio di studiare il tedesco, ma è difficile.",
        "Stasera ho voglia di mangiare il sushi.",
    ];

    public async Task CreateIfMissingAsync(UserContext user, CancellationToken ct)
    {
        if (await profiles.FindAsync(user.UserId, ct) is not null) return;
        await Gate.WaitAsync(ct);
        try
        {
            if (await profiles.FindAsync(user.UserId, ct) is null) await SeedAsync(user, ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task SeedAsync(UserContext user, CancellationToken ct)
    {
        var profile = await profiles.GetOrCreateAsync(user, ct);
        profile.LearnerName = user.DisplayName;
        profile.CurrentLevel = CefrLevel.A2;
        profile.LearningGoals = ["イタリア旅行で日常会話ができるようになる", "週末の出来事を近過去で話せるようになる"];
        await profiles.SaveAsync(profile, ct);

        // Strong: everything correct.
        await PracticeAsync(user, "ps-demo-basics", [KnownTopics.DefiniteArticles, KnownTopics.PresentRegular], 8, _ => true, ct);
        // Weak: a / di (mostly wrong, mixing up the two prepositions).
        await PracticeAsync(user, "ps-demo-a-di", [KnownTopics.VerbA, KnownTopics.VerbDi], 6, i => i == 3, ct);
        // Weak: passato prossimo auxiliary.
        await PracticeAsync(user, "ps-demo-passato", [KnownTopics.PassatoAuxiliary], 4, i => i == 2, ct);

        var session = await tutor.StartSessionAsync(user, "週末の出来事", TutorChannel.DemoSeed, ct, "ts-demo-weekend");
        for (var i = 0; i < DemoConversation.Length; i++)
        {
            await tutor.SendMessageAsync(user, session.Id, $"demo-event-{i + 1}", DemoConversation[i], ct);
        }
        // Offline generation runs the workflow synchronously instead of through Durable Functions.
        await tutor.EndSessionAsync(user, session.Id, new InProcessWorkflowScheduler(workflows), ct);
    }

    private async Task PracticeAsync(UserContext user, string sessionId, string[] topics, int count, Func<int, bool> answerCorrectly, CancellationToken ct)
    {
        var creation = await practice.CreateSessionAsync(user, new PracticeSessionSpec(sessionId, PracticeMode.Topic, topics, CefrLevel.A2, count,
            new PracticeOrigin(PracticeOriginKind.DemoSeed, null), null), ct);
        foreach (var question in creation.Session.Questions.OrderBy(q => q.Order))
        {
            var answer = answerCorrectly(question.Order) ? question.CorrectAnswer : TypicalMistake(question);
            await practice.SubmitAnswerAsync(user, sessionId, new SubmitAnswerRequest(question.Id, answer, $"demo-{question.Id}"), ct);
        }
        await practice.CompleteAsync(user, sessionId, ct);
    }

    private static string TypicalMistake(PracticeQuestion question) => question.CorrectAnswer switch
    {
        "a" => "di",
        "di" => "a",
        "sono" => "ho",
        "ho" => "sono",
        "è" => "ha",
        "siamo" => "abbiamo",
        _ => question.Choices.FirstOrDefault(c => !string.Equals(c, question.CorrectAnswer, StringComparison.OrdinalIgnoreCase)) ?? "?",
    };
}
