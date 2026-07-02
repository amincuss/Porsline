using System.Text.Json;
using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services.ExamDispatch;

public record ExamScoreResult(int CorrectCount, int ScorableQuestionCount, bool? IsPassed);

public static class ExamScoringHelper
{
    public static int CountScorableQuestions(IEnumerable<ExamQuestion> questions) =>
        questions.Count(IsScorable);

    public static bool IsScorable(ExamQuestion q) =>
        q.QuestionType is ExamQuestionType.TwoOption or ExamQuestionType.FourOption
        && q.CorrectAnswerIndex is not null;

    public static ExamScoreResult Score(ExamForm form, IReadOnlyDictionary<string, string> answers, int? passingCorrectCount)
    {
        var scorable = form.Questions.Where(IsScorable).ToList();
        var correct = 0;
        foreach (var q in scorable)
        {
            if (!answers.TryGetValue(q.Id.ToString(), out var raw) || string.IsNullOrWhiteSpace(raw))
                continue;
            var options = DeserializeOptions(q);
            var idx = q.CorrectAnswerIndex!.Value;
            if (idx < 0 || idx >= options.Count) continue;
            if (string.Equals(raw.Trim(), options[idx].Trim(), StringComparison.Ordinal))
                correct++;
        }

        bool? passed = passingCorrectCount is int pass && pass > 0 && scorable.Count > 0
            ? correct >= pass
            : null;

        return new ExamScoreResult(correct, scorable.Count, passed);
    }

    private static List<string> DeserializeOptions(ExamQuestion q)
    {
        if (string.IsNullOrWhiteSpace(q.OptionsJson))
            return DefaultOptions(q.QuestionType);
        try
        {
            return JsonSerializer.Deserialize<List<string>>(q.OptionsJson) ?? DefaultOptions(q.QuestionType);
        }
        catch
        {
            return DefaultOptions(q.QuestionType);
        }
    }

    public static string? GetCorrectAnswerText(ExamQuestion q)
    {
        if (!IsScorable(q) || q.CorrectAnswerIndex is not int idx) return null;
        var options = DeserializeOptions(q);
        if (idx < 0 || idx >= options.Count) return null;
        return options[idx];
    }

    public static bool? IsAnswerCorrect(ExamQuestion q, string? userAnswer)
    {
        if (!IsScorable(q) || string.IsNullOrWhiteSpace(userAnswer)) return null;
        var expected = GetCorrectAnswerText(q);
        if (expected is null) return null;
        return string.Equals(userAnswer.Trim(), expected.Trim(), StringComparison.Ordinal);
    }

    private static List<string> DefaultOptions(ExamQuestionType type) => type switch
    {
        ExamQuestionType.TwoOption => ["گزینه ۱", "گزینه ۲"],
        ExamQuestionType.FourOption => ["گزینه ۱", "گزینه ۲", "گزینه ۳", "گزینه ۴"],
        _ => [],
    };
}
