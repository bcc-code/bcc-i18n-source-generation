using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BccCode.I18N.SourceGen;

internal static class I18NLanguageDictGenerator
{
    private const string DictionaryType = "global::System.Collections.Generic.Dictionary<string, string>";
    private const string CurrentLanguage = "global::System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName";

    public static void AddSource(SourceProductionContext context, TranslationModelSet translations)
    {
        const string className = "LanguageStrings";

        var writer = GeneratedFile.Begin(translations.GeneratedNamespace, className);
        WriteMembers(writer, translations.Files, translations.FallbackLanguage);
        GeneratedFile.AddSource(context, writer, translations.GeneratedNamespace, className);
    }

    private static void WriteMembers(IndentedWriter writer, EquatableArray<LanguageFileModel> files, string fallbackLanguage)
    {
        var fallbackLiteral = TranslationGeneratorUtilities.ToCSharpStringLiteral(fallbackLanguage);

        writer.Append("private static readonly global::System.Collections.Generic.Dictionary<string, ").Append(DictionaryType).AppendLine("> languages = new()");
        writer.OpenBlock();

        foreach (var file in files.Array.OrderBy(static file => file.Language, StringComparer.Ordinal))
        {
            writer.Append('[').Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(file.Language)).Append("] = new ").AppendLine(DictionaryType);
            writer.OpenBlock();

            foreach (var entry in file.Entries)
            {
                writer.Append('[').Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(entry.KeyPath))
                    .Append("] = ").Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(entry.Value)).AppendLine(",");
            }

            writer.CloseBlock(",");
        }

        writer.CloseBlock(";");
        writer.AppendLine();

        WriteLookup(writer, "public static string GetString(string key)", fallbackLiteral, "key");
        writer.AppendLine();
        WriteLookup(writer, "public static string? GetStringOrNull(string key)", fallbackLiteral, "null");
    }

    // The current culture's dictionary is used when it exists, otherwise the fallback dictionary.
    // A key missing from the chosen dictionary is not looked up in the fallback dictionary.
    private static void WriteLookup(IndentedWriter writer, string signature, string fallbackLiteral, string missingValue)
    {
        writer.AppendLine(signature);
        writer.OpenBlock();
        writer.Append("if (!languages.TryGetValue(").Append(CurrentLanguage).AppendLine(", out var dictionary) &&");
        writer.Indent();
        writer.Append("!languages.TryGetValue(").Append(fallbackLiteral).AppendLine(", out dictionary))");
        writer.Outdent();
        writer.OpenBlock();
        writer.Append("return ").Append(missingValue).AppendLine(";");
        writer.CloseBlock();
        writer.AppendLine();
        writer.Append("return dictionary.TryGetValue(key, out var value) ? value : ").Append(missingValue).AppendLine(";");
        writer.CloseBlock();
    }
}
