using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BccCode.I18N.SourceGen;

internal static class I18NLanguageDictGenerator
{
    private const string MetadataUpdateHandlerAttribute = "global::System.Reflection.Metadata.MetadataUpdateHandler";

    public static void AddSource(SourceProductionContext context, TranslationModelSet translations, bool supportsMetadataUpdateHandler)
    {
        const string className = "LanguageStrings";

        var ns = translations.GeneratedNamespace;
        string? assemblyAttribute = null;
        if (supportsMetadataUpdateHandler)
        {
            var type = string.IsNullOrWhiteSpace(ns) ? "global::" + className : "global::" + ns + "." + className;
            assemblyAttribute = "[assembly: " + MetadataUpdateHandlerAttribute + "(typeof(" + type + "))]";
        }

        var writer = GeneratedFile.BeginWithAssemblyAttribute(assemblyAttribute, ns, className);
        WriteMembers(writer, translations);
        GeneratedFile.AddSource(context, writer, ns, className);
    }

    // The translations are emitted as arrays that are built on first use and cached in static fields. The
    // fields have no initializers, and the arrays are built by methods, so hot reload can update the data
    // (method bodies). The MetadataUpdateHandler attribute makes the runtime call ClearCache afterwards, so
    // that the next lookup builds the arrays again. Compared with a switch over all keys, the compiler time
    // grows linearly with the number of keys.
    private static void WriteMembers(IndentedWriter writer, TranslationModelSet translations)
    {
        var files = translations.Files;
        var fallbackLanguage = translations.FallbackLanguage;
        var aliases = translations.Aliases;
        var orderedFiles = files.Array.OrderBy(static file => file.Language, StringComparer.Ordinal).ToArray();
        var fallbackIndex = Array.FindIndex(orderedFiles, file => string.Equals(file.Language, fallbackLanguage, StringComparison.OrdinalIgnoreCase));
        var keyIndex = TranslationKeyIndex.Create(files);

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
        writer.AppendLine("public static string? GetStringOrNull(string key, global::System.Globalization.CultureInfo culture) =>");
        writer.Indent();
        writer.AppendLine("GetStringOrNullByLanguage(key, ResolveLanguage(culture));");
        writer.Outdent();
        writer.AppendLine();
        writer.AppendLine("private static string? GetStringOrNullByLanguage(string key, string language)");
        writer.OpenBlock();
        writer.AppendLine("var index = FindKeyIndex(key);");
        writer.AppendLine("return index < 0 ? null : GetEntry(index, language);");
        writer.CloseBlock();

        writer.AppendLine();
        WriteListings(writer, orderedFiles, fallbackLanguage);

        writer.AppendLine();
        WriteFormat(writer, orderedFiles, translations.FallbackFile, keyIndex);

        writer.AppendLine();
        WriteCaches(writer, orderedFiles, fallbackIndex, keyIndex);
    }

    private const string StringPair = "global::System.Collections.Generic.KeyValuePair<string, string>";

    // These members allocate a new array or list on every call.
    private static void WriteListings(IndentedWriter writer, LanguageFileModel[] orderedFiles, string fallbackLanguage)
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
        writer.AppendLine("public static global::System.Collections.Generic.IReadOnlyList<string> Keys => CollectKeys(FallbackStrings());");
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
            writer.Append("return CollectStrings(").Append(GetStringsMethodName(index)).AppendLine("());");
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
        writer.AppendLine();

        // The values of a language are in the order of the key index (ordinal order of the keys of all files).
        writer.Append("private static global::System.Collections.Generic.List<").Append(StringPair)
            .AppendLine("> CollectStrings(string?[] strings)");
        writer.OpenBlock();
        writer.AppendLine("var keys = KeyPaths();");
        writer.Append("var result = new global::System.Collections.Generic.List<").Append(StringPair).AppendLine(">();");
        writer.AppendLine("for (var index = 0; index < keys.Length; index++)");
        writer.OpenBlock();
        writer.AppendLine("var value = strings[index];");
        writer.AppendLine("if (value != null)");
        writer.OpenBlock();
        writer.Append("result.Add(new ").Append(StringPair).AppendLine("(keys[index], value));");
        writer.CloseBlock();
        writer.CloseBlock();
        writer.AppendLine();
        writer.AppendLine("return result;");
        writer.CloseBlock();
        writer.AppendLine();

