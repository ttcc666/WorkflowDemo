using Approval.Core.Abstractions;
using DynamicExpresso;

namespace Approval.Core.Conditions;

/// <summary>
/// 基于 DynamicExpresso 的表达式求值，如 <c>amount &gt; 5000 &amp;&amp; certType == "Special"</c>。
/// 只开放基础类型与关键字、不启用反射，表达式无法访问任意 .NET API。
/// </summary>
public sealed class DynamicExpressoConditionEvaluator : IConditionEvaluator
{
    public bool Evaluate(string expression, IReadOnlyDictionary<string, object?> variables)
    {
        var interpreter = new Interpreter(InterpreterOptions.PrimitiveTypes | InterpreterOptions.SystemKeywords);
        foreach (var (name, value) in variables)
        {
            if (IsIdentifier(name)) interpreter.SetVariable(name, value, value?.GetType() ?? typeof(object));
        }

        try
        {
            return interpreter.Eval<bool>(expression);
        }
        catch (Exception ex)
        {
            throw new ApprovalException($"条件表达式 '{expression}' 求值失败：{ex.Message}", ex)
            {
                Code = ApprovalErrorCode.InvalidDefinition
            };
        }
    }

    private static bool IsIdentifier(string name) =>
        name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') && name.All(c => char.IsLetterOrDigit(c) || c == '_');
}
