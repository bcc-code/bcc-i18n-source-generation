using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BccCode.I18N.SourceGen;

internal enum MessageTokenKind
{
    /// <summary>Plain text.</summary>
    Text,

    /// <summary><c>{name}</c> or <c>{name:format}</c>.</summary>
    Placeholder,

    /// <summary><c>{'text'}</c>: text that is never interpreted.</summary>
    Literal,

    /// <summary><c>@:key</c>, <c>@:{'key'}</c> or <c>@.modifier:key</c>.</summary>
    Link,

    /// <summary>A <c>|</c> outside of literals, placeholders and links.</summary>
    Pipe,
}

internal readonly struct MessageToken
{
    public MessageToken(MessageTokenKind kind, int start, int length, string text, string? format = null, string? modifier = null)
    {
        Kind = kind;
        Start = start;
        Length = length;
        Text = text;
        Format = format;
        Modifier = modifier;
    }

    public MessageTokenKind Kind { get; }

    public int Start { get; }

    public int Length { get; }

    /// <summary>Placeholder name, literal content or link key. For text and pipes the source text.</summary>
    public string Text { get; }

    public string? Format { get; }

    public string? Modifier { get; }
}

/// <summary>Tokenizer for the subset of the vue-i18n message syntax that the generator understands.</summary>
internal static class TranslationMessageSyntax
{
    private static readonly Regex PlaceholderRegex = new(
        @"\G\{(\s*\w+\s*)(?::([^{}]*))?\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LinkRegex = new(
        @"\G@(?:\.(lower|upper|capitalize))?:(?:\{'([^']*)'\}|([\w\-$.]+))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static List<MessageToken> Tokenize(string? value)
    {
        value ??= string.Empty;
        var tokens = new List<MessageToken>();
        var textStart = 0;
        var position = 0;

        void FlushText(int end)
        {
            if (end > textStart)
            {
                tokens.Add(new MessageToken(MessageTokenKind.Text, textStart, end - textStart, value.Substring(textStart, end - textStart)));
            }
        }

        while (position < value.Length)
        {
            var character = value[position];
            MessageToken? token = null;

            if (character == '{')
            {
                if (position + 1 < value.Length && value[position + 1] == '\'')
                {
                    var close = value.IndexOf("'}", position + 2, StringComparison.Ordinal);
                    if (close >= 0)
                    {
                        token = new MessageToken(
                            MessageTokenKind.Literal,
                            position,
                            close + 2 - position,
                            value.Substring(position + 2, close - position - 2));
                    }
                }

                if (token is null)
                {
                    var match = PlaceholderRegex.Match(value, position);
                    if (match.Success)
                    {
                        token = new MessageToken(
                            MessageTokenKind.Placeholder,
                            position,
                            match.Length,
                            match.Groups[1].Value.Trim(),
                            match.Groups[2].Success ? match.Groups[2].Value : null);
                    }
                }
            }
            else if (character == '@')
            {
                var match = LinkRegex.Match(value, position);
                if (match.Success)
                {
                    var length = match.Length;
                    string key;
                    if (match.Groups[2].Success)
                    {
                        key = match.Groups[2].Value;
                    }
                    else
                    {
                        // A trailing '.' ends the sentence and is not part of the key.
                        key = match.Groups[3].Value;
                        var trimmed = key.TrimEnd('.');
                        length -= key.Length - trimmed.Length;
                        key = trimmed;
                    }

                    if (key.Length > 0)
                    {
                        token = new MessageToken(
                            MessageTokenKind.Link,
                            position,
                            length,
                            key,
                            modifier: match.Groups[1].Success ? match.Groups[1].Value : null);
                    }
                }
            }
            else if (character == '|')
            {
                token = new MessageToken(MessageTokenKind.Pipe, position, 1, "|");
            }

            if (token is { } found)
            {
                FlushText(position);
                tokens.Add(found);
                position += found.Length;
                textStart = position;
            }
            else
            {
                position++;
            }
        }

        FlushText(value.Length);
        return tokens;
    }

    public static bool IsPlural(string? value)
    {
        if (value is null || value.IndexOf('|') < 0)
        {
            return false;
        }

        return Tokenize(value).Any(static token => token.Kind == MessageTokenKind.Pipe);
    }

    /// <summary>Splits at pipes outside of literals, placeholders and links. Parts are trimmed.</summary>
    public static string[] SplitPlural(string? value)
    {
        value ??= string.Empty;
        var parts = new List<string>();
        var start = 0;
        foreach (var token in Tokenize(value))
        {
            if (token.Kind != MessageTokenKind.Pipe)
            {
                continue;
            }

            parts.Add(value.Substring(start, token.Start - start).Trim());
            start = token.Start + 1;
        }

        parts.Add(value.Substring(start).Trim());
        return parts.ToArray();
    }

    /// <summary>Applies a link modifier to the text of a message, leaving placeholders and links untouched.</summary>
    public static string ApplyModifier(string value, string? modifier)
    {
        if (modifier is null)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var first = true;
        foreach (var token in Tokenize(value))
        {
            var raw = value.Substring(token.Start, token.Length);
            var isText = token.Kind is MessageTokenKind.Text or MessageTokenKind.Pipe;
            if (token.Kind == MessageTokenKind.Literal)
            {
                builder.Append("{'").Append(ApplyToText(token.Text, modifier, first)).Append("'}");
            }
            else if (isText)
            {
                builder.Append(ApplyToText(raw, modifier, first));
            }
            else
            {
                builder.Append(raw);
            }

            first = false;
        }

        return builder.ToString();
    }

    private static string ApplyToText(string text, string modifier, bool isFirstToken)
    {
        switch (modifier)
        {
            case "lower":
                return text.ToLowerInvariant();
            case "upper":
                return text.ToUpperInvariant();
            case "capitalize":
                // Only the very start of the message is capitalized; text after a placeholder is not.
                return isFirstToken && text.Length > 0
                    ? char.ToUpperInvariant(text[0]) + text.Substring(1)
                    : text;
            default:
                return text;
        }
    }
}

/// <summary>Resolves linked messages (<c>@:key</c>) in all translation files at generation time.</summary>
internal static class TranslationLinkResolver
{
    public static ImmutableArray<LanguageFileModel> Resolve(
        ImmutableArray<LanguageFileModel> files,
        string fallbackLanguage,
        ImmutableArray<GeneratorDiagnosticInfo>.Builder diagnostics)
    {
        if (!files.Any(static file => file.Entries.Any(static entry => entry.Value.IndexOf('@') >= 0)))
        {
            return files;
        }

        var valuesByLanguage = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in file.Entries)
            {
                values[entry.KeyPath] = entry.Value;
            }

