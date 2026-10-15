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

    Assert.Equal(
        ["Language.g.cs", "LanguageKeys.g.cs", "LanguageStrings.g.cs", "LanguageTranslations.g.cs", "LocalizedLanguage.g.cs", "LocalizedLanguageGroups.g.cs"],
        generatedFiles);
    Assert.Empty(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult));

    var languageSource = GetLanguageSource(outputCompilation);
    Assert.Contains("namespace BccCode.I18N.Tests.Generated", languageSource, StringComparison.Ordinal);
    Assert.Contains("public static class message", languageSource, StringComparison.Ordinal);
    Assert.Contains("public static string hello1 =>", languageSource, StringComparison.Ordinal);
    Assert.Contains("\"no\" => \"hei verden\"", languageSource, StringComparison.Ordinal);
    Assert.Contains("_ => \"hello world\"", languageSource, StringComparison.Ordinal);
    Assert.Contains("public static string hello2(object msg) =>", languageSource, StringComparison.Ordinal);
    Assert.Contains("public static string apple(int count) =>", languageSource, StringComparison.Ordinal);

    var keySource = GeneratorTestHelpers.GetGeneratedSource(outputCompilation, "LanguageKeys.g.cs");
    Assert.Contains("namespace BccCode.I18N.Tests.Generated", keySource, StringComparison.Ordinal);
    Assert.Contains("public const string hello1 = \"message.hello1\";", keySource, StringComparison.Ordinal);
    Assert.Contains("public static class plural", keySource, StringComparison.Ordinal);

    var dictionarySource = GeneratorTestHelpers.GetGeneratedSource(outputCompilation, "LanguageStrings.g.cs");
    Assert.Contains("namespace BccCode.I18N.Tests.Generated", dictionarySource, StringComparison.Ordinal);
    Assert.Contains("\"message.hello1\" => \"hei verden\",", dictionarySource, StringComparison.Ordinal);
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

    var languageSource = GetLanguageSource(outputCompilation);
    Assert.Contains("\"no\" => \"hei verden\"", languageSource, StringComparison.Ordinal);
    Assert.Contains("_ => \"hello world\"", languageSource, StringComparison.Ordinal);

    var dictionarySource = GeneratorTestHelpers.GetGeneratedSource(outputCompilation, "LanguageStrings.g.cs");
    Assert.Contains("\"no\" => GetStringOrNull1(key) ?? GetStringOrNull0(key),", dictionarySource, StringComparison.Ordinal);
    Assert.Contains("_ => GetStringOrNull0(key),", dictionarySource, StringComparison.Ordinal);
  }

  [Fact]
  public void Uses_the_configured_fallback_language_for_LanguageStrings_lookup()
  {
    var (_, _, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        CreateTranslationFiles(),
        fallbackLanguage: "no");

    var dictionarySource = GeneratorTestHelpers.GetGeneratedSource(outputCompilation, "LanguageStrings.g.cs");
    Assert.Contains("\"en\" => GetStringOrNull0(key) ?? GetStringOrNull1(key),", dictionarySource, StringComparison.Ordinal);
    Assert.Contains("_ => GetStringOrNull1(key),", dictionarySource, StringComparison.Ordinal);
  }

  [Fact]
  public void LanguageStrings_has_no_static_field_initializers_so_hot_reload_can_apply_translation_edits()
  {
    var (_, _, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        CreateTranslationFiles());

    var dictionarySource = GeneratorTestHelpers.GetGeneratedSource(outputCompilation, "LanguageStrings.g.cs");
    Assert.DoesNotContain("static readonly", dictionarySource, StringComparison.Ordinal);
    Assert.DoesNotContain("Dictionary", dictionarySource, StringComparison.Ordinal);
  }

  [Fact]
  public void LanguageStrings_compiles_when_a_key_path_is_defined_twice()
  {
    var (_, _, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [new TestAdditionalFile("./en.json", """{ "a.b": "flat", "a": { "b": "nested" } }""")],
        fallbackLanguage: "en");

    Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    var dictionarySource = GeneratorTestHelpers.GetGeneratedSource(outputCompilation, "LanguageStrings.g.cs");
    Assert.Single(dictionarySource.Split('\n'), static line => line.TrimStart().StartsWith("\"a.b\" =>", StringComparison.Ordinal));
  }

  [Fact]
  public void Reports_invalid_json_and_skips_generation_when_fallback_file_cannot_be_parsed()
  {
    var (_, runResult, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [
            new TestAdditionalFile("./en.json", InvalidJson),
                new TestAdditionalFile("./no.json", NoJson)
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
    Assert.DoesNotContain(outputCompilation.SyntaxTrees, static tree => Path.GetFileName(tree.FilePath).EndsWith(".g.cs", StringComparison.Ordinal));
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

    // The compilation itself is a new object in every run, so that built-in step is always modified.
    foreach (var trackingName in secondResult.TrackedSteps.Keys.Where(static name => name != "Compilation"))
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

  private const string DeJson = """{ "message": { "hello1": "hallo welt" } }""";

  [Fact]
  public void Only_marked_files_are_translations_when_any_file_is_marked()
  {
    var (_, runResult, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [
            new TestAdditionalFile("./en.json", EnJson, "true"),
            new TestAdditionalFile("./no.json", NoJson, "true"),
            new TestAdditionalFile("./settings.json", "[1,2]"),
            new TestAdditionalFile("./de.json", DeJson)
        ],
        fallbackLanguage: "en");

    Assert.Empty(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult));

    var languageSource = GetLanguageSource(outputCompilation);
    Assert.Contains("\"no\" => \"hei verden\"", languageSource, StringComparison.Ordinal);
    Assert.DoesNotContain("\"de\"", languageSource, StringComparison.Ordinal);

    var dictionarySource = GeneratorTestHelpers.GetGeneratedSource(outputCompilation, "LanguageStrings.g.cs");
    Assert.DoesNotContain("\"de\"", dictionarySource, StringComparison.Ordinal);
  }

  [Fact]
  public void All_json_files_are_translations_when_no_file_is_marked()
  {
    var (_, runResult, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [
            new TestAdditionalFile("./en.json", EnJson),
            new TestAdditionalFile("./no.json", NoJson),
            new TestAdditionalFile("./de.json", DeJson)
        ],
        fallbackLanguage: "en");

    Assert.Empty(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult));

    var languageSource = GetLanguageSource(outputCompilation);
    Assert.Contains("\"de\" => \"hallo welt\"", languageSource, StringComparison.Ordinal);
  }

  [Theory]
  [InlineData("True", true)]
  [InlineData(" true ", true)]
  [InlineData("false", false)]
  [InlineData("", false)]
  public void Metadata_value_decides_whether_a_file_is_marked(string metadata, bool expectedMarked)
  {
    var (_, _, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [
            new TestAdditionalFile("./en.json", EnJson, "true"),
            new TestAdditionalFile("./de.json", DeJson, metadata)
        ],
        fallbackLanguage: "en");

    var languageSource = GetLanguageSource(outputCompilation);
    Assert.Equal(expectedMarked, languageSource.Contains("\"de\" => \"hallo welt\"", StringComparison.Ordinal));
  }

  [Fact]
  public void Marked_file_that_is_not_json_is_ignored()
  {
    var (_, runResult, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [
            new TestAdditionalFile("./en.json", EnJson, "true"),
            new TestAdditionalFile("./de.txt", "not json", "true")
        ],
        fallbackLanguage: "en");

    Assert.Empty(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult));
    Assert.Contains(outputCompilation.SyntaxTrees, static tree => Path.GetFileName(tree.FilePath) == "Language.g.cs");
  }

  [Fact]
  public void Editing_an_unmarked_file_leaves_translation_outputs_unchanged()
  {
    var noFile = new TestAdditionalFile("./no.json", NoJson, "true");
    var enFile = new TestAdditionalFile("./en.json", EnJson, "true");
    var settingsFile = new TestAdditionalFile("./settings.json", """{ "a": [1] }""");

    var (driver, _, _) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [noFile, enFile, settingsFile],
        fallbackLanguage: "en",
        trackIncrementalSteps: true);

    driver = driver
        .ReplaceAdditionalText(settingsFile, new TestAdditionalFile("./settings.json", """{ "a": [2] }"""))
        .RunGenerators(GeneratorTestHelpers.CreateCompilation());

    var result = Assert.Single(driver.GetRunResult().Results);

    foreach (var trackingName in new[] { "ParsedTranslationFile", "TranslationModelSet" })
    {
      Assert.All(
          result.TrackedSteps[trackingName].SelectMany(static step => step.Outputs),
          output => Assert.True(
              output.Reason == IncrementalStepRunReason.Cached || output.Reason == IncrementalStepRunReason.Unchanged,
              $"Expected '{trackingName}' to be cached or unchanged, but saw {output.Reason}."));
    }

    Assert.All(
        result.TrackedOutputSteps.SelectMany(static step => step.Value).SelectMany(static step => step.Outputs),
        output => Assert.True(
            output.Reason == IncrementalStepRunReason.Cached || output.Reason == IncrementalStepRunReason.Unchanged,
            $"Expected generated output to be cached or unchanged, but saw {output.Reason}."));
  }

  [Fact]
  public void Generates_plural_method_when_only_a_non_fallback_language_is_plural()
  {
    var (_, runResult, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [
            new TestAdditionalFile("./en.json", """{ "item": "item", "owned": "{name} owns it" }"""),
            new TestAdditionalFile("./no.json", """{ "item": "ingen ting | én ting | {count} ting", "owned": "{name} eier én | {name} eier {count}" }""")
        ],
        fallbackLanguage: "en");

    Assert.Empty(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult));
    Assert.Empty(outputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

    var languageSource = GetLanguageSource(outputCompilation);
    Assert.Contains("public static string item(int count) =>", languageSource, StringComparison.Ordinal);
    Assert.Contains("(0, \"no\") => \"ingen ting\",", languageSource, StringComparison.Ordinal);
    Assert.Contains("(_, \"no\") => string.Format(__provider, \"{0} ting\", count),", languageSource, StringComparison.Ordinal);
    Assert.Contains("(_, _) => \"item\",", languageSource, StringComparison.Ordinal);

    Assert.Contains("public static string owned(int count, object name) =>", languageSource, StringComparison.Ordinal);
    Assert.Contains("(_, _) => string.Format(__provider, \"{1} owns it\", count, name),", languageSource, StringComparison.Ordinal);
  }

  [Fact]
  public void Skips_all_plural_variants_of_a_language_when_one_variant_has_an_unknown_placeholder()
  {
    var (_, runResult, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [
            new TestAdditionalFile("./en.json", """{ "item": "item" }"""),
            new TestAdditionalFile("./no.json", """{ "item": "ok | {missing}" }""")
        ],
        fallbackLanguage: "en");

    Assert.Contains(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult), static diagnostic => diagnostic.Id == "I18N006");

    var languageSource = GetLanguageSource(outputCompilation);
    Assert.Contains("public static string item(int count) =>", languageSource, StringComparison.Ordinal);
    Assert.DoesNotContain("\"no\"", languageSource, StringComparison.Ordinal);
    Assert.Contains("(_, _) => \"item\",", languageSource, StringComparison.Ordinal);
  }

  [Fact]
  public void Generates_single_form_case_when_only_the_fallback_language_is_plural()
  {
    var (_, runResult, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [
            new TestAdditionalFile("./en.json", """{ "car": "car | cars" }"""),
            new TestAdditionalFile("./no.json", """{ "car": "bil" }""")
        ],
        fallbackLanguage: "en");

    Assert.Empty(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult));
    Assert.Empty(outputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

    var languageSource = GetLanguageSource(outputCompilation);
    Assert.Contains("public static string car(int count) =>", languageSource, StringComparison.Ordinal);
    Assert.Contains("(_, \"no\") => \"bil\",", languageSource, StringComparison.Ordinal);
    Assert.Contains("(1, _) => \"car\",", languageSource, StringComparison.Ordinal);
    Assert.Contains("(_, _) => \"cars\",", languageSource, StringComparison.Ordinal);
  }

  [Fact]
  public void Editing_a_non_fallback_file_leaves_keys_output_unchanged()
  {
    var noFile = new TestAdditionalFile("./no.json", NoJson);
    var enFile = new TestAdditionalFile("./en.json", EnJson);

    var (driver, _, _) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [noFile, enFile],
        fallbackLanguage: "en",
        trackIncrementalSteps: true);

    var changedNoFile = new TestAdditionalFile("./no.json", NoJson.Replace("hei verden", "hallo verden", StringComparison.Ordinal));
    driver = driver
        .ReplaceAdditionalText(noFile, changedNoFile)
        .RunGenerators(GeneratorTestHelpers.CreateCompilation());

    var result = Assert.Single(driver.GetRunResult().Results);
    // All source outputs are tracked under "SourceOutput", in registration order: diagnostics, keys, language, strings, string localizer.
    var outputSteps = Assert.Single(result.TrackedOutputSteps).Value;
    Assert.Equal(5, outputSteps.Length);
    var keysOutput = outputSteps[1];
    var languageOutput = outputSteps[2];
    var stringsOutput = outputSteps[3];

    Assert.All(
        keysOutput.Outputs,
        output => Assert.True(
            output.Reason == IncrementalStepRunReason.Cached || output.Reason == IncrementalStepRunReason.Unchanged,
            $"Expected LanguageKeys output to be cached or unchanged, but saw {output.Reason}."));

    Assert.Contains(
        languageOutput.Outputs,
        static output => output.Reason == IncrementalStepRunReason.Modified);
    Assert.Contains(
        stringsOutput.Outputs,
        static output => output.Reason == IncrementalStepRunReason.Modified);
  }

  [Fact]
  public void Generated_files_start_with_auto_generated_header_and_carry_generated_code_attribute()
  {
    var (_, _, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        CreateTranslationFiles(),
        fallbackLanguage: "en");

    foreach (var fileName in new[] { "Language.g.cs", "LanguageKeys.g.cs", "LanguageStrings.g.cs", "LanguageTranslations.g.cs", "LocalizedLanguage.g.cs", "LocalizedLanguageGroups.g.cs" })
    {
      var source = GeneratorTestHelpers.GetGeneratedSource(outputCompilation, fileName);
      var lines = source.Split('\n');
      Assert.Equal("// <auto-generated/>", lines[0].TrimEnd('\r'));
      Assert.Equal("#nullable enable", lines[1].TrimEnd('\r'));
      Assert.DoesNotContain("using ", source, StringComparison.Ordinal);
      Assert.Contains("[global::System.CodeDom.Compiler.GeneratedCode(\"BccCode.I18N.SourceGen\", \"", source, StringComparison.Ordinal);
    }
  }

  [Fact]
  public void Generated_code_compiles_when_keys_are_named_like_framework_types()
  {
    var (_, runResult, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [
            new TestAdditionalFile("./en.json", """{ "CultureInfo": { "name": "culture {x}" }, "System": { "String": "text", "Globalization": { "item": "a | b" } }, "Dictionary": { "Entry": "d" } }"""),
            new TestAdditionalFile("./no.json", """{ "CultureInfo": { "name": "kultur {x}" }, "System": { "String": "tekst", "Globalization": { "item": "a | b | c" } }, "Dictionary": { "Entry": "d" } }""")
        ],
        fallbackLanguage: "en");

    Assert.Empty(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult));
    Assert.Empty(outputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

    var languageSource = GetLanguageSource(outputCompilation);
    Assert.Contains("public static class CultureInfo", languageSource, StringComparison.Ordinal);
    Assert.Contains("public static class System", languageSource, StringComparison.Ordinal);
  }

  [Fact]
  public void Placeholder_names_that_are_keywords_or_start_with_a_digit_are_valid_identifiers()
  {
    var (_, runResult, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [
            new TestAdditionalFile("./en.json", """{ "greeting": "{class} meets {1st}", "items": "{class} item | {class} items" }"""),
            new TestAdditionalFile("./no.json", """{ "greeting": "{class} møter {1st}", "items": "{class} ting | {class} tinger" }""")
        ],
        fallbackLanguage: "en");

    Assert.Empty(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult));
    Assert.Empty(outputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

    var languageSource = GetLanguageSource(outputCompilation);
    Assert.Contains("public static string greeting(object @class, object _1st) =>", languageSource, StringComparison.Ordinal);
    Assert.Contains("\"{0} meets {1}\", @class, _1st)", languageSource, StringComparison.Ordinal);
    Assert.Contains("public static string items(int @class) =>", languageSource, StringComparison.Ordinal);
  }

  [Fact]
  public void Placeholders_that_sanitize_to_the_same_identifier_get_unique_parameter_names()
  {
    var (_, runResult, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [
            new TestAdditionalFile("./en.json", """{ "rank": "{1st} then {_1st}" }"""),
            new TestAdditionalFile("./no.json", """{ "rank": "{_1st} så {1st}" }""")
        ],
        fallbackLanguage: "en");

    Assert.Empty(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult));
    Assert.Empty(outputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

    var languageSource = GetLanguageSource(outputCompilation);
    Assert.Contains("public static string rank(object _1st, object _1st2) =>", languageSource, StringComparison.Ordinal);
    Assert.Contains("\"{0} then {1}\", _1st, _1st2)", languageSource, StringComparison.Ordinal);
    Assert.Contains("\"{1} så {0}\", _1st, _1st2)", languageSource, StringComparison.Ordinal);
  }

  [Fact]
  public void Placeholder_mismatch_warning_points_at_the_translation_file()
  {
    var (_, runResult, _) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [
            new TestAdditionalFile("./en.json", """{ "hello": "hi" }"""),
            new TestAdditionalFile("./no.json", """{ "hello": "hei {name}" }""")
        ],
        fallbackLanguage: "en");

    var diagnostic = Assert.Single(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult), static diagnostic => diagnostic.Id == "I18N006");
    Assert.Equal("./no.json", diagnostic.Location.GetLineSpan().Path);
  }

  [Fact]
  public void Reports_model_diagnostics_only_once()
  {
    var (_, runResult, _) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [new TestAdditionalFile("./no.json", NoJson)],
        fallbackLanguage: "en");

    Assert.Single(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult), static diagnostic => diagnostic.Id == "I18N004");
  }

  // The static Language members delegate to LanguageTranslations, which holds the translation switches.
  private static string GetLanguageSource(Compilation compilation) =>
      GeneratorTestHelpers.GetGeneratedSource(compilation, "Language.g.cs") +
      GeneratorTestHelpers.GetGeneratedSource(compilation, "LanguageTranslations.g.cs");

  private static TestAdditionalFile[] CreateTranslationFiles() =>
  [
      new TestAdditionalFile("./no.json", NoJson),
        new TestAdditionalFile("./en.json", EnJson)
  ];
}