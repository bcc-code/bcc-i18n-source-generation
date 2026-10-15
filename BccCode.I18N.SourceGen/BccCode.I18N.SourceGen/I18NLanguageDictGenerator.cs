using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BccCode.I18N.SourceGen;

internal static class I18NLanguageDictGenerator
{
    public static void AddSource(SourceProductionContext context, TranslationModelSet translations)
    {
        const string className = "LanguageStrings";

        var writer = GeneratedFile.Begin(translations.GeneratedNamespace, className);
        WriteMembers(writer, translations);
        GeneratedFile.AddSource(context, writer, translations.GeneratedNamespace, className);
    }

    // The translations are emitted as switch expressions in method bodies rather than a static
    // dictionary. Hot reload can update method bodies, but it cannot re-run a static field
    // initializer, so a dictionary made dotnet watch restart the app on every translation edit.
    private static void WriteMembers(IndentedWriter writer, TranslationModelSet translations)
    {
        var files = translations.Files;
        var fallbackLanguage = translations.FallbackLanguage;
        var aliases = translations.Aliases;
        var orderedFiles = files.Array.OrderBy(static file => file.Language, StringComparer.Ordinal).ToArray();
        var fallbackIndex = Array.FindIndex(orderedFiles, file => string.Equals(file.Language, fallbackLanguage, StringComparison.OrdinalIgnoreCase));

        writer.AppendLine("public static string GetString(string key) => GetStringOrNull(key) ?? key;");
        writer.AppendLine();
        writer.AppendLine("public static string GetString(string key, global::System.Globalization.CultureInfo culture) => GetStringOrNull(key, culture) ?? key;");
        writer.AppendLine();
        writer.AppendLine("public static string? GetStringOrNull(string key) => GetStringOrNull(key, global::System.Globalization.CultureInfo.CurrentUICulture);");
        writer.AppendLine();

        WriteResolveLanguage(writer, orderedFiles, fallbackLanguage, aliases);
        writer.AppendLine();

        // The current culture's language is resolved first. A key missing from it is looked up in the
        // fallback language, which matches the members of the Language class.
        var fallbackMethod = GetLookupMethodName(fallbackIndex);
        writer.AppendLine("public static string? GetStringOrNull(string key, global::System.Globalization.CultureInfo culture) =>");
        writer.Indent();
        writer.AppendLine("GetStringOrNullByLanguage(key, ResolveLanguage(culture));");
        writer.Outdent();
        writer.AppendLine();
        writer.AppendLine("private static string? GetStringOrNullByLanguage(string key, string language) =>");
        writer.Indent();
        writer.AppendLine("language switch");
        writer.OpenBlock();
        for (var index = 0; index < orderedFiles.Length; index++)
        {
            if (index == fallbackIndex)
            {
                continue;
            }

            writer.Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(orderedFiles[index].Language))
                .Append(" => ").Append(GetLookupMethodName(index)).Append("(key) ?? ").Append(fallbackMethod).AppendLine("(key),");
        }

        writer.Append("_ => ").Append(fallbackMethod).AppendLine("(key),");
        writer.CloseBlock(";");
        writer.Outdent();

        writer.AppendLine();
        WriteListings(writer, orderedFiles, fallbackLanguage, translations.FallbackFile);

        writer.AppendLine();
        WriteFormat(writer, orderedFiles, translations.FallbackFile);

