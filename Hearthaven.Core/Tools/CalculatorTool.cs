using System.Text.Json;
using Hearthaven.Core.Utilities;

namespace Hearthaven.Core.Tools;

/// <summary>
/// 精确计算器工具 — 解析数学表达式并求值。
/// 使用 Shunting Yard 算法实现，不依赖任何第三方库。
/// 支持四则运算、幂运算、取模、括号、负数。
/// </summary>
public class CalculatorTool : ITool
{
    public string Name => "calculator";
    public string Description => "精确计算数学表达式。支持 + - * / % ^ 和括号，支持负数。";
    public bool IsLongRunning => false;

    public string GetDisplayTitle(string argsJson)
    {
        var expr = Utilities.JsonHelper.ExtractString(argsJson, "expression");
        return expr != null ? $"计算 [{expr}]" : "计算器";
    }

    public object GetParametersSchema() => new
    {
        type = "object",
        properties = new
        {
            expression = new
            {
                type = "string",
                description = "要计算的数学表达式，如 \"(15 + 3) * 4 / 2\""
            }
        },
        required = new[] { "expression" }
    };

    public Task<ToolOutput> ExecuteAsync(string argsJson, CancellationToken ct = default)
    {
        try
        {
            var args = JsonSerializer.Deserialize<CalculatorArgs>(argsJson);
            if (string.IsNullOrWhiteSpace(args?.Expression))
                return Task.FromResult(ToolOutput.Error("错误：缺少 expression 参数"));

            var result = Evaluate(args.Expression.Trim());
            return Task.FromResult(ToolOutput.Success($"{args.Expression.Trim()} = {result}"));
        }
        catch (JsonException)
        {
            return Task.FromResult(ToolOutput.Error("错误：参数解析失败，需要提供 expression 字符串参数"));
        }
        catch (CalcException ex)
        {
            return Task.FromResult(ToolOutput.Error($"错误：{ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolOutput.Error($"错误：计算时发生异常 — {ex.Message}"));
        }
    }

    /// <summary>解析并计算表达式</summary>
    private static string Evaluate(string expression)
    {
        var tokens = Tokenize(expression);
        var rpn = ShuntingYard(tokens);
        var result = EvaluateRpn(rpn);

        // 如果结果是整数（误差小于 1e-10），显示为整数
        if (Math.Abs(result - Math.Round(result)) < 1e-10)
            return Math.Round(result).ToString("0");
        return result.ToString("G");
    }

    // ===== 词法分析 =====

    private enum TokenType { Number, Plus, Minus, Multiply, Divide, Modulo, Power, LParen, RParen, End }

    private sealed record Token(TokenType Type, double Value = 0);

    /// <summary>词法分析：字符串 → Token 列表</summary>
    private static List<Token> Tokenize(string expr)
    {
        var tokens = new List<Token>();
        int i = 0;

        while (i < expr.Length)
        {
            var ch = expr[i];

            // 跳过空白
            if (char.IsWhiteSpace(ch))
            {
                i++;
                continue;
            }

            // 数字
            if (char.IsDigit(ch) || (ch == '.' && i + 1 < expr.Length && char.IsDigit(expr[i + 1])))
            {
                int start = i;
                while (i < expr.Length && (char.IsDigit(expr[i]) || expr[i] == '.')) i++;
                var numStr = expr[start..i];
                if (!double.TryParse(numStr,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var num))
                    throw new CalcException($"无法解析数字 '{numStr}'");
                tokens.Add(new Token(TokenType.Number, num));
                continue;
            }

            // 运算符和括号
            switch (ch)
            {
                case '+': tokens.Add(new Token(TokenType.Plus)); break;
                case '-': tokens.Add(new Token(TokenType.Minus)); break;
                case '*': tokens.Add(new Token(TokenType.Multiply)); break;
                case '/': tokens.Add(new Token(TokenType.Divide)); break;
                case '%': tokens.Add(new Token(TokenType.Modulo)); break;
                case '^': tokens.Add(new Token(TokenType.Power)); break;
                case '(': tokens.Add(new Token(TokenType.LParen)); break;
                case ')': tokens.Add(new Token(TokenType.RParen)); break;
                default: throw new CalcException($"无法识别的字符 '{ch}'");
            }
            i++;
        }

        tokens.Add(new Token(TokenType.End));
        return tokens;
    }

    // ===== Shunting Yard：中缀 → 后缀（RPN）=====

    private static readonly Dictionary<TokenType, (int Precedence, bool RightAssoc)> OpInfo = new()
    {
        [TokenType.Power] = (4, true),    // ^ 右结合
        [TokenType.Multiply] = (3, false),
        [TokenType.Divide] = (3, false),
        [TokenType.Modulo] = (3, false),
        [TokenType.Plus] = (2, false),
        [TokenType.Minus] = (2, false),
    };

    /// <summary>调度场算法：处理一元负号，中缀 → RPN</summary>
    private static List<Token> ShuntingYard(List<Token> tokens)
    {
        var output = new List<Token>();
        var opStack = new Stack<Token>();
        bool expectUnary = true; // 表达式开头或左括号后，期待一元运算符

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            switch (token.Type)
            {
                case TokenType.Number:
                    output.Add(token);
                    expectUnary = false;
                    break;

                case TokenType.Minus when expectUnary:
                    // 一元负号：压入特殊标记（用 Power 的优先级但含义不同，实际用 0 替换值）
                    opStack.Push(new Token(TokenType.Power, -1)); // 用 value=-1 标记一元负号
                    break;

                case TokenType.LParen:
                    opStack.Push(token);
                    expectUnary = true;
                    break;

                case TokenType.RParen:
                    while (opStack.Count > 0 && opStack.Peek().Type != TokenType.LParen)
                        output.Add(opStack.Pop());
                    if (opStack.Count == 0)
                        throw new CalcException("括号不匹配：缺少左括号");
                    opStack.Pop(); // 弹出左括号
                    expectUnary = false;
                    break;

                case TokenType.End:
                    // 结束
                    break;

                default: // 二元运算符
                    var op = token.Type;

                    while (opStack.Count > 0)
                    {
                        var top = opStack.Peek();

                        // 一元负号特殊处理
                        if (top.Type == TokenType.Power && top.Value == -1)
                        {
                            // 一元负号优先级很高，弹出
                            output.Add(opStack.Pop());
                            continue;
                        }

                        if (top.Type == TokenType.LParen) break;

                        var (topPrec, topRight) = OpInfo[top.Type];
                        var (opPrec, _) = OpInfo[op];

                        // 当栈顶运算符优先级更高或相同时弹出（左结合时相同也弹出）
                        bool shouldPop = topRight
                            ? topPrec > opPrec   // 右结合：只有更高才弹出
                            : topPrec >= opPrec; // 左结合：相同或更高都弹出

                        if (!shouldPop) break;
                        output.Add(opStack.Pop());
                    }

                    opStack.Push(token);
                    expectUnary = false;
                    break;
            }
        }

        // 弹出剩余运算符
        while (opStack.Count > 0)
        {
            var top = opStack.Pop();
            if (top.Type == TokenType.LParen)
                throw new CalcException("括号不匹配：缺少右括号");
            output.Add(top);
        }

        return output;
    }

    // ===== RPN 求值 =====

    /// <summary>计算 RPN（逆波兰表达式）的值</summary>
    private static double EvaluateRpn(List<Token> rpn)
    {
        var stack = new Stack<double>();

        foreach (var token in rpn)
        {
            if (token.Type == TokenType.Number)
            {
                stack.Push(token.Value);
                continue;
            }

            // 一元负号
            if (token.Type == TokenType.Power && token.Value == -1)
            {
                if (stack.Count < 1)
                    throw new CalcException("表达式错误：缺少操作数");
                stack.Push(-stack.Pop());
                continue;
            }

            // 二元运算符
            if (stack.Count < 2)
                throw new CalcException("表达式错误：缺少操作数");

            var b = stack.Pop();
            var a = stack.Pop();

            stack.Push(token.Type switch
            {
                TokenType.Plus => a + b,
                TokenType.Minus => a - b,
                TokenType.Multiply => a * b,
                TokenType.Divide => b == 0 ? throw new CalcException("除数不能为 0") : a / b,
                TokenType.Modulo => a % b,
                TokenType.Power => Math.Pow(a, b),
                _ => throw new CalcException($"未知运算符 {token.Type}")
            });
        }

        if (stack.Count != 1)
            throw new CalcException("表达式格式错误");

        return stack.Pop();
    }

    private class CalculatorArgs
    {
        [System.Text.Json.Serialization.JsonPropertyName("expression")]
        public string? Expression { get; set; }
    }
}

/// <summary>
/// 计算器专用异常
/// </summary>
internal sealed class CalcException : Exception
{
    public CalcException(string message) : base(message) { }
}
