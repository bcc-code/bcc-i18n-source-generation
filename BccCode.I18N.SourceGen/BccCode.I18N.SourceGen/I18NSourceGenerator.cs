using Microsoft.CodeAnalysis;

namespace BccCode.I18N.SourceGen;

[Generator]
public sealed class I18NSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var parsedFiles = context.AdditionalTextsProvider
            .Where(static file => TranslationGeneratorUtilities.IsJsonFile(file))
            .Select(static (file, cancellationToken) => TranslationFileParser.Parse(file, cancellationToken))
            .WithTrackingName(TranslationGeneratorTrackingNames.ParsedTranslationFile);

        var fallbackLanguage = context.AnalyzerConfigOptionsProvider
            .Select(static (optionsProvider, _) => TranslationFileParser.GetFallbackLanguage(optionsProvider))
            .WithTrackingName(TranslationGeneratorTrackingNames.FallbackLanguage);

        var modelSet = parsedFiles
            .Collect()
            .Combine(fallbackLanguage)
            .Select(static (pair, _) => TranslationModelSetFactory.Create(pair.Left, pair.Right))
            .WithTrackingName(TranslationGeneratorTrackingNames.TranslationModelSet);

        context.RegisterSourceOutput(modelSet, static (productionContext, translations) => GenerateSources(productionContext, translations));
    }

    private static void GenerateSources(SourceProductionContext context, TranslationModelSet translations)
    {
        TranslationGeneratorUtilities.ReportDiagnostics(context, translations.Diagnostics);
        if (!translations.HasFallbackFile)
        {
            return;
        }

        I18NLanguageGenerator.AddSource(context, translations);
        I18NLanguageKeyGenerator.AddSource(context, translations);
        I18NLanguageDictGenerator.AddSource(context, translations);
    }
}