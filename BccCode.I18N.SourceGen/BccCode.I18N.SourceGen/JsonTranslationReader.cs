using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BccCode.I18N.SourceGen;

internal enum JsonNodeKind
{
    Object,
    Array,
    String,
    Number,
    Boolean,
    Null,
}

internal sealed class JsonNode
{
    private static readonly IReadOnlyList<JsonProperty> NoProperties = Array.Empty<JsonProperty>();

    private JsonNode(JsonNodeKind kind, string? stringValue, IReadOnlyList<JsonProperty>? properties)
    {
        Kind = kind;
        StringValue = stringValue;
        Properties = properties ?? NoProperties;
    }

    public JsonNodeKind Kind { get; }

    /// <summary>The unescaped value when <see cref="Kind"/> is <see cref="JsonNodeKind.String"/>.</summary>
    public string? StringValue { get; }

    /// <summary>The properties in source order, duplicates included, when <see cref="Kind"/> is <see cref="JsonNodeKind.Object"/>.</summary>
    public IReadOnlyList<JsonProperty> Properties { get; }

    public static JsonNode Scalar(JsonNodeKind kind) => new(kind, null, null);

    public static JsonNode String(string value) => new(JsonNodeKind.String, value, null);

    public static JsonNode Object(List<JsonProperty> properties) => new(JsonNodeKind.Object, null, properties);
}

/// <summary>A property of a JSON object. Offset is the zero-based index of the opening quote of the name in the parsed text.</summary>
internal readonly struct JsonProperty
{
    public JsonProperty(string name, int offset, int length, JsonNode value)
    {
        Name = name;
        Offset = offset;
        Length = length;
        Value = value;
    }

    public string Name { get; }

    public int Offset { get; }

    /// <summary>Length of the quoted name as written in the source, escapes included.</summary>
    public int Length { get; }

    public JsonNode Value { get; }
}

/// <summary>A syntax error at a zero-based character offset in the parsed text.</summary>
internal sealed class JsonSyntaxException : Exception
{
    public JsonSyntaxException(string description, int offset)
        : base(description)
    {
        Description = description;
        Offset = offset;
    }

    public string Description { get; }

    public int Offset { get; }
}

/// <summary>
/// Minimal strict JSON reader. It follows the defaults of <c>JsonDocument.Parse</c>: no comments, no trailing
/// commas, one root value, a maximum depth of 64, and duplicate property names are kept. Unlike
/// <c>JsonDocument</c> it records where each property name is, and it rejects unpaired surrogate escapes
/// while parsing instead of when the string is read.
/// </summary>
internal sealed class JsonTranslationReader
{
    public const int MaxDepth = 64;

    private readonly string _text;
    private int _pos;
    private StringBuilder? _builder;

