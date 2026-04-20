using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace BccCode.I18N.SourceGen;

internal static class TranslationGeneratorDefaults
{
    public const string DefaultFallbackLanguage = "no";
}

internal static class TranslationGeneratorTrackingNames
{
    public const string ParsedTranslationFile = nameof(ParsedTranslationFile);
    public const string FallbackLanguage = nameof(FallbackLanguage);
    public const string TranslationModelSet = nameof(TranslationModelSet);
}

internal static class TranslationGeneratorDiagnostics
{
    private const string Category = "BccCode.I18N.SourceGen";

    public static readonly DiagnosticDescriptor InvalidJson = new(
        id: "I18N001",
        title: "Invalid translation file",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RootMustBeObject = new(
        id: "I18N002",
        title: "Translation file must use an object root",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedValue = new(
        id: "I18N003",
        title: "Unsupported translation value",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingFallbackLanguage = new(
        id: "I18N004",
        title: "Fallback language file missing",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateLanguage = new(
        id: "I18N005",
        title: "Duplicate translation language",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PlaceholderMismatch = new(
        id: "I18N006",
        title: "Translation placeholders do not match fallback language",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}

internal readonly struct TranslationEntryModel : IEquatable<TranslationEntryModel>
{
    public TranslationEntryModel(string keyPath, string value)
    {
        KeyPath = keyPath ?? string.Empty;
        Value = value ?? string.Empty;
    }

    public string KeyPath { get; }

    public string Value { get; }

    public bool Equals(TranslationEntryModel other) =>
        string.Equals(KeyPath, other.KeyPath, StringComparison.Ordinal) &&
        string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is TranslationEntryModel other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return ((KeyPath != null ? StringComparer.Ordinal.GetHashCode(KeyPath) : 0) * 397) ^
                   (Value != null ? StringComparer.Ordinal.GetHashCode(Value) : 0);
        }
    }
}

internal readonly struct LocationInfo : IEquatable<LocationInfo>
{
    public LocationInfo(string filePath)
    {
        FilePath = filePath ?? string.Empty;
    }

    public string FilePath { get; }

    public Location ToLocation()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            return Location.None;
        }

        return Location.Create(
            FilePath,
            new TextSpan(0, 0),
            new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 0)));
    }

    public bool Equals(LocationInfo other) => string.Equals(FilePath, other.FilePath, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is LocationInfo other && Equals(other);

    public override int GetHashCode() => FilePath != null ? StringComparer.Ordinal.GetHashCode(FilePath) : 0;
}

internal readonly struct GeneratorDiagnosticInfo : IEquatable<GeneratorDiagnosticInfo>
{
    public GeneratorDiagnosticInfo(DiagnosticDescriptor descriptor, string message, LocationInfo? location = null)
    {
        Descriptor = descriptor;
        Message = message ?? string.Empty;
        Location = location;
    }

    public DiagnosticDescriptor Descriptor { get; }

    public string Message { get; }

    public LocationInfo? Location { get; }

    public Diagnostic ToDiagnostic() => Diagnostic.Create(Descriptor, Location?.ToLocation(), Message);

    public bool Equals(GeneratorDiagnosticInfo other) =>
        string.Equals(Descriptor.Id, other.Descriptor.Id, StringComparison.Ordinal) &&
        string.Equals(Message, other.Message, StringComparison.Ordinal) &&
        Nullable.Equals(Location, other.Location);

    public override bool Equals(object? obj) => obj is GeneratorDiagnosticInfo other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = Descriptor.Id != null ? StringComparer.Ordinal.GetHashCode(Descriptor.Id) : 0;
            hashCode = (hashCode * 397) ^ (Message != null ? StringComparer.Ordinal.GetHashCode(Message) : 0);
            hashCode = (hashCode * 397) ^ (Location?.GetHashCode() ?? 0);
            return hashCode;
        }
    }
}

internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
{
    private readonly ImmutableArray<T> _array;

    public EquatableArray(ImmutableArray<T> array)
    {
        _array = array.IsDefault ? ImmutableArray<T>.Empty : array;
    }

    public EquatableArray(IEnumerable<T> values)
        : this(values == null ? ImmutableArray<T>.Empty : values.ToImmutableArray())
    {
    }

    public ImmutableArray<T> Array => _array.IsDefault ? ImmutableArray<T>.Empty : _array;

    public int Length => Array.Length;

    public T this[int index] => Array[index];

    public bool Equals(EquatableArray<T> other)
    {
        if (Length != other.Length)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (var index = 0; index < Length; index++)
        {
            if (!comparer.Equals(this[index], other[index]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = 17;
            foreach (var item in Array)
            {
                hashCode = (hashCode * 31) + (item?.GetHashCode() ?? 0);
            }

            return hashCode;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Array).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal readonly struct LanguageFileModel : IEquatable<LanguageFileModel>
{
    public LanguageFileModel(string language, string filePath, EquatableArray<TranslationEntryModel> entries)
    {
        Language = language ?? string.Empty;
        FilePath = filePath ?? string.Empty;
        Entries = entries;
    }

    public string Language { get; }

    public string FilePath { get; }

    public EquatableArray<TranslationEntryModel> Entries { get; }

    public bool Equals(LanguageFileModel other) =>
        string.Equals(Language, other.Language, StringComparison.Ordinal) &&
        string.Equals(FilePath, other.FilePath, StringComparison.Ordinal) &&
        Entries.Equals(other.Entries);

    public override bool Equals(object? obj) => obj is LanguageFileModel other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = Language != null ? StringComparer.Ordinal.GetHashCode(Language) : 0;
            hashCode = (hashCode * 397) ^ (FilePath != null ? StringComparer.Ordinal.GetHashCode(FilePath) : 0);
            hashCode = (hashCode * 397) ^ Entries.GetHashCode();
            return hashCode;
        }
    }
}

internal readonly struct LanguageFileParseResult : IEquatable<LanguageFileParseResult>
{
    private LanguageFileParseResult(bool hasFile, LanguageFileModel file, EquatableArray<GeneratorDiagnosticInfo> diagnostics)
    {
        HasFile = hasFile;
        File = file;
        Diagnostics = diagnostics;
    }

    public bool HasFile { get; }

    public LanguageFileModel File { get; }

    public EquatableArray<GeneratorDiagnosticInfo> Diagnostics { get; }

    public static LanguageFileParseResult Success(LanguageFileModel file, EquatableArray<GeneratorDiagnosticInfo> diagnostics) =>
        new(true, file, diagnostics);

    public static LanguageFileParseResult Failure(EquatableArray<GeneratorDiagnosticInfo> diagnostics) =>
        new(false, default, diagnostics);

    public bool Equals(LanguageFileParseResult other) =>
        HasFile == other.HasFile &&
        File.Equals(other.File) &&
        Diagnostics.Equals(other.Diagnostics);

    public override bool Equals(object? obj) => obj is LanguageFileParseResult other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = HasFile.GetHashCode();
            hashCode = (hashCode * 397) ^ File.GetHashCode();
            hashCode = (hashCode * 397) ^ Diagnostics.GetHashCode();
            return hashCode;
        }
    }
}

internal readonly struct TranslationModelSet : IEquatable<TranslationModelSet>
{
    public TranslationModelSet(
        string fallbackLanguage,
        EquatableArray<LanguageFileModel> files,
        EquatableArray<GeneratorDiagnosticInfo> diagnostics,
        bool hasFallbackFile,
        LanguageFileModel fallbackFile)
    {
        FallbackLanguage = fallbackLanguage ?? TranslationGeneratorDefaults.DefaultFallbackLanguage;
        Files = files;
        Diagnostics = diagnostics;
        HasFallbackFile = hasFallbackFile;
        FallbackFile = fallbackFile;
    }

    public string FallbackLanguage { get; }

    public EquatableArray<LanguageFileModel> Files { get; }

    public EquatableArray<GeneratorDiagnosticInfo> Diagnostics { get; }

    public bool HasFallbackFile { get; }

    public LanguageFileModel FallbackFile { get; }

    public bool Equals(TranslationModelSet other) =>
        string.Equals(FallbackLanguage, other.FallbackLanguage, StringComparison.Ordinal) &&
        Files.Equals(other.Files) &&
        Diagnostics.Equals(other.Diagnostics) &&
        HasFallbackFile == other.HasFallbackFile &&
        FallbackFile.Equals(other.FallbackFile);

    public override bool Equals(object? obj) => obj is TranslationModelSet other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = FallbackLanguage != null ? StringComparer.Ordinal.GetHashCode(FallbackLanguage) : 0;
            hashCode = (hashCode * 397) ^ Files.GetHashCode();
            hashCode = (hashCode * 397) ^ Diagnostics.GetHashCode();
            hashCode = (hashCode * 397) ^ HasFallbackFile.GetHashCode();
            hashCode = (hashCode * 397) ^ FallbackFile.GetHashCode();
            return hashCode;
        }
    }
}

