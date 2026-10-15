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
    public const string DefaultFallbackLanguage = "en";
}

internal readonly struct TranslationGeneratorOptions : IEquatable<TranslationGeneratorOptions>
{
    public TranslationGeneratorOptions(string fallbackLanguage, string generatedNamespace, string languageAliases = "")
    {
        LanguageAliases = languageAliases?.Trim() ?? string.Empty;
        FallbackLanguage = string.IsNullOrWhiteSpace(fallbackLanguage)
            ? TranslationGeneratorDefaults.DefaultFallbackLanguage
            : fallbackLanguage.Trim().ToLowerInvariant();
        GeneratedNamespace = generatedNamespace?.Trim() ?? string.Empty;
    }

    public string FallbackLanguage { get; }

    public string GeneratedNamespace { get; }

    /// <summary>The raw value of the I18NLanguageAliases property, e.g. "nb=no;nn=no".</summary>
    public string LanguageAliases { get; }

    public bool HasGeneratedNamespace => !string.IsNullOrWhiteSpace(GeneratedNamespace);

    public bool Equals(TranslationGeneratorOptions other) =>
        string.Equals(FallbackLanguage, other.FallbackLanguage, StringComparison.Ordinal) &&
        string.Equals(GeneratedNamespace, other.GeneratedNamespace, StringComparison.Ordinal) &&
        string.Equals(LanguageAliases, other.LanguageAliases, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is TranslationGeneratorOptions other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = FallbackLanguage != null ? StringComparer.Ordinal.GetHashCode(FallbackLanguage) : 0;
            hashCode = (hashCode * 397) ^ (GeneratedNamespace != null ? StringComparer.Ordinal.GetHashCode(GeneratedNamespace) : 0);
            hashCode = (hashCode * 397) ^ (LanguageAliases != null ? StringComparer.Ordinal.GetHashCode(LanguageAliases) : 0);
            return hashCode;
        }
    }
}

internal static class TranslationGeneratorTrackingNames
{
    public const string TranslationFileCandidate = nameof(TranslationFileCandidate);
    public const string AnyFileMarked = nameof(AnyFileMarked);
    public const string ParsedTranslationFile = nameof(ParsedTranslationFile);
    public const string FallbackLanguage = nameof(FallbackLanguage);
    public const string TranslationModelSet = nameof(TranslationModelSet);
    public const string ModelSetDiagnostics = nameof(ModelSetDiagnostics);
    public const string LanguageKeysModel = nameof(LanguageKeysModel);
    public const string StringLocalizerSupport = nameof(StringLocalizerSupport);
    public const string GeneratedNamespace = nameof(GeneratedNamespace);
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