            valuesByLanguage[file.Language] = values;
        }

        valuesByLanguage.TryGetValue(fallbackLanguage, out var fallbackValues);

        var result = ImmutableArray.CreateBuilder<LanguageFileModel>(files.Length);
        foreach (var file in files)
        {
            var languageValues = valuesByLanguage[file.Language];
            var entries = ImmutableArray.CreateBuilder<TranslationEntryModel>(file.Entries.Length);
            foreach (var entry in file.Entries)
            {
                var value = entry.Value;
                if (value.IndexOf('@') >= 0)
                {
                    var context = new Context(file, languageValues, fallbackValues, entry.KeyPath, diagnostics);
                    value = context.ExpandLinks(value, new List<string> { entry.KeyPath }, isTopLevel: true);
                }

                entries.Add(new TranslationEntryModel(entry.KeyPath, value));
            }

            result.Add(new LanguageFileModel(file.Language, file.FilePath, new EquatableArray<TranslationEntryModel>(entries.ToImmutable())));
        }

        return result.ToImmutable();
    }

    private sealed class Context
    {
        private readonly LanguageFileModel _file;
        private readonly Dictionary<string, string> _languageValues;
        private readonly Dictionary<string, string>? _fallbackValues;
        private readonly string _rootKey;
        private readonly ImmutableArray<GeneratorDiagnosticInfo>.Builder _diagnostics;
        private bool _reported;

        public Context(
            LanguageFileModel file,
            Dictionary<string, string> languageValues,
            Dictionary<string, string>? fallbackValues,
            string rootKey,
            ImmutableArray<GeneratorDiagnosticInfo>.Builder diagnostics)
        {
            _file = file;
            _languageValues = languageValues;
            _fallbackValues = fallbackValues;
            _rootKey = rootKey;
            _diagnostics = diagnostics;
        }

        public string ExpandLinks(string value, List<string> stack, bool isTopLevel)
        {
            if (value.IndexOf('@') < 0)
            {
                return value;
            }

            var builder = new StringBuilder(value.Length);
            var position = 0;
            foreach (var token in TranslationMessageSyntax.Tokenize(value))
            {
                if (token.Kind != MessageTokenKind.Link)
                {
                    continue;
                }

                builder.Append(value, position, token.Start - position);
                position = token.Start + token.Length;

                var replacement = ExpandLink(token, stack, isTopLevel);
                builder.Append(replacement ?? value.Substring(token.Start, token.Length));
            }

            builder.Append(value, position, value.Length - position);
            return builder.ToString();
        }

        private string? ExpandLink(MessageToken link, List<string> stack, bool isTopLevel)
        {
            var key = link.Text;
            if (stack.Contains(key, StringComparer.Ordinal))
            {
                // Only the key that is being resolved reports a cycle through itself. Other keys on the
                // cycle report it when they are resolved.
                if (string.Equals(key, _rootKey, StringComparison.Ordinal))
                {
                    Report($"linked message '@:{key}' creates a cycle");
                }

                return null;
            }

            if (!_languageValues.TryGetValue(key, out var target) && (_fallbackValues is null || !_fallbackValues.TryGetValue(key, out target)))
            {
                if (isTopLevel)
                {
                    Report($"linked message '@:{key}' does not match a translation key");
                }

                return null;
            }

            if (TranslationMessageSyntax.IsPlural(target))
            {
                if (isTopLevel)
                {
                    Report($"linked message '@:{key}' points to a plural message, which cannot be linked");
                }

                return null;
            }

            stack.Add(key);
            var expanded = ExpandLinks(target, stack, isTopLevel: false);
            stack.RemoveAt(stack.Count - 1);
            return TranslationMessageSyntax.ApplyModifier(expanded, link.Modifier);
        }

        private void Report(string reason)
        {
            if (_reported)
            {
                return;
            }

            _reported = true;
            _diagnostics.Add(new GeneratorDiagnosticInfo(
                TranslationGeneratorDiagnostics.InvalidLinkedMessage,
                $"Translation key '{_rootKey}' in language '{_file.Language}': {reason}. The link text is kept as written.",
                new LocationInfo(_file.FilePath)));
        }
    }
}
