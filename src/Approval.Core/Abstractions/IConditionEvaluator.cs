namespace Approval.Core.Abstractions;

/// <summary>条件分支表达式求值。</summary>
public interface IConditionEvaluator
{
    bool Evaluate(string expression, IReadOnlyDictionary<string, object?> variables);
}