internal static class TranslationModelSetFactory
{
    public static TranslationModelSet Create(ImmutableArray<LanguageFileParseResult> parseResults, string fallbackLanguage)
    {
        var normalizedFallbackLanguage = NormalizeLanguage(fallbackLanguage);
        var diagnosticBuilder = ImmutableArray.CreateBuilder<GeneratorDiagnosticInfo>();
        var validFiles = new List<LanguageFileModel>();

        foreach (var parseResult in parseResults)
        {
            diagnosticBuilder.AddRange(parseResult.Diagnostics.Array);
            if (parseResult.HasFile)
            {
                validFiles.Add(parseResult.File);
            }
        }

        var uniqueFiles = validFiles
            .OrderBy(static file => file.Language, StringComparer.Ordinal)
            .ThenBy(static file => file.FilePath, StringComparer.Ordinal)
            .GroupBy(static file => file.Language, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var orderedFiles = group.OrderBy(static file => file.FilePath, StringComparer.Ordinal).ToArray();
                if (orderedFiles.Length > 1)
                {
                    foreach (var duplicateFile in orderedFiles)
                    {
                        diagnosticBuilder.Add(new GeneratorDiagnosticInfo(
                            TranslationGeneratorDiagnostics.DuplicateLanguage,
                            $"Found multiple translation files for language '{duplicateFile.Language}'. Keep a single JSON file per language.",
                            new LocationInfo(duplicateFile.FilePath)));
                    }
                }

                return orderedFiles[0];
            })
            .OrderBy(static file => file.Language, StringComparer.Ordinal)
            .ToImmutableArray();

        var hasFallbackFile = false;
        LanguageFileModel fallbackFile = default;

        foreach (var file in uniqueFiles)
        {
            if (!string.Equals(file.Language, normalizedFallbackLanguage, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            hasFallbackFile = true;
            fallbackFile = file;
            break;
        }

        if (!hasFallbackFile)
        {
            diagnosticBuilder.Add(new GeneratorDiagnosticInfo(
                TranslationGeneratorDiagnostics.MissingFallbackLanguage,
                $"Fallback language '{normalizedFallbackLanguage}' was not found among the AdditionalFiles JSON translations."));
        }

        return new TranslationModelSet(
            normalizedFallbackLanguage,
            new EquatableArray<LanguageFileModel>(uniqueFiles),
            new EquatableArray<GeneratorDiagnosticInfo>(diagnosticBuilder.ToImmutable()),
            hasFallbackFile,
            fallbackFile);
    }

    private static string NormalizeLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return TranslationGeneratorDefaults.DefaultFallbackLanguage;
        }

        return language.Trim().ToLowerInvariant();
    }
}

