using System.Globalization;
using System.Text;
using LineLanguageLearn.Learning.Domain;

namespace LineLanguageLearn.Learning.Practice;

/// <summary>Explicit, documented normalization used for grading. No model is involved in grading.</summary>
public static class AnswerNormalizer
{
    /// <summary>
    /// trim → Unicode NFC → lower case → typographic apostrophes to ASCII → collapse whitespace →
    /// drop trailing sentence punctuation. Accents are preserved (è ≠ e).
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var text = value.Normalize(NormalizationForm.FormC).Trim().ToLowerInvariant()
            .Replace('’', '\'').Replace('‘', '\'').Replace('`', '\'').Replace('´', '\'');
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!space) builder.Append(' ');
                space = true;
                continue;
            }
            builder.Append(c);
            space = false;
        }
        return builder.ToString().Trim().TrimEnd('.', '!', '?', ',', ';', ':').TrimEnd();
    }

    public static string StripAccents(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}

public sealed record GradeResult(bool IsCorrect, FeedbackCode FeedbackCode, string NormalizedAnswer);

public static class AnswerGrader
{
    public static GradeResult Grade(PracticeQuestion question, string answer)
    {
        var normalized = AnswerNormalizer.Normalize(answer);
        var accepted = question.AcceptedAnswers.Append(question.CorrectAnswer)
            .Select(AnswerNormalizer.Normalize)
            .Where(a => a.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var isCorrect = question.Type switch
        {
            // Multiple choice: the answer must be one of the offered choices and equal the stored key.
            QuestionType.MultipleChoice => question.Choices.Any(c => AnswerNormalizer.Normalize(c) == normalized)
                && normalized == AnswerNormalizer.Normalize(question.CorrectAnswer),
            _ => accepted.Contains(normalized),
        };
        if (isCorrect) return new GradeResult(true, FeedbackCode.Correct, normalized);

        var accentOnly = normalized.Length > 0 && accepted.Any(a => a != normalized && AnswerNormalizer.StripAccents(a) == AnswerNormalizer.StripAccents(normalized));
        return new GradeResult(false, accentOnly ? FeedbackCode.AccentMismatch : FeedbackCode.Incorrect, normalized);
    }
}
