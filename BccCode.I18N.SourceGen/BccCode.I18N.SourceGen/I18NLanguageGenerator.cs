using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BccCode.I18N.SourceGen;

internal static class I18NLanguageGenerator
{
    public static void AddSource(SourceProductionContext context, TranslationModelSet translations)
    {
        const string className = "Language";
        var currentLanguage = GetCurrentLanguageExpression(translations.GeneratedNamespace);

        var translationsByKey = BuildTranslationsByKey(translations.Files);
        var filePathsByLanguage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in translations.Files)
        {
            filePathsByLanguage[file.Language] = file.FilePath;
        }

        var root = TranslationTreeBuilder.BuildTree(translations.FallbackFile.Entries);
        var generationContext = new GenerationContext(context, translationsByKey, filePathsByLanguage, translations.FallbackLanguage, currentLanguage);

        var writer = GeneratedFile.Begin(translations.GeneratedNamespace, className);
        WriteMembers(writer, generationContext, root);
        GeneratedFile.AddSource(context, writer, translations.GeneratedNamespace, className);
    }

    private sealed class GenerationContext
    {
        public GenerationContext(
            SourceProductionContext sourceContext,
            Dictionary<string, Dictionary<string, string>> translationsByKey,
            Dictionary<string, string> filePathsByLanguage,
            string fallbackLanguage,
            string currentLanguage)
        {
            CurrentLanguage = currentLanguage;
            SourceContext = sourceContext;
            TranslationsByKey = translationsByKey;
            FilePathsByLanguage = filePathsByLanguage;
            FallbackLanguage = fallbackLanguage;
        }

        public SourceProductionContext SourceContext { get; }

        public Dictionary<string, Dictionary<string, string>> TranslationsByKey { get; }

        public Dictionary<string, string> FilePathsByLanguage { get; }

        public string FallbackLanguage { get; }

        public string CurrentLanguage { get; }
    }

    // Fully qualified so that a nested class in Language named LanguageStrings cannot shadow it.
    private static string GetCurrentLanguageExpression(string generatedNamespace) =>
        (string.IsNullOrWhiteSpace(generatedNamespace) ? "global::" : "global::" + generatedNamespace + ".") +
        "LanguageStrings.ResolveLanguage(global::System.Globalization.CultureInfo.CurrentUICulture)";

    private static Dictionary<string, Dictionary<string, string>> BuildTranslationsByKey(EquatableArray<LanguageFileModel> files)
    {
        var translationsByKey = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (var entry in file.Entries)
            {
                if (!translationsByKey.TryGetValue(entry.KeyPath, out var valuesByLanguage))
                {
                    valuesByLanguage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    translationsByKey.Add(entry.KeyPath, valuesByLanguage);
                }

                valuesByLanguage[file.Language] = entry.Value;
            }
        }

        return translationsByKey;
    }

    private static void WriteMembers(IndentedWriter writer, GenerationContext context, TranslationNode node)
    {
        foreach (var child in node.Children.Values)
        {
            if (child.Children.Count > 0)
            {
                writer.Append("public static class ").AppendLine(TranslationGeneratorUtilities.SanitizeName(child.Name));
                writer.OpenBlock();
                WriteMembers(writer, context, child);
                writer.CloseBlock();
                continue;
            }

            if (child.KeyPath is null || child.Value is null || !context.TranslationsByKey.TryGetValue(child.KeyPath, out var translations))
            {
                continue;
            }

            WriteLeaf(writer, context, child.Name, child.KeyPath, child.Value, translations);
        }
    }

    private static void WriteLeaf(
        IndentedWriter writer,
        GenerationContext context,
        string name,
        string keyPath,
        string fallbackValue,
        Dictionary<string, string> translations)
    {
        var fallbackLanguage = context.FallbackLanguage;

        // A key is plural when any language uses pipe-separated variants, so one language
        // can pluralize while the others (including the fallback) use a single form.
        var fallbackIsPlural = TranslationGeneratorUtilities.IsPluralValue(fallbackValue);
        var isPlural = fallbackIsPlural || translations.Values.Any(TranslationGeneratorUtilities.IsPluralValue);
        var parameters = TranslationGeneratorUtilities.GetParameterNames(fallbackValue, fallbackIsPlural);
        if (isPlural && !fallbackIsPlural)
        {
            // The fallback has no plural forms to infer the count parameter from, so it is always named "count".
            parameters = new[] { TranslationGeneratorUtilities.DefaultCountParameter }
                .Concat(parameters.Where(static parameter => parameter != TranslationGeneratorUtilities.DefaultCountParameter))
                .ToArray();
        }

        writer.Append("/// ").AppendLine(TranslationGeneratorUtilities.EscapeXmlDoc(fallbackValue));
        writer.Append("public static string ").Append(TranslationGeneratorUtilities.SanitizeName(name));

        var identifiers = TranslationGeneratorUtilities.GetParameterIdentifiers(parameters);
        if (parameters.Length > 0)
        {
            writer.Append('(');
            for (var index = 0; index < identifiers.Length; index++)
            {
                if (index > 0)
                {
                    writer.Append(", ");
                }

                writer.Append(isPlural && index == 0 ? "int " : "object ").Append(identifiers[index]);
            }

            writer.Append(')');
        }

        writer.AppendLine(" =>");
        writer.Indent();

        var otherTranslations = translations
            .Where(pair => !string.Equals(pair.Key, fallbackLanguage, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal);

        if (isPlural)
        {
            writer.Append('(').Append(identifiers[0]).Append(", ")
                .Append(context.CurrentLanguage).AppendLine(") switch");
            writer.OpenBlock();

            foreach (var translation in otherTranslations)
            {
                WritePluralCases(writer, context, keyPath, translation.Key, translation.Value, parameters, isFallback: false);
            }

            WritePluralCases(writer, context, keyPath, fallbackLanguage, fallbackValue, parameters, isFallback: true);
        }
        else
        {
            writer.Append(context.CurrentLanguage).AppendLine(" switch");
            writer.OpenBlock();

            foreach (var translation in otherTranslations)
            {
                if (!TryCreateExpression(context, keyPath, translation.Key, translation.Value, parameters, out var expression))
                {
                    continue;
                }

                writer.Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(translation.Key))
                    .Append(" => ").Append(expression).AppendLine(",");
            }

            TranslationGeneratorUtilities.TryCreateFormatExpression(fallbackValue, parameters, out var fallbackExpression);
            writer.Append("_ => ").Append(fallbackExpression).AppendLine(",");
        }

        writer.CloseBlock(";");
        writer.Outdent();
        writer.AppendLine();
    }

    private static void WritePluralCases(
        IndentedWriter writer,
        GenerationContext context,
        string keyPath,
        string language,
        string value,
        string[] parameters,
        bool isFallback)
    {
        // Every variant is validated before any arm is written, so a language with one invalid
        // variant falls back as a whole instead of mixing its own and the fallback's variants.
        var parts = TranslationGeneratorUtilities.SplitPluralParts(value);
        var expressions = new string[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!TryCreateExpression(context, keyPath, language, parts[index], parameters, out expressions[index]))
            {
                return;
            }
        }

        var languagePattern = isFallback ? "_" : TranslationGeneratorUtilities.ToCSharpStringLiteral(language);
        for (var index = 0; index < expressions.Length; index++)
        {
            writer.Append('(');
            if (index == expressions.Length - 1)
            {
                writer.Append('_');
            }
            else
            {
                writer.Append(index.ToString());
            }

            writer.Append(", ").Append(languagePattern).Append(") => ").Append(expressions[index]).AppendLine(",");
        }
    }

    private static bool TryCreateExpression(
        GenerationContext context,
        string keyPath,
        string language,
        string value,
        string[] parameters,
        out string expression)
    {
        if (TranslationGeneratorUtilities.TryCreateFormatExpression(value, parameters, out expression))
        {
            return true;
        }

        var location = context.FilePathsByLanguage.TryGetValue(language, out var filePath)
            ? new LocationInfo(filePath).ToLocation()
            : Location.None;

        var unsupportedPlaceholders = TranslationGeneratorUtilities.GetUnsupportedPlaceholders(value, parameters);
        context.SourceContext.ReportDiagnostic(Diagnostic.Create(
            TranslationGeneratorDiagnostics.PlaceholderMismatch,
            location,
            $"Translation key '{keyPath}' in language '{language}' uses placeholders [{string.Join(", ", unsupportedPlaceholders)}] that are not present in the fallback language. The fallback translation will be used instead."));
        return false;
    }
}
