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
        WriteMembers(writer, translations.Files, translations.FallbackLanguage, translations.Aliases);
        GeneratedFile.AddSource(context, writer, translations.GeneratedNamespace, className);
    }

    // The translations are emitted as switch expressions in method bodies rather than a static
    // dictionary. Hot reload can update method bodies, but it cannot re-run a static field
    // initializer, so a dictionary made dotnet watch restart the app on every translation edit.
    private static void WriteMembers(IndentedWriter writer, EquatableArray<LanguageFileModel> files, string fallbackLanguage, EquatableArray<LanguageAliasModel> aliases)
    {
        var orderedFiles = files.Array.OrderBy(static file => file.Language, StringComparer.Ordinal).ToArray();
        var fallbackIndex = Array.FindIndex(orderedFiles, file => string.Equals(file.Language, fallbackLanguage, StringComparison.OrdinalIgnoreCase));

        writer.AppendLine("public static string GetString(string key) => GetStringOrNull(key) ?? key;");
        writer.AppendLine();

        WriteResolveLanguage(writer, orderedFiles, fallbackLanguage, aliases);
        writer.AppendLine();

        // The current culture's language is resolved first. A key missing from it is looked up in the
        // fallback language, which matches the members of the Language class.
        var fallbackMethod = GetLookupMethodName(fallbackIndex);
        writer.AppendLine("public static string? GetStringOrNull(string key) =>");
        writer.Indent();
        writer.AppendLine("ResolveLanguage(global::System.Globalization.CultureInfo.CurrentUICulture) switch");
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

        for (var index = 0; index < orderedFiles.Length; index++)
        {
            writer.AppendLine();
            WriteLookupMethod(writer, GetLookupMethodName(index), orderedFiles[index]);
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
