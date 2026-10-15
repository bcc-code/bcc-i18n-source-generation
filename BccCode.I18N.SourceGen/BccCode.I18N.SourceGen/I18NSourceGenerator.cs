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

        var generatorOptions = context.AnalyzerConfigOptionsProvider
            .Select(static (optionsProvider, _) => TranslationFileParser.GetOptions(optionsProvider))
            .WithTrackingName(TranslationGeneratorTrackingNames.FallbackLanguage);

        var modelSet = parsedFiles
            .Collect()
            .Combine(generatorOptions)
            .Select(static (pair, _) => TranslationModelSetFactory.Create(pair.Left, pair.Right))
            .WithTrackingName(TranslationGeneratorTrackingNames.TranslationModelSet);

        // Diagnostics of the whole model set are reported from this single output.
        var diagnostics = modelSet
            .Select(static (translations, _) => translations.Diagnostics)
            .WithTrackingName(TranslationGeneratorTrackingNames.ModelSetDiagnostics);

        context.RegisterSourceOutput(diagnostics, static (productionContext, modelDiagnostics) =>
            TranslationGeneratorUtilities.ReportDiagnostics(productionContext, modelDiagnostics));

        // LanguageKeys only depends on the fallback file's entries and the namespace, so edits to
        // other language files leave this output unchanged.
        var keysModel = modelSet
            .Select(static (translations, _) => translations.HasFallbackFile
                ? (LanguageKeysModel?)new LanguageKeysModel(translations.GeneratedNamespace, translations.FallbackFile.Entries)
                : null)
            .WithTrackingName(TranslationGeneratorTrackingNames.LanguageKeysModel);

        context.RegisterSourceOutput(keysModel, static (productionContext, model) =>
        {
            if (model is { } keys)
            {
                I18NLanguageKeyGenerator.AddSource(productionContext, keys);
            }
        });

        context.RegisterSourceOutput(modelSet, static (productionContext, translations) =>
        {
            if (translations.HasFallbackFile)
            {
                I18NLanguageGenerator.AddSource(productionContext, translations);
            }
        });

        context.RegisterSourceOutput(modelSet, static (productionContext, translations) =>
        {
            if (translations.HasFallbackFile)
            {
                I18NLanguageDictGenerator.AddSource(productionContext, translations);
            }
        });
    }
}