internal static class TranslationFileParser
{
    public static LanguageFileParseResult Parse(AdditionalText additionalText, CancellationToken cancellationToken)
    {
        var filePath = additionalText.Path ?? string.Empty;
        var sourceText = additionalText.GetText(cancellationToken);
        if (sourceText is null)
        {
            return LanguageFileParseResult.Failure(new EquatableArray<GeneratorDiagnosticInfo>(
                ImmutableArray.Create(new GeneratorDiagnosticInfo(
                    TranslationGeneratorDiagnostics.InvalidJson,
                    $"Could not read translation file '{filePath}'.",
                    new LocationInfo(filePath)))));
        }

        try
        {
            using var document = JsonDocument.Parse(sourceText.ToString());
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return LanguageFileParseResult.Failure(new EquatableArray<GeneratorDiagnosticInfo>(
                    ImmutableArray.Create(new GeneratorDiagnosticInfo(
                        TranslationGeneratorDiagnostics.RootMustBeObject,
                        $"Translation file '{filePath}' must contain a JSON object at the root.",
                        new LocationInfo(filePath)))));
            }

            var entries = ImmutableArray.CreateBuilder<TranslationEntryModel>();
            var diagnostics = ImmutableArray.CreateBuilder<GeneratorDiagnosticInfo>();
            Flatten(document.RootElement, string.Empty, filePath, entries, diagnostics);

            var normalizedEntries = entries
                .OrderBy(static entry => entry.KeyPath, StringComparer.Ordinal)
                .ToImmutableArray();

            var language = Path.GetFileNameWithoutExtension(filePath)?.Trim().ToLowerInvariant() ?? string.Empty;
            var file = new LanguageFileModel(language, filePath, new EquatableArray<TranslationEntryModel>(normalizedEntries));
            return LanguageFileParseResult.Success(file, new EquatableArray<GeneratorDiagnosticInfo>(diagnostics.ToImmutable()));
        }
        catch (JsonException exception)
        {
            return LanguageFileParseResult.Failure(new EquatableArray<GeneratorDiagnosticInfo>(
                ImmutableArray.Create(new GeneratorDiagnosticInfo(
                    TranslationGeneratorDiagnostics.InvalidJson,
                    $"Invalid JSON in translation file '{filePath}': {exception.Message}",
                    new LocationInfo(filePath)))));
        }
    }

    public static string GetFallbackLanguage(AnalyzerConfigOptionsProvider optionsProvider)
    {
        optionsProvider.GlobalOptions.TryGetValue("build_property.FallbackLanguage", out var fallbackLanguage);
        return string.IsNullOrWhiteSpace(fallbackLanguage)
            ? TranslationGeneratorDefaults.DefaultFallbackLanguage
            : fallbackLanguage.Trim().ToLowerInvariant();
    }

    private static void Flatten(
        JsonElement element,
        string outerPath,
        string filePath,
        ImmutableArray<TranslationEntryModel>.Builder entries,
        ImmutableArray<GeneratorDiagnosticInfo>.Builder diagnostics)
    {
        foreach (var property in element.EnumerateObject())
        {
            var keyPath = string.IsNullOrEmpty(outerPath) ? property.Name : outerPath + "." + property.Name;
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String:
                    entries.Add(new TranslationEntryModel(keyPath, property.Value.GetString() ?? string.Empty));
                    break;

                case JsonValueKind.Object:
                    Flatten(property.Value, keyPath, filePath, entries, diagnostics);
                    break;

                default:
                    diagnostics.Add(new GeneratorDiagnosticInfo(
                        TranslationGeneratorDiagnostics.UnsupportedValue,
                        $"Translation key '{keyPath}' in '{filePath}' must contain a string value or nested object.",
                        new LocationInfo(filePath)));
                    break;
            }
        }
    }
}

internal static class TranslationGeneratorUtilities
{
    private static readonly Regex PlaceholderRegex = new(@"\{(\s*\w+\s*)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsJsonFile(AdditionalText file) =>
        file.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    public static void ReportDiagnostics(SourceProductionContext context, EquatableArray<GeneratorDiagnosticInfo> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            context.ReportDiagnostic(diagnostic.ToDiagnostic());
        }
    }