    public static readonly DiagnosticDescriptor MissingKey = new(
        id: "I18N007",
        title: "Translation key missing from language",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ExtraKey = new(
        id: "I18N008",
        title: "Translation key not in fallback language",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidLinkedMessage = new(
        id: "I18N010",
        title: "Invalid linked message",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor LanguageAliasTargetMissing = new(
        id: "I18N009",
        title: "Language alias target missing",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}

internal readonly struct LanguageAliasModel : IEquatable<LanguageAliasModel>
{
    public LanguageAliasModel(string source, string target)
    {
        Source = source;
        Target = target;
    }

    public string Source { get; }

    public string Target { get; }

    public bool Equals(LanguageAliasModel other) =>
        string.Equals(Source, other.Source, StringComparison.Ordinal) &&
        string.Equals(Target, other.Target, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is LanguageAliasModel other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return ((Source != null ? StringComparer.Ordinal.GetHashCode(Source) : 0) * 397) ^
                   (Target != null ? StringComparer.Ordinal.GetHashCode(Target) : 0);
        }
    }
}

internal static class LanguageAliasParser
{
    /// <summary>Aliases that apply when the project does not override them. Ignored silently when the target has no file.</summary>
    public static readonly (string Source, string Target)[] Defaults =
    {
        ("nb", "no"),
        ("nn", "no"),
        ("no", "nb"),
    };

    /// <summary>
    /// Parses "nb=no;nn=no". Names are trimmed and lowercased, empty entries are ignored and the last
    /// entry for a name wins. Entries without '=' (or with an empty side) are returned as malformed.
    /// </summary>
    public static Dictionary<string, string> Parse(string value, List<string> malformedEntries)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawEntry in (value ?? string.Empty).Split(';'))
        {
            var entry = rawEntry.Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            var separator = entry.IndexOf('=');
            var source = separator < 0 ? string.Empty : entry.Substring(0, separator).Trim().ToLowerInvariant();
            var target = separator < 0 ? string.Empty : entry.Substring(separator + 1).Trim().ToLowerInvariant();
            if (source.Length == 0 || target.Length == 0)
            {
                malformedEntries.Add(entry);
                continue;
            }

            result[source] = target;
        }

        return result;
    }
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

internal readonly struct LanguageKeysModel : IEquatable<LanguageKeysModel>
{
    public LanguageKeysModel(string generatedNamespace, EquatableArray<TranslationEntryModel> entries)
    {
        GeneratedNamespace = generatedNamespace ?? string.Empty;
        Entries = entries;
    }

    public string GeneratedNamespace { get; }

    public EquatableArray<TranslationEntryModel> Entries { get; }

    public bool Equals(LanguageKeysModel other) =>
        string.Equals(GeneratedNamespace, other.GeneratedNamespace, StringComparison.Ordinal) &&
        Entries.Equals(other.Entries);

    public override bool Equals(object? obj) => obj is LanguageKeysModel other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (StringComparer.Ordinal.GetHashCode(GeneratedNamespace) * 397) ^ Entries.GetHashCode();
        }
    }
}

