using System.Globalization;

namespace Approval.Core.Forms;

/// <summary>
/// 计算字段的公式：数字、字段标识（不区分大小写）、<c>+ - * / ()</c>、比较运算（<c>&gt; &lt; &gt;= &lt;= == !=</c>，只能用作 if 的条件），
/// 函数 <c>round(x, n)</c>（四舍五入到 n 位，n 为 0–10 的整数）、<c>min(a, b, …)</c>、<c>max(a, b, …)</c>、<c>abs(x)</c>、
/// <c>if(条件, 是, 否)</c>、<c>ifnull(x, 默认值)</c>。
/// 按 decimal 计算；除以 0、空值参与运算（ifnull 除外）结果为空。
/// 前端 <c>formula.ts</c> 为同一语法的实现，两边共用 <c>tests/shared/formula-cases.json</c> 测试用例。
/// </summary>
public sealed class Formula
{
    public const int MaxLength = 500;
    private const int MaxDepth = 32;

    private readonly Node _root;

    private Formula(string text, Node root, IReadOnlySet<string> identifiers)
    {
        Text = text;
        _root = root;
        Identifiers = identifiers;
    }

    public string Text { get; }

    /// <summary>引用的字段标识（不区分大小写）。</summary>
    public IReadOnlySet<string> Identifiers { get; }

    public static Formula Parse(string? text) =>
        TryParse(text, out var formula, out var error) ? formula! : throw new ApprovalException($"公式错误：{error}");

    public static bool TryParse(string? text, out Formula? formula, out string? error)
    {
        formula = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "公式为空";
            return false;
        }
        if (text.Length > MaxLength)
        {
            error = $"公式不能超过 {MaxLength} 个字符";
            return false;
        }