        for (var index = 0; index < orderedFiles.Length; index++)
        {
            writer.AppendLine();
            WriteLookupMethod(writer, GetLookupMethodName(index), orderedFiles[index]);
            writer.AppendLine();
            WriteAllStringsMethod(writer, GetAllStringsMethodName(index), orderedFiles[index]);
        }
    }

    private const string StringPair = "global::System.Collections.Generic.KeyValuePair<string, string>";

    // These members allocate a new array or list on every call.
    private static void WriteListings(IndentedWriter writer, LanguageFileModel[] orderedFiles, string fallbackLanguage, LanguageFileModel fallbackFile)
    {
        writer.Append("/// <summary>The language of the fallback translation file.</summary>").AppendLine();
        writer.Append("public static string FallbackLanguage => ").Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(fallbackLanguage)).AppendLine(";");
        writer.AppendLine();

        writer.AppendLine("/// <summary>The languages of all translation files in ordinal order. Allocates on every call.</summary>");
        writer.AppendLine("public static global::System.Collections.Generic.IReadOnlyList<string> Languages =>");
        writer.Indent();
        WriteStringArray(writer, orderedFiles.Select(static file => file.Language));
        writer.Outdent();
        writer.AppendLine();

        writer.AppendLine("/// <summary>The key paths of the fallback translation file in ordinal order. Allocates on every call.</summary>");
        writer.AppendLine("public static global::System.Collections.Generic.IReadOnlyList<string> Keys =>");
        writer.Indent();
        WriteStringArray(
            writer,
            DistinctByKeyPath(fallbackFile.Entries).Select(static entry => entry.KeyPath).OrderBy(static key => key, StringComparer.Ordinal));
        writer.Outdent();
        writer.AppendLine();

        writer.AppendLine("/// <summary>");
        writer.AppendLine("/// The stored strings of a translation file language (case-insensitive), with linked messages resolved.");
        writer.AppendLine("/// Empty for an unknown language. Allocates on every call.");
        writer.AppendLine("/// </summary>");
        writer.Append("public static global::System.Collections.Generic.IReadOnlyList<").Append(StringPair).AppendLine("> GetAllStrings(string language)");
        writer.OpenBlock();
        for (var index = 0; index < orderedFiles.Length; index++)
        {
            writer.Append("if (string.Equals(language, ").Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(orderedFiles[index].Language))
                .AppendLine(", global::System.StringComparison.OrdinalIgnoreCase))");
            writer.OpenBlock();
            writer.Append("return ").Append(GetAllStringsMethodName(index)).AppendLine("();");
            writer.CloseBlock();
        }

        writer.Append("return global::System.Array.Empty<").Append(StringPair).AppendLine(">();");
        writer.CloseBlock();
        writer.AppendLine();

        writer.AppendLine("/// <summary>");
        writer.AppendLine("/// The stored strings of the language that matches <paramref name=\"culture\"/>. With <paramref name=\"includeFallback\"/>,");
        writer.AppendLine("/// strings the language lacks are taken from the fallback language. Allocates on every call.");
        writer.AppendLine("/// </summary>");
        writer.Append("public static global::System.Collections.Generic.IReadOnlyList<").Append(StringPair)
            .AppendLine("> GetAllStrings(global::System.Globalization.CultureInfo culture, bool includeFallback = true)");
        writer.OpenBlock();
        writer.AppendLine("var language = ResolveLanguage(culture);");
        writer.AppendLine("var strings = GetAllStrings(language);");
        writer.AppendLine("if (!includeFallback || string.Equals(language, FallbackLanguage, global::System.StringComparison.Ordinal))");
        writer.OpenBlock();
        writer.AppendLine("return strings;");
        writer.CloseBlock();
        writer.AppendLine();
        writer.AppendLine("var fallback = GetAllStrings(FallbackLanguage);");
        writer.AppendLine("var keys = new global::System.Collections.Generic.HashSet<string>(global::System.StringComparer.Ordinal);");
        writer.Append("var result = new global::System.Collections.Generic.List<").Append(StringPair).AppendLine(">(strings.Count + fallback.Count);");
        writer.AppendLine("for (var index = 0; index < strings.Count; index++)");
        writer.OpenBlock();
        writer.AppendLine("keys.Add(strings[index].Key);");
        writer.AppendLine("result.Add(strings[index]);");
        writer.CloseBlock();
        writer.AppendLine("for (var index = 0; index < fallback.Count; index++)");
        writer.OpenBlock();
        writer.AppendLine("if (keys.Add(fallback[index].Key))");
        writer.OpenBlock();
        writer.AppendLine("result.Add(fallback[index]);");
        writer.CloseBlock();
        writer.CloseBlock();
        writer.AppendLine("return result;");
        writer.CloseBlock();
    }

    private static void WriteStringArray(IndentedWriter writer, IEnumerable<string> values)
    {
        writer.AppendLine("new string[]");
        writer.OpenBlock();
        foreach (var value in values)
        {
            writer.Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(value)).AppendLine(",");
        }

        writer.CloseBlock(";");
    }

    private static void WriteAllStringsMethod(IndentedWriter writer, string methodName, LanguageFileModel file)
    {
        writer.Append("private static ").Append(StringPair).Append("[] ").Append(methodName).AppendLine("() =>");
        writer.Indent();
        writer.Append("new ").Append(StringPair).AppendLine("[]");
        writer.OpenBlock();
        foreach (var entry in DistinctByKeyPath(file.Entries).OrderBy(static entry => entry.KeyPath, StringComparer.Ordinal))
        {
            writer.Append("new ").Append(StringPair).Append('(')
                .Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(entry.KeyPath)).Append(", ")
                .Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(entry.Value)).AppendLine("),");
        }

        writer.CloseBlock(";");
        writer.Outdent();
    }

    // Format: positional formatting of a stored message. The parameter names come from the same code as the
    // parameters of the Language members, so the order of the arguments is the same.
    private static void WriteFormat(IndentedWriter writer, LanguageFileModel[] orderedFiles, LanguageFileModel fallbackFile)
    {
        writer.AppendLine("/// <summary>");
        writer.AppendLine("/// Formats the stored message of <paramref name=\"key\"/> in the language of the current UI culture. The arguments match the");
        writer.AppendLine("/// parameters of the <c>Language</c> member for the key (for a plural message, the first one is the count).");
        writer.AppendLine("/// Numbers and dates use the current culture. Returns the key when it is unknown.");
        writer.AppendLine("/// </summary>");
        writer.AppendLine("public static string Format(string key, params object?[] args) =>");
        writer.Indent();
        writer.AppendLine("FormatCore(key, ResolveLanguage(global::System.Globalization.CultureInfo.CurrentUICulture), global::System.Globalization.CultureInfo.CurrentCulture, args, false);");
        writer.Outdent();
        writer.AppendLine();
        writer.AppendLine("/// <summary>Like <c>Format(key, args)</c>, but the language and the formatting both use <paramref name=\"culture\"/>.</summary>");
        writer.AppendLine("public static string Format(string key, global::System.Globalization.CultureInfo culture, params object?[] args) =>");
        writer.Indent();
        writer.AppendLine("FormatCore(key, ResolveLanguage(culture), culture, args, false);");
        writer.Outdent();
        writer.AppendLine();

        var valuesByKey = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in orderedFiles)
        {
            foreach (var entry in DistinctByKeyPath(file.Entries))
            {
                if (!valuesByKey.TryGetValue(entry.KeyPath, out var values))
                {
                    values = new List<string>();
                    valuesByKey.Add(entry.KeyPath, values);
                }

                values.Add(entry.Value);
            }
        }

        writer.AppendLine("private static string[]? GetParameterNames(string key) =>");
        writer.Indent();
        writer.AppendLine("key switch");
        writer.OpenBlock();
        foreach (var entry in DistinctByKeyPath(fallbackFile.Entries).OrderBy(static entry => entry.KeyPath, StringComparer.Ordinal))
        {
            var parameters = TranslationGeneratorUtilities.GetKeyParameters(entry.Value, valuesByKey[entry.KeyPath], out _);
            if (parameters.Length == 0)
            {
                continue;
            }

            writer.Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(entry.KeyPath)).Append(" => new string[] { ")
                .Append(string.Join(", ", parameters.Select(TranslationGeneratorUtilities.ToCSharpStringLiteral))).AppendLine(" },");
        }

        writer.AppendLine("_ => null,");
        writer.CloseBlock(";");
        writer.Outdent();
        writer.AppendLine();

        // A language whose message uses placeholders that the key does not have is skipped as a whole by the
        // Language members (I18N006). Format does the same, so it needs to know these (language, key) pairs.
        var fallbackParameters = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var entry in DistinctByKeyPath(fallbackFile.Entries))
        {
            fallbackParameters[entry.KeyPath] = TranslationGeneratorUtilities.GetKeyParameters(entry.Value, valuesByKey[entry.KeyPath], out _);
        }

        writer.AppendLine("private static bool HasUnsupportedPlaceholders(string key, string language) =>");
        writer.Indent();
        writer.AppendLine("language switch");
        writer.OpenBlock();
        foreach (var file in orderedFiles)
        {
            if (string.Equals(file.Language, fallbackFile.Language, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var unsupportedKeys = DistinctByKeyPath(file.Entries)
                .Where(entry => fallbackParameters.TryGetValue(entry.KeyPath, out var parameters)
                    && TranslationGeneratorUtilities.GetUnsupportedPlaceholders(entry.Value, parameters).Length > 0)
                .Select(static entry => entry.KeyPath)
                .OrderBy(static key => key, StringComparer.Ordinal)
                .ToArray();
            if (unsupportedKeys.Length == 0)
            {
                continue;
            }

            writer.Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(file.Language)).AppendLine(" => key switch");
            writer.OpenBlock();
            foreach (var key in unsupportedKeys)
            {
                writer.Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(key)).AppendLine(" => true,");
            }

            writer.AppendLine("_ => false,");
            writer.CloseBlock(",");
        }

        writer.AppendLine("_ => false,");
        writer.CloseBlock(";");
        writer.Outdent();
        writer.AppendLine();

        foreach (var line in RuntimeFormatSource.Code.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Length == 0)
            {
                writer.AppendLine();
            }
            else
            {
                writer.AppendLine(line);
            }
        }
    }

    private static string GetAllStringsMethodName(int index) => "GetAllStrings" + index;

    // Walks the culture and its parents (nb-NO, nb, ...) and returns the first translation file language that
    // matches a culture name, or that a culture name is aliased to. Names are compared without allocating.
    private static void WriteResolveLanguage(
        IndentedWriter writer,
        LanguageFileModel[] orderedFiles,
        string fallbackLanguage,
        EquatableArray<LanguageAliasModel> aliases)
    {
        writer.AppendLine("/// <summary>");
        writer.AppendLine("/// Returns the translation file language to use for <paramref name=\"culture\"/>: the first match of the culture");
        writer.AppendLine("/// or one of its parent cultures (by file name, then by language alias), otherwise the fallback language.");
        writer.AppendLine("/// </summary>");
        writer.AppendLine("public static string ResolveLanguage(global::System.Globalization.CultureInfo culture)");
        writer.OpenBlock();
        writer.AppendLine("for (var current = culture; current != null; current = current.Parent)");
        writer.OpenBlock();
        writer.AppendLine("var name = current.Name;");
        writer.AppendLine("if (name.Length == 0)");
        writer.OpenBlock();
        writer.AppendLine("break;");
        writer.CloseBlock();
        writer.AppendLine();

        foreach (var file in orderedFiles)
        {
            WriteNameMatch(writer, file.Language, file.Language);
        }

        foreach (var alias in aliases)
        {
            WriteNameMatch(writer, alias.Source, alias.Target);
        }

        writer.CloseBlock();
        writer.AppendLine();
        writer.Append("return ").Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(fallbackLanguage)).AppendLine(";");
        writer.CloseBlock();
    }

    private static void WriteNameMatch(IndentedWriter writer, string name, string result)
    {
        writer.Append("if (string.Equals(name, ").Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(name))
            .AppendLine(", global::System.StringComparison.OrdinalIgnoreCase))");
        writer.OpenBlock();
        writer.Append("return ").Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(result)).AppendLine(";");
        writer.CloseBlock();
    }

    private static void WriteLookupMethod(IndentedWriter writer, string methodName, LanguageFileModel file)
    {
        writer.Append("// ").AppendLine(TranslationGeneratorUtilities.ToCSharpStringLiteral(file.Language));
        writer.Append("private static string? ").Append(methodName).AppendLine("(string key) =>");
        writer.Indent();
        writer.AppendLine("key switch");
        writer.OpenBlock();

        foreach (var entry in DistinctByKeyPath(file.Entries))
        {
            writer.Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(entry.KeyPath))
                .Append(" => ").Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(entry.Value)).AppendLine(",");
        }

        writer.AppendLine("_ => null,");
        writer.CloseBlock(";");
        writer.Outdent();
    }

    // Duplicate key paths (e.g. a JSON property repeated, or "a.b" next to { "a": { "b" } }) would be
    // duplicate switch arms. The last value wins, matching the previous dictionary initializer.
    private static IEnumerable<TranslationEntryModel> DistinctByKeyPath(EquatableArray<TranslationEntryModel> entries)
    {
        var lastIndexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < entries.Length; index++)
        {
            lastIndexByKey[entries[index].KeyPath] = index;
        }

        for (var index = 0; index < entries.Length; index++)
        {
            if (lastIndexByKey[entries[index].KeyPath] == index)
            {
                yield return entries[index];
            }
        }
    }

    private static string GetLookupMethodName(int index) => "GetStringOrNull" + index;
}
