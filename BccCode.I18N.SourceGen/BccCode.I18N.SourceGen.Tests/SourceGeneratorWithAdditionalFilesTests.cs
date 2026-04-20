using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using BccCode.I18N.SourceGen.Tests.Utils;
using Xunit;

namespace BccCode.I18N.SourceGen.Tests;

public class SourceGeneratorWithAdditionalFilesTests
{
  private const string NoJson = """
                                  {
                                    "message": {
                                      "hello1": "hei verden",
                                      "hello2": "{msg} verden"
                                    },
                                    "plural": {
                                      "car": "bil | biler",
                                      "apple": "ingen epler | et eple | {count} epler",
                                      "banana": "ingen bananer | {n} banan | {n} bananer"
                                    }
                                  }
                                  """;

  private const string EnJson = """
                                  {
                                    "message": {
                                      "hello1": "hello world",
                                      "hello2": "{msg} world"
                                    },
                                    "plural": {
                                      "car": "car | cars",
                                      "apple": "no apples | one apple | {count} apple",
                                      "banana": "no bananas | {n} banana | {n} bananas"
                                    }
                                  }
                                  """;

  private const string InvalidJson = """
                                       {
                                         "message": {
                                           "hello": "broken"
                                       """;

  [Fact]
  public void Generates_expected_sources_for_valid_translations()
  {
    var (_, runResult, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        CreateTranslationFiles());

    var generatedFiles = outputCompilation.SyntaxTrees
        .Select(static tree => Path.GetFileName(tree.FilePath))
        .Where(static fileName => fileName.EndsWith(".g.cs", StringComparison.Ordinal))
        .OrderBy(static fileName => fileName, StringComparer.Ordinal)
        .ToArray();

    Assert.Equal(new[] { "I18N.g.cs", "I18NStrings.g.cs", "Language.g.cs" }, generatedFiles);
    Assert.Empty(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult));

    var languageSource = GeneratorTestHelpers.GetGeneratedSource(outputCompilation, "Language.g.cs");
    Assert.Contains("public static class message", languageSource, StringComparison.Ordinal);
    Assert.Contains("public static string hello1 =>", languageSource, StringComparison.Ordinal);
    Assert.Contains("\"en\" => \"hello world\"", languageSource, StringComparison.Ordinal);
    Assert.Contains("public static string hello2(object msg) =>", languageSource, StringComparison.Ordinal);
    Assert.Contains("public static string apple(int count) =>", languageSource, StringComparison.Ordinal);

    var keySource = GeneratorTestHelpers.GetGeneratedSource(outputCompilation, "I18N.g.cs");
    Assert.Contains("public const string hello1 = \"message.hello1\";", keySource, StringComparison.Ordinal);
    Assert.Contains("public static class plural", keySource, StringComparison.Ordinal);

    var dictionarySource = GeneratorTestHelpers.GetGeneratedSource(outputCompilation, "I18NStrings.g.cs");
    Assert.Contains("[\"message.hello1\"] = \"hei verden\"", dictionarySource, StringComparison.Ordinal);
    Assert.Contains("public static string? GetStringOrNull(string key)", dictionarySource, StringComparison.Ordinal);
  }

  [Fact]
  public void Uses_custom_fallback_language_when_configured()
  {
    var (_, runResult, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        CreateTranslationFiles(),
        fallbackLanguage: "en");

    Assert.Empty(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult));

    var languageSource = GeneratorTestHelpers.GetGeneratedSource(outputCompilation, "Language.g.cs");
    Assert.Contains("\"no\" => \"hei verden\"", languageSource, StringComparison.Ordinal);
    Assert.Contains("_ => \"hello world\"", languageSource, StringComparison.Ordinal);

    var dictionarySource = GeneratorTestHelpers.GetGeneratedSource(outputCompilation, "I18NStrings.g.cs");
    Assert.Contains("culture = \"en\";", dictionarySource, StringComparison.Ordinal);
  }

  [Fact]
  public void Reports_invalid_json_and_skips_generation_when_fallback_file_cannot_be_parsed()
  {
    var (_, runResult, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [
            new TestAdditionalFile("./no.json", InvalidJson),
                new TestAdditionalFile("./en.json", EnJson)
        ]);

    var diagnostics = GeneratorTestHelpers.GetGeneratorDiagnostics(runResult);
    Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "I18N001");
    Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "I18N004");

    var generatedFiles = outputCompilation.SyntaxTrees
        .Select(static tree => Path.GetFileName(tree.FilePath))
        .Where(static fileName => fileName.EndsWith(".g.cs", StringComparison.Ordinal))
        .ToArray();

    Assert.Empty(generatedFiles);
  }

  [Fact]
  public void Reports_missing_fallback_language_when_requested_file_does_not_exist()
  {
    var (_, runResult, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [new TestAdditionalFile("./no.json", NoJson)],
        fallbackLanguage: "en");

    var diagnostics = GeneratorTestHelpers.GetGeneratorDiagnostics(runResult);
    Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "I18N004");
    Assert.Empty(outputCompilation.SyntaxTrees.Where(static tree => Path.GetFileName(tree.FilePath).EndsWith(".g.cs", StringComparison.Ordinal)));
  }

  [Fact]
  public void Caches_incremental_pipeline_outputs_on_second_run()
  {
    var (firstRun, secondRun) = GeneratorTestHelpers.RunGeneratorTwice(
        new I18NSourceGenerator(),
        CreateTranslationFiles());

    var firstResult = Assert.Single(firstRun.Results);
    var secondResult = Assert.Single(secondRun.Results);

    Assert.Contains("ParsedTranslationFile", secondResult.TrackedSteps.Keys);
    Assert.Contains("FallbackLanguage", secondResult.TrackedSteps.Keys);
    Assert.Contains("TranslationModelSet", secondResult.TrackedSteps.Keys);

    foreach (var trackingName in secondResult.TrackedSteps.Keys)
    {
      Assert.Equal(firstResult.TrackedSteps[trackingName].Length, secondResult.TrackedSteps[trackingName].Length);

      foreach (var runStep in secondResult.TrackedSteps[trackingName])
      {
        Assert.All(
            runStep.Outputs,
            output => Assert.True(
                output.Reason == IncrementalStepRunReason.Cached || output.Reason == IncrementalStepRunReason.Unchanged,
                $"Expected tracked step '{trackingName}' to be cached or unchanged on the second run, but saw {output.Reason}."));
      }
    }

    Assert.All(
        secondResult.TrackedOutputSteps.SelectMany(static step => step.Value).SelectMany(static step => step.Outputs),
        output => Assert.True(
            output.Reason == IncrementalStepRunReason.Cached || output.Reason == IncrementalStepRunReason.Unchanged,
            $"Expected generated output to be cached or unchanged on the second run, but saw {output.Reason}."));
  }

  private static TestAdditionalFile[] CreateTranslationFiles() =>
  [
      new TestAdditionalFile("./no.json", NoJson),
        new TestAdditionalFile("./en.json", EnJson)
  ];
}