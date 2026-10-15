using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BccCode.I18N.SourceGen;

internal static class I18NLanguageDictGenerator
{
    private const string CurrentLanguage = "global::System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName";

    public static void AddSource(SourceProductionContext context, TranslationModelSet translations)
    {
        const string className = "LanguageStrings";

        var writer = GeneratedFile.Begin(translations.GeneratedNamespace, className);
        WriteMembers(writer, translations.Files, translations.FallbackLanguage);
        GeneratedFile.AddSource(context, writer, translations.GeneratedNamespace, className);
    }

    // The translations are emitted as switch expressions in method bodies rather than a static
    // dictionary. Hot reload can update method bodies, but it cannot re-run a static field
    // initializer, so a dictionary made dotnet watch restart the app on every translation edit.
    private static void WriteMembers(IndentedWriter writer, EquatableArray<LanguageFileModel> files, string fallbackLanguage)
    {
        var orderedFiles = files.Array.OrderBy(static file => file.Language, StringComparer.Ordinal).ToArray();
        var fallbackIndex = Array.FindIndex(orderedFiles, file => string.Equals(file.Language, fallbackLanguage, StringComparison.OrdinalIgnoreCase));

        writer.AppendLine("public static string GetString(string key) => GetStringOrNull(key) ?? key;");
        writer.AppendLine();

        // The current culture's translations are used when that language exists, otherwise the
        // fallback language's. A key missing from the chosen language is not looked up in the fallback.
        writer.AppendLine("public static string? GetStringOrNull(string key) =>");
        writer.Indent();
        writer.Append(CurrentLanguage).AppendLine(" switch");
        writer.OpenBlock();
        for (var index = 0; index < orderedFiles.Length; index++)
        {
            writer.Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(orderedFiles[index].Language))
                .Append(" => ").Append(GetLookupMethodName(index)).AppendLine("(key),");
        }

        writer.Append("_ => ").Append(GetLookupMethodName(fallbackIndex)).AppendLine("(key),");
        writer.CloseBlock(";");
        writer.Outdent();

        for (var index = 0; index < orderedFiles.Length; index++)
        {
            writer.AppendLine();
            WriteLookupMethod(writer, GetLookupMethodName(index), orderedFiles[index]);
        }
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