        writer.AppendLine("private static string[] CollectKeys(string?[] strings)");
        writer.OpenBlock();
        writer.AppendLine("var keys = KeyPaths();");
        writer.AppendLine("var count = 0;");
        writer.AppendLine("for (var index = 0; index < strings.Length; index++)");
        writer.OpenBlock();
        writer.AppendLine("if (strings[index] != null)");
        writer.OpenBlock();
        writer.AppendLine("count++;");
        writer.CloseBlock();
        writer.CloseBlock();
        writer.AppendLine();
        writer.AppendLine("var result = new string[count];");
        writer.AppendLine("count = 0;");
        writer.AppendLine("for (var index = 0; index < strings.Length; index++)");
        writer.OpenBlock();
        writer.AppendLine("if (strings[index] != null)");
        writer.OpenBlock();
        writer.AppendLine("result[count++] = keys[index];");
        writer.CloseBlock();
        writer.CloseBlock();
        writer.AppendLine();
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

    // Format: positional formatting of a stored message. The parameter names come from the same code as the
    // parameters of the Language members, so the order of the arguments is the same.
    private static void WriteFormat(IndentedWriter writer, LanguageFileModel[] orderedFiles, LanguageFileModel fallbackFile, TranslationKeyIndex keyIndex)
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

        // A language whose message uses placeholders that the key does not have is skipped as a whole by the
        // Language members (I18N006). Format does the same, so it needs to know these (language, key) pairs.
        var fallbackParameters = GetFallbackParameters(orderedFiles, fallbackFile, keyIndex);

