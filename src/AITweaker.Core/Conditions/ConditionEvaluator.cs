using System.Collections.Concurrent;
using System.Globalization;

namespace AITweaker.Core.Conditions;

public sealed class ConditionSyntaxException(string message) : Exception(message);

/// <summary>
/// Tiny expression language used by the catalog to describe when a tweak fits the machine:
/// <code>gpu.nvidia &amp;&amp; !system.laptop &amp;&amp; ram.gb &gt;= 16</code>
/// Operators: <c>|| &amp;&amp; ! == != &gt;= &lt;= &gt; &lt;</c> and <c>~</c> (case-insensitive "contains").
/// Unknown facts evaluate to null, which is falsy and never equal to anything.
/// </summary>
public static class ConditionEvaluator
{
    private static readonly ConcurrentDictionary<string, Node> Cache = new();

    public static bool Evaluate(string? expression, IReadOnlyDictionary<string, object?> facts)
    {
        if (string.IsNullOrWhiteSpace(expression)) return true;
        var node = Cache.GetOrAdd(expression, Parse);
        return Truthy(node.Eval(facts));
    }

    /// <summary>Parses the expression and returns every fact identifier it references.</summary>
    public static IReadOnlyCollection<string> Identifiers(string expression)
    {
        var node = Parse(expression);
        var set = new HashSet<string>(StringComparer.Ordinal);
        node.Collect(set);
        return set;
    }

    public static Node Parse(string expression)
    {
        var parser = new Parser(Tokenize(expression));
        var node = parser.ParseOr();
        if (!parser.AtEnd) throw new ConditionSyntaxException($"Unexpected '{parser.Peek.Text}' in: {expression}");
        return node;
    }

