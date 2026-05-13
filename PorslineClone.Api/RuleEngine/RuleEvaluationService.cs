using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PorslineClone.Api.RuleEngine;

public interface IRuleEvaluationService
{
    RuleEvaluationResult Evaluate(List<RuleDefinition> rules, Dictionary<Guid, string> values);
}

public class RuleEvaluationService : IRuleEvaluationService
{
    public RuleEvaluationResult Evaluate(List<RuleDefinition> rules, Dictionary<Guid, string> values)
    {
        var result = new RuleEvaluationResult();
        foreach (var rule in rules)
        {
            var matched = EvaluateRule(rule, values);
            if (!matched) continue;
            result.Matched = true;
            result.Actions.AddRange(rule.Actions ?? new());
        }
        return result;
    }

    private bool EvaluateRule(RuleDefinition rule, Dictionary<Guid, string> values)
    {
        var bools = (rule.Conditions ?? new()).Select(c => EvaluateCondition(c, values)).ToList();
        if (bools.Count == 0) return false;
        return string.Equals(rule.ConditionOperator, "OR", StringComparison.OrdinalIgnoreCase)
            ? bools.Any(x => x)
            : bools.All(x => x);
    }

    private bool EvaluateCondition(RuleCondition cond, Dictionary<Guid, string> values)
    {
        if (!string.IsNullOrWhiteSpace(cond.Expression))
            return EvaluateExpression(cond.Expression, values);
        if (cond.SourceFieldId is not Guid fid) return false;
        var left = values.TryGetValue(fid, out var v) ? v ?? "" : "";
        var op = cond.Operator?.ToLowerInvariant() ?? "equals";
        return op switch
        {
            "equals" or "==" => left == (cond.Value ?? ""),
            "not_equals" or "!=" => left != (cond.Value ?? ""),
            "contains" => left.Contains(cond.Value ?? "", StringComparison.OrdinalIgnoreCase),
            "startswith" => left.StartsWith(cond.Value ?? "", StringComparison.OrdinalIgnoreCase),
            "endswith" => left.EndsWith(cond.Value ?? "", StringComparison.OrdinalIgnoreCase),
            "isempty" => string.IsNullOrWhiteSpace(left),
            "isnotempty" => !string.IsNullOrWhiteSpace(left),
            "in" => (cond.Values ?? new()).Contains(left),
            "not_in" => !(cond.Values ?? new()).Contains(left),
            "between" => CompareAsNumber(left, cond.Value) >= 0 && CompareAsNumber(left, cond.Value2) <= 0,
            ">" => CompareAsNumber(left, cond.Value) > 0 || CompareAsDate(left, cond.Value) > 0,
            "<" => CompareAsNumber(left, cond.Value) < 0 || CompareAsDate(left, cond.Value) < 0,
            ">=" => CompareAsNumber(left, cond.Value) >= 0 || CompareAsDate(left, cond.Value) >= 0,
            "<=" => CompareAsNumber(left, cond.Value) <= 0 || CompareAsDate(left, cond.Value) <= 0,
            "true" => bool.TryParse(left, out var b1) && b1,
            "false" => bool.TryParse(left, out var b2) && !b2,
            _ => false,
        };
    }

    private static bool EvaluateExpression(string expression, Dictionary<Guid, string> values)
    {
        var prepared = Regex.Replace(
            expression,
            @"\b[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}\b",
            match =>
            {
                if (!Guid.TryParse(match.Value, out var id)) return "0";
                if (!values.TryGetValue(id, out var raw) || string.IsNullOrWhiteSpace(raw)) return "0";
                if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
                    return dec.ToString(CultureInfo.InvariantCulture);
                if (bool.TryParse(raw, out var b))
                    return b ? "1" : "0";
                return "0";
            },
            RegexOptions.IgnoreCase);
        try
        {
            var dt = new DataTable();
            var r = dt.Compute(prepared, string.Empty);
            return r switch
            {
                bool b => b,
                decimal d => d != 0,
                double d => Math.Abs(d) > double.Epsilon,
                int i => i != 0,
                long l => l != 0,
                string s when bool.TryParse(s, out var sb) => sb,
                _ => false
            };
        }
        catch { return false; }
    }

    private static int CompareAsNumber(string? left, string? right)
    {
        var l = decimal.TryParse(left, NumberStyles.Any, CultureInfo.InvariantCulture, out var dl) ? dl : 0m;
        var r = decimal.TryParse(right, NumberStyles.Any, CultureInfo.InvariantCulture, out var dr) ? dr : 0m;
        return l.CompareTo(r);
    }

    private static int CompareAsDate(string? left, string? right)
    {
        var lOk = DateTime.TryParse(left, out var dl);
        var rOk = DateTime.TryParse(right, out var dr);
        if (!lOk || !rOk) return int.MinValue;
        return dl.CompareTo(dr);
    }
}
