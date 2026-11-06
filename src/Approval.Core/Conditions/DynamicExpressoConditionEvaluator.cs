using Approval.Core.Abstractions;
using DynamicExpresso;
using System.Dynamic;
using System.Linq.Expressions;

namespace Approval.Core.Conditions;

/// <summary>
/// 基于 DynamicExpresso 的表达式求值，如 <c>amount &gt; 5000 &amp;&amp; certType == "Special"</c>。
/// 只开放基础类型与关键字、不启用反射，表达式无法访问任意 .NET API。
/// </summary>
public sealed class DynamicExpressoConditionEvaluator : IConditionEvaluator
{
    public static string? ValidateSyntax(string expression)
    {
        try
        {
            var interpreter = CreateInterpreter();
            var qualifiedRoots = ActionConditionNamespace.QualifiedRoots(expression);
            foreach (var identifier in interpreter.DetectIdentifiers(expression).UnknownIdentifiers)
            {
                var escaped = System.Text.RegularExpressions.Regex.Escape(identifier);
                object sample = System.Text.RegularExpressions.Regex.IsMatch(expression, escaped + "\\s*(?:==|!=)\\s*\\\"|\\\"[^\\\"]*\\\"\\s*(?:==|!=)\\s*" + escaped)
                    ? "" : System.Text.RegularExpressions.Regex.IsMatch(expression, "(?:!\\s*" + escaped + "\\b|\\b" + escaped + "\\s*(?:&&|\\|\\||$))")
                    || System.Text.RegularExpressions.Regex.IsMatch(expression, escaped + "\\s*(?:==|!=)\\s*(?:true|false)\\b|\\b(?:true|false)\\s*(?:==|!=)\\s*" + escaped)
                    ? false : 0m;
                if (qualifiedRoots.Contains(identifier)) sample = new ExpandoObject();
                interpreter.SetVariable(identifier, sample);
            }
            var parsed = interpreter.Parse(expression);
            if (parsed.ReturnType != typeof(bool) && parsed.ReturnType != typeof(object)) return "条件表达式必须返回布尔值";
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public bool Evaluate(string expression, IReadOnlyDictionary<string, object?> variables)
    {
        var interpreter = CreateInterpreter();
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

    private static Interpreter CreateInterpreter()
    {
        var interpreter = new Interpreter(InterpreterOptions.PrimitiveTypes | InterpreterOptions.SystemKeywords);
        interpreter.Visitors.Add(new RejectTypeInspection());
        return interpreter;
    }

    private sealed class RejectTypeInspection : DynamicExpressionVisitor
    {
        protected override Expression VisitDynamic(DynamicExpression node)
        {
            if (node.Binder is InvokeMemberBinder { Name: nameof(GetType) }) throw new InvalidOperationException("条件表达式不能读取 CLR 类型");
            return base.VisitDynamic(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == nameof(GetType)) throw new InvalidOperationException("条件表达式不能读取 CLR 类型");
            return base.VisitMethodCall(node);
        }
    }
}