    internal static bool Truthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        double d => d != 0,
        string s => s.Length > 0,
        _ => true,
    };

    // ---------------------------------------------------------------- tokenizer

    private enum T { Ident, Number, String, Op, LParen, RParen, End }

    private readonly record struct Token(T Kind, string Text);

    private static List<Token> Tokenize(string s)
    {
        var list = new List<Token>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '(') { list.Add(new(T.LParen, "(")); i++; continue; }
            if (c == ')') { list.Add(new(T.RParen, ")")); i++; continue; }
            if (c == '"' || c == '\'')
            {
                int j = s.IndexOf(c, i + 1);
                if (j < 0) throw new ConditionSyntaxException($"Unterminated string in: {s}");
                list.Add(new(T.String, s[(i + 1)..j]));
                i = j + 1;
                continue;
            }
            if (char.IsDigit(c))
            {
                int j = i;
                while (j < s.Length && (char.IsDigit(s[j]) || s[j] == '.')) j++;
                list.Add(new(T.Number, s[i..j]));
                i = j;
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int j = i;
                while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] is '_' or '.')) j++;
                list.Add(new(T.Ident, s[i..j]));
                i = j;
                continue;
            }
            string two = i + 1 < s.Length ? s.Substring(i, 2) : "";
            if (two is "&&" or "||" or "==" or "!=" or ">=" or "<=") { list.Add(new(T.Op, two)); i += 2; continue; }
            if (c is '!' or '>' or '<' or '~') { list.Add(new(T.Op, c.ToString())); i++; continue; }
            throw new ConditionSyntaxException($"Unexpected character '{c}' in: {s}");
        }
        list.Add(new(T.End, ""));
        return list;
    }

    // ---------------------------------------------------------------- parser

    private sealed class Parser(List<Token> tokens)
    {
        private int _pos;
        public Token Peek => tokens[_pos];
        public bool AtEnd => Peek.Kind == T.End;
        private Token Next() => tokens[_pos++];

        public Node ParseOr()
        {
            var left = ParseAnd();
            while (Peek is { Kind: T.Op, Text: "||" }) { Next(); left = new Binary("||", left, ParseAnd()); }
            return left;
        }

        private Node ParseAnd()
        {
            var left = ParseUnary();
            while (Peek is { Kind: T.Op, Text: "&&" }) { Next(); left = new Binary("&&", left, ParseUnary()); }
            return left;
        }

        private Node ParseUnary()
        {
            if (Peek is { Kind: T.Op, Text: "!" }) { Next(); return new Not(ParseUnary()); }
            return ParseComparison();
        }

        private Node ParseComparison()
        {
            var left = ParsePrimary();
            if (Peek is { Kind: T.Op, Text: "==" or "!=" or ">=" or "<=" or ">" or "<" or "~" })
            {
                var op = Next().Text;
                return new Binary(op, left, ParsePrimary());
            }
            return left;
        }

        private Node ParsePrimary()
        {
            var t = Next();
            switch (t.Kind)
            {
                case T.LParen:
                    var inner = ParseOr();
                    if (Next().Kind != T.RParen) throw new ConditionSyntaxException("Missing ')'");
                    return inner;
                case T.Number:
                    return new Literal(double.Parse(t.Text, CultureInfo.InvariantCulture));
                case T.String:
                    return new Literal(t.Text);
                case T.Ident when t.Text == "true":
                    return new Literal(true);
                case T.Ident when t.Text == "false":
                    return new Literal(false);
                case T.Ident when t.Text == "null":
                    return new Literal(null);
                case T.Ident:
                    return new Fact(t.Text);
                default:
                    throw new ConditionSyntaxException($"Unexpected token '{t.Text}'");
            }
        }
    }

    // ---------------------------------------------------------------- AST

    public abstract class Node
    {
        public abstract object? Eval(IReadOnlyDictionary<string, object?> facts);
        public virtual void Collect(HashSet<string> identifiers) { }
    }

    private sealed class Literal(object? value) : Node
    {
        public override object? Eval(IReadOnlyDictionary<string, object?> facts) => value;
    }

    private sealed class Fact(string name) : Node
    {
        public override object? Eval(IReadOnlyDictionary<string, object?> facts) =>
            facts.TryGetValue(name, out var v) ? Normalize(v) : null;

        public override void Collect(HashSet<string> identifiers) => identifiers.Add(name);
    }

    private sealed class Not(Node inner) : Node
    {
        public override object? Eval(IReadOnlyDictionary<string, object?> facts) => !Truthy(inner.Eval(facts));
        public override void Collect(HashSet<string> identifiers) => inner.Collect(identifiers);
    }

    private sealed class Binary(string op, Node left, Node right) : Node
    {
        public override void Collect(HashSet<string> identifiers)
        {
            left.Collect(identifiers);
            right.Collect(identifiers);
        }

        public override object? Eval(IReadOnlyDictionary<string, object?> facts)
        {
            if (op == "&&") return Truthy(left.Eval(facts)) && Truthy(right.Eval(facts));
            if (op == "||") return Truthy(left.Eval(facts)) || Truthy(right.Eval(facts));

            var a = left.Eval(facts);
            var b = right.Eval(facts);
            switch (op)
            {
                case "==": return a is not null && b is not null && Compare(a, b) == 0;
                case "!=": return a is null || b is null ? !(a is null && b is null) : Compare(a, b) != 0;
                case "~":
                    return a is not null && b is not null &&
                           Convert.ToString(a, CultureInfo.InvariantCulture)!.Contains(Convert.ToString(b, CultureInfo.InvariantCulture)!, StringComparison.OrdinalIgnoreCase);
            }
            if (a is null || b is null) return false;
            int c = Compare(a, b);
            return op switch
            {
                ">" => c > 0,
                "<" => c < 0,
                ">=" => c >= 0,
                "<=" => c <= 0,
                _ => throw new ConditionSyntaxException($"Unknown operator {op}"),
            };
        }

        private static int Compare(object a, object b)
        {
            if (a is double da && b is double db) return da.CompareTo(db);
            if (a is bool ba && b is bool bb) return ba.CompareTo(bb);
            return string.Compare(Convert.ToString(a, CultureInfo.InvariantCulture), Convert.ToString(b, CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static object? Normalize(object? v) => v switch
    {
        null => null,
        bool b => b,
        string s => s,
        int i => (double)i,
        long l => (double)l,
        uint u => (double)u,
        ulong ul => (double)ul,
        float f => (double)f,
        double d => d,
        decimal m => (double)m,
        _ => v.ToString(),
    };
}