        try
        {
            var parser = new Parser(Tokenize(text));
            var root = parser.ParseFormula();
            if (root.Type != ValueType.Number) throw new FormulaSyntaxException("公式的结果必须是数值（比较只能用作 if 的条件）");
            formula = new Formula(text, root, parser.Identifiers);
            return true;
        }
        catch (FormulaSyntaxException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>计算结果；除以 0、空值参与运算、溢出时为 null。</summary>
    public decimal? Evaluate(Func<string, decimal?> variables)
    {
        try
        {
            return _root.Evaluate(variables).Number;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    /// <summary>四舍五入（远离零），与前端一致。</summary>
    public static decimal Round(decimal value, int precision) => decimal.Round(value, precision, MidpointRounding.AwayFromZero);

    // ───────────────────────── 词法 ─────────────────────────

    private enum TokenKind { Number, Identifier, Operator, LeftParen, RightParen, Comma, End }

    private sealed record Token(TokenKind Kind, string Text, int Position);

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            var start = i;
            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
            {
                while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
                if (i < text.Length && text[i] == '.')
                {
                    i++;
                    if (i >= text.Length || !char.IsAsciiDigit(text[i])) throw new FormulaSyntaxException($"第 {start + 1} 个字符处的数字格式不正确");
                    while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
                }
                tokens.Add(new Token(TokenKind.Number, text[start..i], start));
            }
            else if (char.IsAsciiLetter(c) || c == '_')
            {
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_')) i++;
                tokens.Add(new Token(TokenKind.Identifier, text[start..i], start));
            }
            else if (i + 1 < text.Length && text.AsSpan(i, 2) is ">=" or "<=" or "==" or "!=")
            {
                tokens.Add(new Token(TokenKind.Operator, text.Substring(i, 2), start));
                i += 2;
            }
            else
            {
                var kind = c switch
                {
                    '+' or '-' or '*' or '/' or '>' or '<' => TokenKind.Operator,
                    '(' => TokenKind.LeftParen,
                    ')' => TokenKind.RightParen,
                    ',' => TokenKind.Comma,
                    _ => throw new FormulaSyntaxException($"第 {start + 1} 个字符 '{c}' 无法识别")
                };
                tokens.Add(new Token(kind, c.ToString(), start));
                i++;
            }
        }
        tokens.Add(new Token(TokenKind.End, "", text.Length));
        return tokens;
    }

    // ───────────────────────── 语法：递归下降 ─────────────────────────

    private enum ValueType { Number, Boolean }

    private readonly record struct Value(decimal? Number, bool? Boolean);

    private abstract class Node
    {
        public abstract ValueType Type { get; }
        public abstract Value Evaluate(Func<string, decimal?> variables);
    }

    private sealed class NumberNode(decimal value) : Node
    {
        public decimal Value { get; } = value;
        public override ValueType Type => ValueType.Number;
        public override Value Evaluate(Func<string, decimal?> variables) => new(Value, null);
    }

    private sealed class VariableNode(string name) : Node
    {
        public override ValueType Type => ValueType.Number;
        public override Value Evaluate(Func<string, decimal?> variables) => new(variables(name), null);
    }

    private sealed class NegateNode(Node operand) : Node
    {
        public override ValueType Type => ValueType.Number;
        public override Value Evaluate(Func<string, decimal?> variables) => new(-operand.Evaluate(variables).Number, null);
    }

    private sealed class BinaryNode(string op, Node left, Node right) : Node
    {
        public override ValueType Type => op is "+" or "-" or "*" or "/" ? ValueType.Number : ValueType.Boolean;

        public override Value Evaluate(Func<string, decimal?> variables)
        {
            if (left.Evaluate(variables).Number is not { } a || right.Evaluate(variables).Number is not { } b) return default;
            return op switch
            {
                "+" => new(a + b, null),
                "-" => new(a - b, null),
                "*" => new(a * b, null),
                "/" => new(b == 0 ? null : a / b, null),
                ">" => new(null, a > b),
                "<" => new(null, a < b),
                ">=" => new(null, a >= b),
                "<=" => new(null, a <= b),
                "==" => new(null, a == b),
                _ => new(null, a != b)
            };
        }
    }

    private sealed class FunctionNode(string name, IReadOnlyList<Node> args) : Node
    {
        public override ValueType Type => ValueType.Number;

        public override Value Evaluate(Func<string, decimal?> variables)
        {
            switch (name)
            {
                case "if":
                    return args[0].Evaluate(variables).Boolean is { } condition
                        ? args[condition ? 1 : 2].Evaluate(variables)
                        : default;
                case "ifnull":
                    return args[0].Evaluate(variables) is { Number: not null } value ? value : args[1].Evaluate(variables);
            }

            var values = new List<decimal>(args.Count);
            foreach (var arg in args)
            {
                if (arg.Evaluate(variables).Number is not { } number) return default;
                values.Add(number);
            }
            return name switch
            {
                "round" => new(Round(values[0], (int)values[1]), null),
                "abs" => new(Math.Abs(values[0]), null),
                "min" => new(values.Min(), null),
                _ => new(values.Max(), null)
            };
        }
    }

    private sealed class Parser(List<Token> tokens)
    {
        private int _index;
        private int _depth;

        public HashSet<string> Identifiers { get; } = new(StringComparer.OrdinalIgnoreCase);

        private Token Current => tokens[_index];

        public Node ParseFormula()
        {
            var node = ParseComparison();
            if (Current.Kind != TokenKind.End) throw Unexpected();
            return node;
        }

        private Node ParseComparison()
        {
            var left = ParseAdditive();
            if (Current is not { Kind: TokenKind.Operator, Text: ">" or "<" or ">=" or "<=" or "==" or "!=" }) return left;
            var op = Next().Text;
            var right = ParseAdditive();
            if (Current is { Kind: TokenKind.Operator, Text: ">" or "<" or ">=" or "<=" or "==" or "!=" })
                throw new FormulaSyntaxException("比较运算不能连写，请用 if 嵌套");
            return new BinaryNode(op, RequireNumber(left), RequireNumber(right));
        }

        private Node ParseAdditive()
        {
            var node = ParseTerm();
            while (Current is { Kind: TokenKind.Operator, Text: "+" or "-" })
            {
                var op = Next().Text;
                node = new BinaryNode(op, RequireNumber(node), RequireNumber(ParseTerm()));
            }
            return node;
        }

        private Node ParseTerm()
        {
            var node = ParseUnary();
            while (Current is { Kind: TokenKind.Operator, Text: "*" or "/" })
            {
                var op = Next().Text;
                node = new BinaryNode(op, RequireNumber(node), RequireNumber(ParseUnary()));
            }
            return node;
        }

        private Node ParseUnary()
        {
            if (Current is { Kind: TokenKind.Operator, Text: "-" or "+" })
            {
                var negate = Next().Text == "-";
                var operand = RequireNumber(Nested(ParseUnary));
                return negate ? new NegateNode(operand) : operand;
            }
            return ParsePrimary();
        }

        private Node ParsePrimary()
        {
            var token = Current;
            switch (token.Kind)
            {
                case TokenKind.Number:
                    Next();
                    return new NumberNode(decimal.Parse(token.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture));

                case TokenKind.Identifier when tokens[_index + 1].Kind == TokenKind.LeftParen:
                    return ParseFunction();

                case TokenKind.Identifier:
                    Next();
                    Identifiers.Add(token.Text);
                    return new VariableNode(token.Text);

                case TokenKind.LeftParen:
                {
                    Next();
                    var node = Nested(ParseComparison);
                    Expect(TokenKind.RightParen, "缺少右括号");
                    return node;
                }

                default:
                    throw Unexpected();
            }
        }

        private Node ParseFunction()
        {
            var nameToken = Next();
            var name = nameToken.Text.ToLowerInvariant();
            Next(); // (
            var args = new List<Node>();
            if (Current.Kind != TokenKind.RightParen)
            {
                args.Add(Nested(ParseComparison));
                while (Current.Kind == TokenKind.Comma)
                {
                    Next();
                    args.Add(Nested(ParseComparison));
                }
            }
            Expect(TokenKind.RightParen, $"函数 {nameToken.Text} 缺少右括号");

            void Arity(int min, int? max = null)
            {
                if (args.Count < min || args.Count > (max ?? min))
                    throw new FormulaSyntaxException(min == (max ?? min)
                        ? $"函数 {name} 需要 {min} 个参数"
                        : $"函数 {name} 至少需要 {min} 个参数");
            }

            switch (name)
            {
                case "if":
                    Arity(3);
                    if (args[0].Type != ValueType.Boolean) throw new FormulaSyntaxException("if 的第一个参数必须是比较，如 if(amount > 1000, 1, 0)");
                    RequireNumber(args[1]);
                    RequireNumber(args[2]);
                    break;
                case "ifnull":
                    Arity(2);
                    args.ForEach(a => RequireNumber(a));
                    break;
                case "round":
                    Arity(2);
                    RequireNumber(args[0]);
                    if (args[1] is not NumberNode { Value: var n } || n != decimal.Truncate(n) || n > 10)
                        throw new FormulaSyntaxException("round 的第二个参数必须是 0–10 的整数，如 round(amount, 2)");
                    break;
                case "abs":
                    Arity(1);
                    RequireNumber(args[0]);
                    break;
                case "min" or "max":
                    Arity(1, int.MaxValue);
                    args.ForEach(a => RequireNumber(a));
                    break;
                default:
                    throw new FormulaSyntaxException($"不支持的函数 {nameToken.Text}（可用：round、min、max、abs、if、ifnull）");
            }
            return new FunctionNode(name, args);
        }

        private Node Nested(Func<Node> parse)
        {
            if (++_depth > MaxDepth) throw new FormulaSyntaxException("公式嵌套层数过多");
            try
            {
                return parse();
            }
            finally
            {
                _depth--;
            }
        }

        private static Node RequireNumber(Node node) =>
            node.Type == ValueType.Number ? node : throw new FormulaSyntaxException("比较的结果只能用作 if 的条件");

        private Token Next() => tokens[_index++];

        private void Expect(TokenKind kind, string message)
        {
            if (Current.Kind != kind) throw new FormulaSyntaxException(message);
            Next();
        }

        private FormulaSyntaxException Unexpected() =>
            Current.Kind == TokenKind.End
                ? new FormulaSyntaxException("公式不完整")
                : new FormulaSyntaxException($"第 {Current.Position + 1} 个字符处的 '{Current.Text}' 不符合语法");
    }

    private sealed class FormulaSyntaxException(string message) : Exception(message);

    public override string ToString() => Text;
}
