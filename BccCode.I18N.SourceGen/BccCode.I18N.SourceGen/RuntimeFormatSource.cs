namespace BccCode.I18N.SourceGen;

/// <summary>
/// Source of the runtime message formatter that is emitted into <c>LanguageStrings</c> (for <c>Format</c>).
/// It mirrors <see cref="TranslationMessageSyntax"/>: placeholders <c>{name}</c> / <c>{name:format}</c>
/// (name is <c>\w+</c>, format has no braces), literals <c>{'text'}</c> and pipes outside of both.
/// Links are already resolved in the stored values, so they are not handled here.
/// Only methods are emitted (no static fields), so hot reload keeps working. The code uses C# 8 features at most.
/// </summary>
internal static class RuntimeFormatSource
{
    public const string Code = @"private static string FormatCore(string key, string language, global::System.IFormatProvider? provider, object?[]? args)
{
    var text = GetStringOrNullByLanguage(key, language);
    if (text == null)
    {
        return key;
    }

    return Substitute(
        SelectForm(text, args ?? global::System.Array.Empty<object?>()),
        GetParameterNames(key),
        args ?? global::System.Array.Empty<object?>(),
        provider);
}

// Selects the plural form with the vue-i18n rules: the absolute value of the first argument is the count.
// Two forms: 1 selects the first form, anything else the second. Three or more forms: the count is the
// index, and larger counts use the last form. Without a numeric first argument the count is 1.
private static string SelectForm(string text, object?[] args)
{
    global::System.Collections.Generic.List<int>? pipes = null;
    var index = 0;
    while (index < text.Length)
    {
        var character = text[index];
        if (character == '{')
        {
            var end = ScanBrace(text, index, out _, out _, out _);
            if (end > 0)
            {
                index = end;
                continue;
            }
        }
        else if (character == '|')
        {
            if (pipes == null)
            {
                pipes = new global::System.Collections.Generic.List<int>();
            }

            pipes.Add(index);
        }

        index++;
    }

    if (pipes == null)
    {
        return text;
    }

    var formCount = pipes.Count + 1;
    long count;
    if (args.Length == 0 || !TryGetCount(args[0], out count))
    {
        count = 1;
    }

    if (count < 0)
    {
        // long.MinValue cannot be negated, also not in a checked context.
        count = count == long.MinValue ? long.MaxValue : -count;
    }

    int selected;
    if (formCount == 2)
    {
        selected = count == 1 ? 0 : 1;
    }
    else
    {
        selected = count < formCount - 1 ? (int)count : formCount - 1;
    }

    var from = selected == 0 ? 0 : pipes[selected - 1] + 1;
    var to = selected == pipes.Count ? text.Length : pipes[selected];
    return text.Substring(from, to - from).Trim();
}

private static bool TryGetCount(object? value, out long count)
{
    if (value is sbyte || value is byte || value is short || value is ushort || value is int || value is uint
        || value is long || value is ulong || value is float || value is double || value is decimal)
    {
        try
        {
            count = global::System.Convert.ToInt64(value, global::System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (global::System.OverflowException)
        {
            count = long.MaxValue;
        }

        return true;
    }

    count = 0;
    return false;
}

// Replaces {name} and {name:format} with the argument at the position of the name in the key's parameter
// list, and {'text'} with text. A placeholder without a matching argument stays as written.
private static string Substitute(string text, string[]? names, object?[] args, global::System.IFormatProvider? provider)
{
    if (text.IndexOf('{') < 0)
    {
        return text;
    }

    var builder = new global::System.Text.StringBuilder(text.Length + 16);
    var index = 0;
    while (index < text.Length)
    {
        var character = text[index];
        if (character == '{')
        {
            var end = ScanBrace(text, index, out var name, out var format, out var isLiteral);
            if (end > 0)
            {
                if (isLiteral)
                {
                    builder.Append(name);
                }
                else
                {
                    var position = names == null ? -1 : global::System.Array.IndexOf(names, name);
                    if (position >= 0 && position < args.Length)
                    {
                        builder.Append(string.Format(provider, format == null ? ""{0}"" : ""{0:"" + format + ""}"", args[position]));
                    }
                    else
                    {
                        builder.Append(text, index, end - index);
                    }
                }

                index = end;
                continue;
            }
        }

        builder.Append(character);
        index++;
    }

    return builder.ToString();
}

// Reads a placeholder or literal that starts at the '{' at index. Returns the index after the closing '}',
// or -1 when the '{' does not start one.
private static int ScanBrace(string text, int index, out string? name, out string? format, out bool isLiteral)
{
    name = null;
    format = null;
    isLiteral = false;
    if (index + 1 < text.Length && text[index + 1] == '\'')
    {
        var literal = ReadLiteral(text, index, out var literalEnd);
        if (literal != null)
        {
            name = literal;
            isLiteral = true;
            return literalEnd;
        }
    }

    var position = index + 1;
    while (position < text.Length && char.IsWhiteSpace(text[position]))
    {
        position++;
    }

    var nameStart = position;
    while (position < text.Length && IsWordCharacter(text[position]))
    {
        position++;
    }

    if (position == nameStart)
    {
        return -1;
    }

    var nameEnd = position;
    while (position < text.Length && char.IsWhiteSpace(text[position]))
    {
        position++;
    }

    if (position < text.Length && text[position] == ':')
    {
        var formatStart = position + 1;
        position = formatStart;
        while (position < text.Length && text[position] != '{' && text[position] != '}')
        {
            position++;
        }

        if (position >= text.Length || text[position] != '}')
        {
            return -1;
        }

        format = text.Substring(formatStart, position - formatStart);
        name = text.Substring(nameStart, nameEnd - nameStart);
        return position + 1;
    }

    if (position < text.Length && text[position] == '}')
    {
        name = text.Substring(nameStart, nameEnd - nameStart);
        return position + 1;
    }

    return -1;
}

// Reads {'text'} at start with the vue-i18n escapes \' \\ \uXXXX and \UXXXXXX. Returns null when it is not a
// literal (invalid escape or no end).
private static string? ReadLiteral(string text, int start, out int end)
{
    var builder = new global::System.Text.StringBuilder();
    var index = start + 2;
    while (index < text.Length)
    {
        var character = text[index];
        if (character == '\'' && index + 1 < text.Length && text[index + 1] == '}')
        {
            end = index + 2;
            return builder.ToString();
        }

        if (character != '\\')
        {
            builder.Append(character);
            index++;
            continue;
        }

        if (index + 1 >= text.Length)
        {
            break;
        }

        var escape = text[index + 1];
        if (escape == '\'' || escape == '\\')
        {
            builder.Append(escape);
            index += 2;
        }
        else if (escape == 'u' || escape == 'U')
        {
            var length = escape == 'u' ? 4 : 6;
            var codePoint = ReadCodePoint(text, index + 2, length);
            if (codePoint == null)
            {
                break;
            }

            builder.Append(codePoint);
            index += 2 + length;
        }
        else
        {
            break;
        }
    }

    end = start;
    return null;
}

private static string? ReadCodePoint(string text, int start, int length)
{
    if (start + length > text.Length)
    {
        return null;
    }

    var number = 0;
    for (var index = start; index < start + length; index++)
    {
        var character = text[index];
        var digit = character >= '0' && character <= '9' ? character - '0'
            : character >= 'a' && character <= 'f' ? character - 'a' + 10
            : character >= 'A' && character <= 'F' ? character - 'A' + 10
            : -1;
        if (digit < 0)
        {
            return null;
        }

        number = (number * 16) + digit;
    }

    // Surrogates and values above U+10FFFF are not valid code points.
    if (number > 0x10FFFF || (number >= 0xD800 && number <= 0xDFFF))
    {
        return null;
    }

    return char.ConvertFromUtf32(number);
}

// Same characters as \w in a regular expression.
private static bool IsWordCharacter(char character)
{
    switch (global::System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character))
    {
        case global::System.Globalization.UnicodeCategory.UppercaseLetter:
        case global::System.Globalization.UnicodeCategory.LowercaseLetter:
        case global::System.Globalization.UnicodeCategory.TitlecaseLetter:
        case global::System.Globalization.UnicodeCategory.ModifierLetter:
        case global::System.Globalization.UnicodeCategory.OtherLetter:
        case global::System.Globalization.UnicodeCategory.NonSpacingMark:
        case global::System.Globalization.UnicodeCategory.DecimalDigitNumber:
        case global::System.Globalization.UnicodeCategory.ConnectorPunctuation:
            return true;
        default:
            return false;
    }
}";
}