internal readonly struct TranslationModelSet : IEquatable<TranslationModelSet>
{
    public TranslationModelSet(
        TranslationGeneratorOptions options,
        EquatableArray<LanguageFileModel> files,
        EquatableArray<GeneratorDiagnosticInfo> diagnostics,
        bool hasFallbackFile,
        LanguageFileModel fallbackFile,
        EquatableArray<LanguageAliasModel> aliases)
    {
        Aliases = aliases;
        Options = options;
        Files = files;
        Diagnostics = diagnostics;
        HasFallbackFile = hasFallbackFile;
        FallbackFile = fallbackFile;
    }

    public TranslationGeneratorOptions Options { get; }

    public string FallbackLanguage => Options.FallbackLanguage;

    public string GeneratedNamespace => Options.GeneratedNamespace;

    public bool HasGeneratedNamespace => Options.HasGeneratedNamespace;

    public EquatableArray<LanguageFileModel> Files { get; }

    public EquatableArray<GeneratorDiagnosticInfo> Diagnostics { get; }

    public bool HasFallbackFile { get; }

    public LanguageFileModel FallbackFile { get; }

    /// <summary>Effective aliases: the target has a file and the alias name has no file of its own.</summary>
    public EquatableArray<LanguageAliasModel> Aliases { get; }

    public bool Equals(TranslationModelSet other) =>
        Aliases.Equals(other.Aliases) &&
        Options.Equals(other.Options) &&
        Files.Equals(other.Files) &&
        Diagnostics.Equals(other.Diagnostics) &&
        HasFallbackFile == other.HasFallbackFile &&
        FallbackFile.Equals(other.FallbackFile);

    public override bool Equals(object? obj) => obj is TranslationModelSet other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = Options.GetHashCode();
            hashCode = (hashCode * 397) ^ Aliases.GetHashCode();
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
    public static TranslationModelSet Create(ImmutableArray<LanguageFileParseResult> parseResults, TranslationGeneratorOptions options)
    {
        var normalizedFallbackLanguage = NormalizeLanguage(options.FallbackLanguage);
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

        // Linked messages are resolved here so that Language, LocalizedLanguage and LanguageStrings all see the final text.
        uniqueFiles = TranslationLinkResolver.Resolve(uniqueFiles, normalizedFallbackLanguage, diagnosticBuilder);

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

        var aliases = ResolveAliases(options.LanguageAliases, uniqueFiles, diagnosticBuilder);

        if (hasFallbackFile)
        {
            AddKeyMismatchDiagnostics(diagnosticBuilder, uniqueFiles, fallbackFile, normalizedFallbackLanguage);
        }

        return new TranslationModelSet(
            new TranslationGeneratorOptions(normalizedFallbackLanguage, options.GeneratedNamespace, options.LanguageAliases),
            new EquatableArray<LanguageFileModel>(uniqueFiles),
            new EquatableArray<GeneratorDiagnosticInfo>(diagnosticBuilder.ToImmutable()),
            hasFallbackFile,
            fallbackFile,
            aliases);
    }

    private static EquatableArray<LanguageAliasModel> ResolveAliases(
        string userAliases,
        ImmutableArray<LanguageFileModel> files,
        ImmutableArray<GeneratorDiagnosticInfo>.Builder diagnostics)
    {
        var languages = new HashSet<string>(files.Select(static file => file.Language), StringComparer.OrdinalIgnoreCase);

        var malformedEntries = new List<string>();
        var user = LanguageAliasParser.Parse(userAliases, malformedEntries);
        foreach (var entry in malformedEntries)
        {
            diagnostics.Add(new GeneratorDiagnosticInfo(
                TranslationGeneratorDiagnostics.LanguageAliasTargetMissing,
                $"Language alias '{entry}' in I18NLanguageAliases is malformed. Use the format 'source=target', e.g. 'nb=no;nn=no'."));
        }

        var effective = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (source, target) in LanguageAliasParser.Defaults)
        {
            effective[source] = target;
        }

        foreach (var pair in user)
        {
            effective[pair.Key] = pair.Value;
            if (!languages.Contains(pair.Value))
            {
                diagnostics.Add(new GeneratorDiagnosticInfo(
                    TranslationGeneratorDiagnostics.LanguageAliasTargetMissing,
                    $"Language alias '{pair.Key}={pair.Value}' in I18NLanguageAliases points to language '{pair.Value}', which has no translation file."));
            }
        }

        return new EquatableArray<LanguageAliasModel>(effective
            .Where(pair => languages.Contains(pair.Value) && !languages.Contains(pair.Key))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new LanguageAliasModel(pair.Key, pair.Value))
            .ToImmutableArray());
    }

    private const int MaxListedKeys = 10;

    private static void AddKeyMismatchDiagnostics(
        ImmutableArray<GeneratorDiagnosticInfo>.Builder diagnostics,
        ImmutableArray<LanguageFileModel> files,
        LanguageFileModel fallbackFile,
        string fallbackLanguage)
    {
        var fallbackKeys = new HashSet<string>(fallbackFile.Entries.Select(static entry => entry.KeyPath), StringComparer.Ordinal);

        foreach (var file in files)
        {
            if (string.Equals(file.Language, fallbackLanguage, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileKeys = new HashSet<string>(file.Entries.Select(static entry => entry.KeyPath), StringComparer.Ordinal);
            var missing = fallbackKeys.Where(key => !fileKeys.Contains(key)).OrderBy(static key => key, StringComparer.Ordinal).ToArray();
            var extra = fileKeys.Where(key => !fallbackKeys.Contains(key)).OrderBy(static key => key, StringComparer.Ordinal).ToArray();

            if (missing.Length > 0)
            {
                diagnostics.Add(new GeneratorDiagnosticInfo(
                    TranslationGeneratorDiagnostics.MissingKey,
                    $"Language '{file.Language}' is missing {missing.Length} key(s) present in fallback language '{fallbackLanguage}': {FormatKeys(missing)}",
                    new LocationInfo(file.FilePath)));
            }

            if (extra.Length > 0)
            {
                diagnostics.Add(new GeneratorDiagnosticInfo(
                    TranslationGeneratorDiagnostics.ExtraKey,
                    $"Language '{file.Language}' has {extra.Length} key(s) not present in fallback language '{fallbackLanguage}', so they are not generated in Language or LanguageKeys: {FormatKeys(extra)}",
                    new LocationInfo(file.FilePath)));
            }
        }
    }

    private static string FormatKeys(string[] keys)
    {
        var listed = string.Join(", ", keys.Take(MaxListedKeys));
        return keys.Length > MaxListedKeys ? $"{listed}, and {keys.Length - MaxListedKeys} more" : listed;
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

    public static TranslationGeneratorOptions GetOptions(AnalyzerConfigOptionsProvider optionsProvider)
    {
        optionsProvider.GlobalOptions.TryGetValue("build_property.FallbackLanguage", out var fallbackLanguage);
        optionsProvider.GlobalOptions.TryGetValue("build_property.GeneratedNamespace", out var generatedNamespace);
        optionsProvider.GlobalOptions.TryGetValue("build_property.I18NLanguageAliases", out var languageAliases);

        return new TranslationGeneratorOptions(
            fallbackLanguage ?? string.Empty,
            generatedNamespace ?? string.Empty,
            languageAliases ?? string.Empty);
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

internal readonly struct TranslationFileCandidate : IEquatable<TranslationFileCandidate>
{
    public TranslationFileCandidate(AdditionalText file, bool isMarked)
    {
        File = file;
        IsMarked = isMarked;
    }

    public AdditionalText File { get; }

    public bool IsMarked { get; }

    public bool Equals(TranslationFileCandidate other) =>
        ReferenceEquals(File, other.File) && IsMarked == other.IsMarked;

    public override bool Equals(object? obj) => obj is TranslationFileCandidate other && Equals(other);

    public override int GetHashCode() =>
        (System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(File) * 397) ^ IsMarked.GetHashCode();
}

internal static class TranslationGeneratorUtilities
{
    public const string DefaultCountParameter = "count";

    /// <summary>Name of the parameter that carries the format provider in the generated translation methods.</summary>
    public const string FormatProviderParameter = "__provider";

    /// <summary>Name of the parameter that carries the resolved language in the generated translation methods.</summary>
    public const string LanguageParameter = "__language";

    public const string TranslationMetadataKey = "build_metadata.AdditionalFiles.I18NTranslation";

    public static bool IsMarkedAsTranslation(AnalyzerConfigOptionsProvider optionsProvider, AdditionalText file) =>
        optionsProvider.GetOptions(file).TryGetValue(TranslationMetadataKey, out var value)
        && string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// The parameters of a key, in order: the placeholder names of the fallback text. A key is plural when any
    /// language uses pipe-separated variants. When the fallback is not plural, the count parameter is named
    /// "count" and comes first. This is the single source of the parameter order for the Language members
    /// and for <c>LanguageStrings.Format</c>.
    /// </summary>
    public static string[] GetKeyParameters(string fallbackValue, IEnumerable<string> allValues, out bool isPlural)
    {
        var fallbackIsPlural = IsPluralValue(fallbackValue);
        isPlural = fallbackIsPlural || allValues.Any(IsPluralValue);
        var parameters = GetParameterNames(fallbackValue, fallbackIsPlural);
        if (isPlural && !fallbackIsPlural)
        {
            parameters = new[] { DefaultCountParameter }
                .Concat(parameters.Where(static parameter => parameter != DefaultCountParameter))
                .ToArray();
        }

        return parameters;
    }

    public static string[] GetParameterNames(string value, bool isPlural)
    {
        var parameters = new List<string>();
        foreach (var token in TranslationMessageSyntax.Tokenize(value))
        {
            if (token.Kind != MessageTokenKind.Placeholder || parameters.Contains(token.Text, StringComparer.Ordinal))
            {
                continue;
            }

            parameters.Add(token.Text);
        }

        if (isPlural && parameters.Count == 0)
        {
            parameters.Add(DefaultCountParameter);
        }

        return parameters.ToArray();
    }

    /// <summary>
    /// Maps placeholder names to unique C# parameter identifiers, in the same order. Placeholders that
    /// sanitize to the same identifier (e.g. <c>1st</c> and <c>_1st</c>) get a numeric suffix.
    /// </summary>
    public static string[] GetParameterIdentifiers(string[] parameterNames)
    {
        var identifiers = new string[parameterNames.Length];
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < parameterNames.Length; index++)
        {
            var baseName = SanitizeName(parameterNames[index]);
            var identifier = baseName;
            for (var suffix = 2; !used.Add(identifier); suffix++)
            {
                identifier = baseName + suffix;
            }

            identifiers[index] = identifier;
        }

        return identifiers;
    }

    public static bool IsPluralValue(string value) => TranslationMessageSyntax.IsPlural(value);

    public static string[] SplitPluralParts(string value) => TranslationMessageSyntax.SplitPlural(value);

    public static bool TryCreateFormatExpression(
        string value,
        string[] parameterNames,
        out string expression,
        string providerParameter = FormatProviderParameter)
    {
        value ??= string.Empty;

        // Literals ({'text'}) become plain text. Text is escaped for string.Format, unless the message
        // has no placeholders and is emitted as a plain string.
        var formatBuilder = new StringBuilder();
        var plainBuilder = new StringBuilder();
        var hasPlaceholder = false;
        foreach (var token in TranslationMessageSyntax.Tokenize(value))
        {
            switch (token.Kind)
            {
                case MessageTokenKind.Placeholder:
                    var parameterIndex = Array.IndexOf(parameterNames, token.Text);
                    if (parameterIndex < 0)
                    {
                        expression = string.Empty;
                        return false;
                    }

                    formatBuilder.Append('{').Append(parameterIndex);
                    if (token.Format is not null)
                    {
                        // A format specifier such as {date:d} is passed through to string.Format.
                        formatBuilder.Append(':').Append(token.Format);
                    }

                    formatBuilder.Append('}');
                    hasPlaceholder = true;
                    break;
                case MessageTokenKind.Literal:
                    AppendEscapedFormatText(formatBuilder, token.Text);
                    plainBuilder.Append(token.Text);
                    break;
                default:
                    var text = value.Substring(token.Start, token.Length);
                    AppendEscapedFormatText(formatBuilder, text);
                    plainBuilder.Append(text);
                    break;
            }
        }

        if (!hasPlaceholder)
        {
            expression = ToCSharpStringLiteral(plainBuilder.ToString());
            return true;
        }

        var literal = ToCSharpStringLiteral(formatBuilder.ToString());
        var expressionBuilder = new StringBuilder("string.Format(" + providerParameter + ", ", literal.Length + 96);
        expressionBuilder.Append(literal);
        foreach (var identifier in GetParameterIdentifiers(parameterNames))
        {
            expressionBuilder.Append(", ").Append(identifier);
        }

        expression = expressionBuilder.Append(')').ToString();
        return true;
    }

    public static string[] GetUnsupportedPlaceholders(string value, string[] parameterNames)
    {
        var unsupported = new List<string>();
        foreach (var token in TranslationMessageSyntax.Tokenize(value))
        {
            if (token.Kind != MessageTokenKind.Placeholder ||
                Array.IndexOf(parameterNames, token.Text) >= 0 ||
                unsupported.Contains(token.Text, StringComparer.Ordinal))
            {
                continue;
            }

            unsupported.Add(token.Text);
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