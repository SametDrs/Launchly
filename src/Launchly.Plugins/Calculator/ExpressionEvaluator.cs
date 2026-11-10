using System.Globalization;

namespace Launchly.Plugins.Calculator;

/// <summary>
/// Küçük, güvenli özyinelemeli ifade çözücü: + - * / % ^, parantez, tekli eksi/artı, ondalık (nokta).
/// Eval/DataTable gibi keyfi ifade çalıştırmaz; geçersiz girdi ve tanımsız sonuçlar (sıfıra bölme) için false döner.
/// </summary>
public static class ExpressionEvaluator
{
    private const int MaxLength = 200;
    private const int MaxDepth = 50;

    public static bool TryEvaluate(string? expression, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > MaxLength)
        {
            return false;
        }

        var parser = new Parser(expression);
        if (!parser.TryParse(out var result) || double.IsNaN(result) || double.IsInfinity(result))
        {
            return false;
        }

        value = result;
        return true;
    }

    /// <summary>İfade hesap gibi mi görünüyor: yalnızca izinli karakterler, en az bir rakam ve bir işleç/parantez.</summary>
    public static bool LooksLikeMath(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var hasDigit = false;
        var hasOperator = false;
        foreach (var c in text)
        {
            if (char.IsAsciiDigit(c))
            {
                hasDigit = true;
            }
            else if (c is '+' or '-' or '*' or '/' or '%' or '^' or '(' or ')')
            {
                hasOperator = true;
            }
            else if (c is not ('.' or ' '))
            {
                return false;
            }
        }

        return hasDigit && hasOperator;
    }

    public static string Format(double value) =>
        value.ToString("0.###############", CultureInfo.InvariantCulture);

    private sealed class Parser(string text)
    {
        private int _pos;
        private int _depth;
        private bool _failed;

        public bool TryParse(out double result)
        {
            result = ParseExpression();
            SkipSpaces();
            return !_failed && _pos == text.Length;
        }

        private double ParseExpression()
        {
            var left = ParseTerm();
            while (!_failed)
            {
                SkipSpaces();
                if (Peek() is '+')
                {
                    _pos++;
                    left += ParseTerm();
                }
                else if (Peek() is '-')
                {
                    _pos++;
                    left -= ParseTerm();
                }
                else
                {
                    break;
                }
            }

            return left;
        }

        private double ParseTerm()
        {
            var left = ParseUnary();
            while (!_failed)
            {
                SkipSpaces();
                var op = Peek();
                if (op is not ('*' or '/' or '%'))
                {
                    break;
                }

                _pos++;
                var right = ParseUnary();
                if (op == '*')
                {
                    left *= right;
                }
                else if (right == 0)
                {
                    _failed = true; // sıfıra bölme / mod: sonuç yok
                    return 0;
                }
                else
                {
                    left = op == '/' ? left / right : left % right;
                }
            }

            return left;
        }

        // Üs sağdan birleşir ve tekli eksiden güçlüdür: -2^2 = -4.
        private double ParsePower()
        {
            var left = ParsePrimary();
            SkipSpaces();
            if (!_failed && Peek() is '^')
            {
                _pos++;
                if (++_depth > MaxDepth)
                {
                    _failed = true;
                    return 0;
                }

                var right = ParseUnary();
                _depth--;
                return Math.Pow(left, right);
            }

            return left;
        }

        private double ParseUnary()
        {
            SkipSpaces();
            if (Peek() is '-' or '+')
            {
                var negative = Peek() == '-';
                _pos++;
                if (++_depth > MaxDepth)
                {
                    _failed = true;
                    return 0;
                }

                var v = ParseUnary();
                _depth--;
                return negative ? -v : v;
            }

            return ParsePower();
        }

        private double ParsePrimary()
        {
            SkipSpaces();
            if (Peek() is '(')
            {
                _pos++;
                if (++_depth > MaxDepth)
                {
                    _failed = true;
                    return 0;
                }

                var v = ParseExpression();
                _depth--;
                SkipSpaces();
                if (Peek() is not ')')
                {
                    _failed = true;
                    return 0;
                }

                _pos++;
                return v;
            }

            var start = _pos;
            while (_pos < text.Length && (char.IsAsciiDigit(text[_pos]) || text[_pos] == '.'))
            {
                _pos++;
            }

            if (_pos == start ||
                !double.TryParse(text.AsSpan(start, _pos - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
            {
                _failed = true;
                return 0;
            }

            return number;
        }

        private char Peek() => _pos < text.Length ? text[_pos] : '\0';

        private void SkipSpaces()
        {
            while (_pos < text.Length && text[_pos] == ' ')
            {
                _pos++;
            }
        }
    }
}