    private JsonTranslationReader(string text)
    {
        _text = text;
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            _pos = 1;
        }
    }

    /// <exception cref="JsonSyntaxException">The text is not valid JSON.</exception>
    public static JsonNode Parse(string text)
    {
        var reader = new JsonTranslationReader(text ?? string.Empty);
        reader.SkipWhitespace();
        if (reader.AtEnd)
        {
            throw reader.Error("the file does not contain a JSON value");
        }

        var root = reader.ParseValue(0);
        reader.SkipWhitespace();
        if (!reader.AtEnd)
        {
            throw reader.Error($"unexpected {reader.DescribeCurrent()} after the root value");
        }

        return root;
    }

    private bool AtEnd => _pos >= _text.Length;

    private JsonNode ParseValue(int depth)
    {
        SkipWhitespace();
        if (AtEnd)
        {
            throw Unexpected("a value");
        }

        var c = _text[_pos];
        switch (c)
        {
            case '{':
                return ParseObject(depth + 1);
            case '[':
                return ParseArray(depth + 1);
            case '"':
                return JsonNode.String(ReadString());
            case 't':
                ReadLiteral("true");
                return JsonNode.Scalar(JsonNodeKind.Boolean);
            case 'f':
                ReadLiteral("false");
                return JsonNode.Scalar(JsonNodeKind.Boolean);
            case 'n':
                ReadLiteral("null");
                return JsonNode.Scalar(JsonNodeKind.Null);
            default:
                if (c == '-' || (c >= '0' && c <= '9'))
                {
                    ReadNumber();
                    return JsonNode.Scalar(JsonNodeKind.Number);
                }

                throw Unexpected("a value");
        }
    }

    private JsonNode ParseObject(int depth)
    {
        CheckDepth(depth);
        _pos++;
        var properties = new List<JsonProperty>();

        SkipWhitespace();
        if (!AtEnd && _text[_pos] == '}')
        {
            _pos++;
            return JsonNode.Object(properties);
        }

        while (true)
        {
            SkipWhitespace();
            if (AtEnd || _text[_pos] != '"')
            {
                throw Unexpected("a property name");
            }

            var nameStart = _pos;
            var name = ReadString();
            var nameLength = _pos - nameStart;

            SkipWhitespace();
            if (AtEnd || _text[_pos] != ':')
            {
                throw Unexpected("':' after the property name");
            }

            _pos++;
            var value = ParseValue(depth);
            properties.Add(new JsonProperty(name, nameStart, nameLength, value));

            SkipWhitespace();
            if (AtEnd)
            {
                throw Unexpected("',' or '}'");
            }

            var next = _text[_pos];
            if (next == ',')
            {
                _pos++;
                SkipWhitespace();
                if (!AtEnd && _text[_pos] == '}')
                {
                    throw Error("a trailing comma is not allowed");
                }
            }
            else if (next == '}')
            {
                _pos++;
                return JsonNode.Object(properties);
            }
            else
            {
                throw Unexpected("',' or '}'");
            }
        }
    }

    private JsonNode ParseArray(int depth)
    {
        CheckDepth(depth);
        _pos++;

        SkipWhitespace();
        if (!AtEnd && _text[_pos] == ']')
        {
            _pos++;
            return JsonNode.Scalar(JsonNodeKind.Array);
        }

        while (true)
        {
            ParseValue(depth);

            SkipWhitespace();
            if (AtEnd)
            {
                throw Unexpected("',' or ']'");
            }

            var next = _text[_pos];
            if (next == ',')
            {
                _pos++;
                SkipWhitespace();
                if (!AtEnd && _text[_pos] == ']')
                {
                    throw Error("a trailing comma is not allowed");
                }
            }
            else if (next == ']')
            {
                _pos++;
                return JsonNode.Scalar(JsonNodeKind.Array);
            }
            else
            {
                throw Unexpected("',' or ']'");
            }
        }
    }

    private void CheckDepth(int depth)
    {
        if (depth > MaxDepth)
        {
            throw Error($"the maximum nesting depth of {MaxDepth} is exceeded");
        }
    }

    private string ReadString()
    {
        var startOffset = _pos;
        _pos++; // opening quote
        var segmentStart = _pos;
        StringBuilder? builder = null;

        while (true)
        {
            if (AtEnd)
            {
                throw new JsonSyntaxException("the string is not terminated", startOffset);
            }

            var c = _text[_pos];
            if (c == '"')
            {
                string result;
                if (builder is null)
                {
                    result = _text.Substring(segmentStart, _pos - segmentStart);
                }
                else
                {
                    builder.Append(_text, segmentStart, _pos - segmentStart);
                    result = builder.ToString();
                }

                _pos++;
                return result;
            }

            if (c < ' ')
            {
                throw Error($"a control character (U+{(int)c:X4}) in a string must be escaped");
            }

            if (char.IsHighSurrogate(c))
            {
                if (_pos + 1 >= _text.Length || !char.IsLowSurrogate(_text[_pos + 1]))
                {
                    throw Error("the string contains an unpaired high surrogate character");
                }

                _pos += 2;
                continue;
            }

            if (char.IsLowSurrogate(c))
            {
                throw Error("the string contains an unpaired low surrogate character");
            }

            if (c != '\\')
            {
                _pos++;
                continue;
            }

            if (builder is null)
            {
                builder = _builder ??= new StringBuilder();
                builder.Clear();
            }

            builder.Append(_text, segmentStart, _pos - segmentStart);
            ReadEscape(builder);
            segmentStart = _pos;
        }
    }

    /// <summary>Reads an escape sequence starting at the backslash and appends the decoded text.</summary>
    private void ReadEscape(StringBuilder builder)
    {
        if (_pos + 1 >= _text.Length)
        {
            _pos++;
            throw Error("the string is not terminated");
        }

        var escape = _text[_pos + 1];
        switch (escape)
        {
            case '"': builder.Append('"'); break;
            case '\\': builder.Append('\\'); break;
            case '/': builder.Append('/'); break;
            case 'b': builder.Append('\b'); break;
            case 'f': builder.Append('\f'); break;
            case 'n': builder.Append('\n'); break;
            case 'r': builder.Append('\r'); break;
            case 't': builder.Append('\t'); break;
            case 'u':
                var unit = ReadHex4(_pos + 2);
                if (unit >= 0xDC00 && unit <= 0xDFFF)
                {
                    throw Error("the escape sequence is an unpaired low surrogate");
                }

                if (unit >= 0xD800 && unit <= 0xDBFF)
                {
                    if (_pos + 7 >= _text.Length || _text[_pos + 6] != '\\' || _text[_pos + 7] != 'u')
                    {
                        throw Error("the escape sequence is an unpaired high surrogate");
                    }

                    var low = ReadHex4(_pos + 8);
                    if (low < 0xDC00 || low > 0xDFFF)
                    {
                        throw Error("the escape sequence is an unpaired high surrogate");
                    }

                    builder.Append((char)unit).Append((char)low);
                    _pos += 12;
                    return;
                }

                builder.Append((char)unit);
                _pos += 6;
                return;
            default:
                throw Error($"'\\{escape}' is not a valid escape sequence");
        }

        _pos += 2;
    }

    /// <summary>Reads four hex digits at <paramref name="start"/>. Errors point at the backslash that starts the escape.</summary>
    private int ReadHex4(int start)
    {
        // The escape starts at _pos - for a second \u of a surrogate pair this reports the pair start, which is fine.
        if (start + 4 > _text.Length)
        {
            throw Error("the \\u escape sequence needs four hex digits");
        }

        var value = 0;
        for (var i = start; i < start + 4; i++)
        {
            var c = _text[i];
            int digit;
            if (c >= '0' && c <= '9')
            {
                digit = c - '0';
            }
            else if (c >= 'a' && c <= 'f')
            {
                digit = c - 'a' + 10;
            }
            else if (c >= 'A' && c <= 'F')
            {
                digit = c - 'A' + 10;
            }
            else
            {
                throw Error("the \\u escape sequence needs four hex digits");
            }

            value = (value << 4) | digit;
        }

        return value;
    }

    private void ReadLiteral(string literal)
    {
        if (string.CompareOrdinal(_text, _pos, literal, 0, literal.Length) != 0)
        {
            throw Error($"invalid value, expected '{literal}'");
        }

        _pos += literal.Length;
    }

    private void ReadNumber()
    {
        if (_text[_pos] == '-')
        {
            _pos++;
        }

        if (AtEnd || !IsDigit(_text[_pos]))
        {
            throw Error("invalid number, expected a digit");
        }

        if (_text[_pos] == '0')
        {
            _pos++;
        }
        else
        {
            SkipDigits();
        }

        if (!AtEnd && _text[_pos] == '.')
        {
            _pos++;
            if (AtEnd || !IsDigit(_text[_pos]))
            {
                throw Error("invalid number, expected a digit after the decimal point");
            }

            SkipDigits();
        }

        if (!AtEnd && (_text[_pos] == 'e' || _text[_pos] == 'E'))
        {
            _pos++;
            if (!AtEnd && (_text[_pos] == '+' || _text[_pos] == '-'))
            {
                _pos++;
            }

            if (AtEnd || !IsDigit(_text[_pos]))
            {
                throw Error("invalid number, expected a digit in the exponent");
            }

            SkipDigits();
        }
    }

    private void SkipDigits()
    {
        while (!AtEnd && IsDigit(_text[_pos]))
        {
            _pos++;
        }
    }

    private static bool IsDigit(char c) => c >= '0' && c <= '9';

    /// <summary>Skips space, tab, CR and LF, the only whitespace JSON allows.</summary>
    private void SkipWhitespace()
    {
        while (_pos < _text.Length)
        {
            var c = _text[_pos];
            if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
            {
                break;
            }

            _pos++;
        }
    }

    private JsonSyntaxException Error(string description) => new(description, _pos);

    private JsonSyntaxException Unexpected(string expected)
    {
        if (!AtEnd && _text[_pos] == '/')
        {
            return Error("comments are not allowed");
        }

        return Error($"unexpected {DescribeCurrent()}, expected {expected}");
    }

    private string DescribeCurrent()
    {
        if (AtEnd)
        {
            return "end of file";
        }

        var c = _text[_pos];
        return c < ' ' || char.IsSurrogate(c)
            ? "character U+" + ((int)c).ToString("X4", CultureInfo.InvariantCulture)
            : "'" + c + "'";
    }
}