        writer.AppendLine("private static bool HasUnsupportedPlaceholders(int index, string? language) =>");
        writer.Indent();
        writer.AppendLine("language switch");
        writer.OpenBlock();
        foreach (var file in orderedFiles)
        {
            if (string.Equals(file.Language, fallbackFile.Language, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var values = keyIndex.GetValues(file);
            var unsupportedIndexes = new List<int>();
            for (var index = 0; index < values.Length; index++)
            {
                var parameters = fallbackParameters[index];
                if (values[index] is { } value
                    && parameters is not null
                    && TranslationGeneratorUtilities.GetUnsupportedPlaceholders(value, parameters).Length > 0)
                {
                    unsupportedIndexes.Add(index);
                }
            }

            if (unsupportedIndexes.Count == 0)
            {
                continue;
            }

            writer.Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(file.Language)).AppendLine(" => index switch");
            writer.OpenBlock();
            foreach (var index in unsupportedIndexes)
            {
                writer.Append(ToInvariant(index)).AppendLine(" => true,");
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

    // The parameters of each key of the fallback file, in the order of the key index. Null for keys that the
    // fallback file lacks. Empty for keys without placeholders.
    private static string[]?[] GetFallbackParameters(LanguageFileModel[] orderedFiles, LanguageFileModel fallbackFile, TranslationKeyIndex keyIndex)
    {
        var fileValues = orderedFiles.Select(keyIndex.GetValues).ToArray();
        var fallbackValues = keyIndex.GetValues(fallbackFile);
        var result = new string[]?[keyIndex.Count];
        for (var index = 0; index < result.Length; index++)
        {
            if (fallbackValues[index] is not { } fallbackValue)
            {
                continue;
            }

            var allValues = new List<string>(fileValues.Length);
            foreach (var values in fileValues)
            {
                if (values[index] is { } value)
                {
                    allValues.Add(value);
                }
            }

            result[index] = TranslationGeneratorUtilities.GetKeyParameters(fallbackValue, allValues, out _);
        }

        return result;
    }

    // The data arrays and their caches. Every array is built on first use by a method that only contains the data.
    // A benign race can build an array twice. The key index (Dictionary) is built from the keys on first use.
    private static void WriteCaches(IndentedWriter writer, LanguageFileModel[] orderedFiles, int fallbackIndex, TranslationKeyIndex keyIndex)
    {
        writer.AppendLine("private static string[]? s_keys;");
        writer.AppendLine("private static string[]?[]? s_parameterNames;");
        writer.AppendLine("private static global::System.Collections.Generic.Dictionary<string, int>? s_keyIndex;");
        for (var index = 0; index < orderedFiles.Length; index++)
        {
            writer.Append("private static string?[]? s_strings").Append(ToInvariant(index)).AppendLine(";");
        }

        writer.AppendLine();
        writer.AppendLine("/// <summary>");
        writer.AppendLine("/// Clears the cached data. The runtime calls this after a hot reload edit (MetadataUpdateHandler), so that edited");
        writer.AppendLine("/// translation files are used by the next lookup.");
        writer.AppendLine("/// </summary>");
        writer.AppendLine("internal static void ClearCache(global::System.Type[]? updatedTypes)");
        writer.OpenBlock();
        writer.AppendLine("s_keys = null;");
        writer.AppendLine("s_parameterNames = null;");
        writer.AppendLine("s_keyIndex = null;");
        for (var index = 0; index < orderedFiles.Length; index++)
        {
            writer.Append("s_strings").Append(ToInvariant(index)).AppendLine(" = null;");
        }

        writer.CloseBlock();
        writer.AppendLine();

        writer.AppendLine("private static string[] KeyPaths() => s_keys ??= KeysData();");
        writer.AppendLine();
        writer.AppendLine("private static string[]?[] ParameterNames() => s_parameterNames ??= ParameterNamesData();");
        writer.AppendLine();
        for (var index = 0; index < orderedFiles.Length; index++)
        {
            writer.Append("private static string?[] ").Append(GetStringsMethodName(index)).Append("() => s_strings").Append(ToInvariant(index))
                .Append(" ??= ").Append(GetStringsDataMethodName(index)).AppendLine("();");
            writer.AppendLine();
        }

        writer.AppendLine("private static string?[] FallbackStrings() => " + GetStringsMethodName(fallbackIndex) + "();");
        writer.AppendLine();
        writer.AppendLine("private static string?[] GetStrings(string? language) =>");
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
                .Append(" => ").Append(GetStringsMethodName(index)).AppendLine("(),");
        }

        writer.Append("_ => ").Append(GetStringsMethodName(fallbackIndex)).AppendLine("(),");
        writer.CloseBlock(";");
        writer.Outdent();
        writer.AppendLine();

        writer.AppendLine("private static global::System.Collections.Generic.Dictionary<string, int> KeyIndex()");
        writer.OpenBlock();
        writer.AppendLine("var index = s_keyIndex;");
        writer.AppendLine("if (index == null)");
        writer.OpenBlock();
        writer.AppendLine("var keys = KeyPaths();");
        writer.AppendLine("index = new global::System.Collections.Generic.Dictionary<string, int>(keys.Length, global::System.StringComparer.Ordinal);");
        writer.AppendLine("for (var position = 0; position < keys.Length; position++)");
        writer.OpenBlock();
        writer.AppendLine("index[keys[position]] = position;");
        writer.CloseBlock();
        writer.AppendLine();
        writer.AppendLine("s_keyIndex = index;");
        writer.CloseBlock();
        writer.AppendLine();
        writer.AppendLine("return index;");
        writer.CloseBlock();
        writer.AppendLine();

        writer.AppendLine("// The key paths of all files in ordinal order. A key's position is its index in every other array.");
        writer.AppendLine("private static string[] KeysData() =>");
        writer.Indent();
        WriteStringArray(writer, keyIndex.Keys);
        writer.Outdent();

        writer.AppendLine();
        writer.AppendLine("// The parameter names of each key, for the keys of the fallback file that have placeholders.");
        writer.AppendLine("private static string[]?[] ParameterNamesData() =>");
        writer.Indent();
        writer.AppendLine("new string[]?[]");
        writer.OpenBlock();
        var fallbackFile = orderedFiles[fallbackIndex];
        var parameters = GetFallbackParameters(orderedFiles, fallbackFile, keyIndex);
        foreach (var names in parameters)
        {
            if (names is null || names.Length == 0)
            {
                writer.AppendLine("null,");
            }
            else
            {
                writer.AppendLine("new string[] { " + string.Join(", ", names.Select(TranslationGeneratorUtilities.ToCSharpStringLiteral)) + " },");
            }
        }

        writer.CloseBlock(";");
        writer.Outdent();

        for (var index = 0; index < orderedFiles.Length; index++)
        {
            writer.AppendLine();
            writer.Append("// ").AppendLine(TranslationGeneratorUtilities.ToCSharpStringLiteral(orderedFiles[index].Language));
            writer.Append("private static string?[] ").Append(GetStringsDataMethodName(index)).AppendLine("() =>");
            writer.Indent();
            writer.AppendLine("new string?[]");
            writer.OpenBlock();
            foreach (var value in keyIndex.GetValues(orderedFiles[index]))
            {
                writer.AppendLine(value is null ? "null," : TranslationGeneratorUtilities.ToCSharpStringLiteral(value) + ",");
            }

            writer.CloseBlock(";");
            writer.Outdent();
        }
    }

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

    private static string ToInvariant(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string GetStringsMethodName(int index) => "Strings" + ToInvariant(index);

    private static string GetStringsDataMethodName(int index) => "StringsData" + ToInvariant(index);
}