    public static string SanitizeName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "_";
        }

        var builder = new StringBuilder();
        if (!SyntaxFacts.IsIdentifierStartCharacter(name[0]))
        {
            builder.Append('_');
        }

        if (SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None)
        {
            builder.Append('@');
        }

        foreach (var character in name)
        {
            builder.Append(SyntaxFacts.IsIdentifierPartCharacter(character) ? character : '_');
        }

        return builder.ToString();
    }

    public static string EscapeXmlDoc(string text) =>
        (text ?? string.Empty)
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\r", string.Empty)
        .Replace("\n", "\\n");

    public static string ToCSharpStringLiteral(string value)
    {
        var builder = new StringBuilder(value?.Length ?? 0 + 2);
        builder.Append('"');

        foreach (var character in value ?? string.Empty)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\0':
                    builder.Append("\\0");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    public static string[] GetParameterNames(string value, bool isPlural)
    {
        var matches = PlaceholderRegex.Matches(value ?? string.Empty);
        var parameters = new List<string>();
        foreach (Match match in matches)
        {
            var parameter = match.Groups[1].Value.Trim();
            if (parameters.Contains(parameter, StringComparer.Ordinal))
            {
                continue;
            }

            parameters.Add(parameter);
        }

        if (isPlural && parameters.Count == 0)
        {
            parameters.Add("count");
        }

        return parameters.ToArray();
    }

    public static string[] SplitPluralParts(string value) =>
        (value ?? string.Empty)
        .Split('|')
        .Select(static part => part.Trim())
        .ToArray();

    public static bool TryCreateFormatExpression(string value, string[] parameterNames, out string expression)
    {
        value ??= string.Empty;

        var formatBuilder = new StringBuilder();
        var currentIndex = 0;
        foreach (Match match in PlaceholderRegex.Matches(value))
        {
            AppendEscapedFormatText(formatBuilder, value.Substring(currentIndex, match.Index - currentIndex));

            var parameterName = match.Groups[1].Value.Trim();
            var parameterIndex = Array.IndexOf(parameterNames, parameterName);
            if (parameterIndex < 0)
            {
                expression = string.Empty;
                return false;
            }

            formatBuilder.Append('{').Append(parameterIndex).Append('}');
            currentIndex = match.Index + match.Length;
        }

        AppendEscapedFormatText(formatBuilder, value.Substring(currentIndex));

        var literal = ToCSharpStringLiteral(formatBuilder.ToString());
        if (parameterNames.Length == 0 || value.IndexOf('{') < 0)
        {
            expression = literal;
            return true;
        }

        expression = $"string.Format(CultureInfo.CurrentCulture, {literal}, {string.Join(", ", parameterNames)})";
        return true;
    }

    public static string[] GetUnsupportedPlaceholders(string value, string[] parameterNames)
    {
        var unsupported = new List<string>();
        foreach (Match match in PlaceholderRegex.Matches(value ?? string.Empty))
        {
            var parameterName = match.Groups[1].Value.Trim();
            if (Array.IndexOf(parameterNames, parameterName) >= 0 || unsupported.Contains(parameterName, StringComparer.Ordinal))
            {
                continue;
            }

            unsupported.Add(parameterName);
        }

        return unsupported.ToArray();
    }

    private static void AppendEscapedFormatText(StringBuilder builder, string segment)
    {
        foreach (var character in segment)
        {
            if (character == '{' || character == '}')
            {
                builder.Append(character).Append(character);
            }
            else
            {
                builder.Append(character);
            }
        }
    }
}

internal sealed class TranslationNode
{
    public TranslationNode(string name)
    {
        Name = name;
        Children = new SortedDictionary<string, TranslationNode>(StringComparer.Ordinal);
    }

    public string Name { get; }

    public string? KeyPath { get; set; }

    public string? Value { get; set; }

    public SortedDictionary<string, TranslationNode> Children { get; }
}

internal static class TranslationTreeBuilder
{
    public static TranslationNode BuildTree(EquatableArray<TranslationEntryModel> entries)
    {
        var root = new TranslationNode(string.Empty);

        foreach (var entry in entries)
        {
            var currentNode = root;
            foreach (var segment in entry.KeyPath.Split('.'))
            {
                if (!currentNode.Children.TryGetValue(segment, out var childNode))
                {
                    childNode = new TranslationNode(segment);
                    currentNode.Children.Add(segment, childNode);
                }

                currentNode = childNode;
            }

            currentNode.KeyPath = entry.KeyPath;
            currentNode.Value = entry.Value;
        }

        return root;
    }
}