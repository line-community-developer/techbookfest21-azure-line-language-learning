namespace LineLanguageLearn.Learning.Domain;

/// <summary>Maps CEFR levels to the question difficulty range (1-5) and topic levels a learner should see.</summary>
public static class LevelPolicy
{
    public static (int Min, int Max) DifficultyRange(CefrLevel level) => level switch
    {
        CefrLevel.A1 => (1, 2),
        CefrLevel.A2 => (1, 3),
        CefrLevel.B1 => (2, 4),
        CefrLevel.B2 => (2, 5),
        _ => (3, 5),
    };

    /// <summary>Topics up to one level above the learner are allowed (i+1).</summary>
    public static bool TopicAllowed(CefrLevel learnerLevel, CefrLevel topicLevel) => topicLevel.Rank() <= learnerLevel.Rank() + 1;

    public static CefrLevel MaxRetrievalLevel(CefrLevel learnerLevel) =>
        (CefrLevel)Math.Min((int)CefrLevel.C2, learnerLevel.Rank() + 1);
}
