using System.Text.Json.Serialization;

namespace PorslineClone.Api.RuleEngine;

public class RuleDefinition
{
    public List<RuleCondition> Conditions { get; set; } = new();
    public string ConditionOperator { get; set; } = "AND";
    public List<RuleAction> Actions { get; set; } = new();
}

public class RuleCondition
{
    public string? Expression { get; set; }
    public Guid? SourceFieldId { get; set; }
    public string Operator { get; set; } = "equals";
    public string? Value { get; set; }
    public string? Value2 { get; set; }
    public List<string>? Values { get; set; }
}

public class RuleAction
{
    public string Type { get; set; } = "Show";
    public Guid? TargetField { get; set; }
    public string? ValueExpression { get; set; }
    public string? Message { get; set; }
}

public class RuleEvaluationRequest
{
    public Dictionary<Guid, string> Values { get; set; } = new();
    public List<RuleDefinition> Rules { get; set; } = new();
}

public class RuleEvaluationResult
{
    public bool Matched { get; set; }
    public List<RuleAction> Actions { get; set; } = new();
    public string? Error { get; set; }
}
