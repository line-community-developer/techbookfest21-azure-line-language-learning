using LineLanguageLearn.Learning.Contracts;
using LineLanguageLearn.Learning.Domain;
using LineLanguageLearn.Learning.Infrastructure;

namespace LineLanguageLearn.Learning.Knowledge;

/// <summary>Validated sample questions loaded from the database, never substituted for AI output.</summary>
public sealed class SampleQuestionBank
{
    public SampleQuestionBank(IEnumerable<SampleQuestion> questions)
    {
        Questions = questions.ToList();
        foreach (var question in Questions)
            ContractSchemas.EnsureValid("model.SampleQuestion", LearningJson.ToElement(question));
        if (Questions.Select(q => q.Id).Distinct().Count() != Questions.Count)
            throw new InvalidOperationException("Duplicate sample question IDs.");
    }

    public IReadOnlyList<SampleQuestion> Questions { get; }

}
