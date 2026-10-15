using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using BccCode.I18N.SourceGen.Tests.Utils;
using Microsoft.CodeAnalysis;
using Xunit;

namespace BccCode.I18N.SourceGen.Tests;

public class LanguageMatchingTests
{
  private const string GeneratedNamespace = "BccCode.I18N.Tests.Generated";

  [Theory]
  [InlineData("nb-NO", "no")]
  [InlineData("nb", "no")]
  [InlineData("nn-NO", "no")]
  [InlineData("no", "no")]
  [InlineData("no-NO", "no")]
  [InlineData("en-US", "en")]
  [InlineData("EN-gb", "en")]
  [InlineData("de-DE", "en")]
  [InlineData("de", "en")]
  [InlineData("", "en")]
  public void Resolves_cultures_with_default_aliases(string culture, string expected)
  {
    var resolver = Generate(["en", "no"], fallback: "en");

    Assert.Equal(expected, resolver.Resolve(culture));
  }

  [Fact]
  public void Region_specific_file_wins_over_the_neutral_file()
  {
    var resolver = Generate(["en", "pt", "pt-br"], fallback: "en");

    Assert.Equal("pt-br", resolver.Resolve("pt-BR"));
    Assert.Equal("pt", resolver.Resolve("pt-PT"));
    Assert.Equal("pt", resolver.Resolve("pt"));
  }

  [Fact]
  public void Region_specific_culture_resolves_to_the_neutral_file()
  {
    var resolver = Generate(["en", "pt"], fallback: "en");

    Assert.Equal("pt", resolver.Resolve("pt-BR"));
  }

  [Fact]
  public void Walks_the_parent_chain_for_chinese()
  {
    // zh-TW > zh-Hant > zh
    Assert.Equal("zh-hant", Generate(["en", "zh", "zh-hant"], fallback: "en").Resolve("zh-TW"));
    Assert.Equal("zh", Generate(["en", "zh"], fallback: "en").Resolve("zh-TW"));
    Assert.Equal("zh-tw", Generate(["en", "zh", "zh-tw"], fallback: "en").Resolve("zh-TW"));
  }

  [Fact]
  public void Default_aliases_whose_target_is_missing_are_ignored_silently()
  {
    var resolver = Generate(["en", "nb"], fallback: "en");

    Assert.Equal("en", resolver.Resolve("nn-NO"));
    Assert.Equal("nb", resolver.Resolve("no"));
    Assert.Empty(resolver.Diagnostics);

    var onlyEnglish = Generate(["en"], fallback: "en");
    Assert.Equal("en", onlyEnglish.Resolve("nb-NO"));
    Assert.Empty(onlyEnglish.Diagnostics);
  }

  [Fact]
  public void Alias_is_not_applied_when_the_culture_has_its_own_file()
  {
    var resolver = Generate(["en", "nb", "no"], fallback: "en");

    Assert.Equal("nb", resolver.Resolve("nb-NO"));
    Assert.Equal("no", resolver.Resolve("no"));
    Assert.Equal("no", resolver.Resolve("nn"));
  }

  [Fact]
  public void User_aliases_are_parsed_trimmed_case_insensitive_and_skip_empty_entries()
  {
    var resolver = Generate(["en", "sv", "da"], fallback: "en", aliases: " NB = SV ;; nn=da ; ");

    Assert.Equal("sv", resolver.Resolve("nb-NO"));
    Assert.Equal("da", resolver.Resolve("nn-NO"));
    Assert.Empty(resolver.Diagnostics);
  }

  [Fact]
  public void User_alias_overrides_the_default_for_the_same_key()
  {
    var resolver = Generate(["en", "no", "sv"], fallback: "en", aliases: "nb=sv");

    Assert.Equal("sv", resolver.Resolve("nb-NO"));
    Assert.Equal("no", resolver.Resolve("nn-NO"));
    Assert.Empty(resolver.Diagnostics);
  }

  [Fact]
  public void User_alias_can_map_a_region_culture()
  {
    var resolver = Generate(["en", "es"], fallback: "en", aliases: "ca=es;gl-es=es");

    Assert.Equal("es", resolver.Resolve("ca-ES"));
    Assert.Equal("es", resolver.Resolve("gl-ES"));
  }

  [Fact]
  public void Reports_I18N009_when_a_user_alias_target_has_no_file()
  {
    var resolver = Generate(["en", "no"], fallback: "en", aliases: "sv=da");

    var diagnostic = Assert.Single(resolver.Diagnostics);
    Assert.Equal("I18N009", diagnostic.Id);
    Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    Assert.Contains("sv=da", diagnostic.GetMessage(), StringComparison.Ordinal);
    Assert.Equal("en", resolver.Resolve("sv"));
  }

  [Fact]
  public void Reports_I18N009_when_a_user_alias_overrides_a_default_with_a_missing_target()
  {
    var resolver = Generate(["en", "no"], fallback: "en", aliases: "nb=da");

    Assert.Equal("I18N009", Assert.Single(resolver.Diagnostics).Id);
  }

  [Theory]
  [InlineData("nb")]
  [InlineData("nb=")]
  [InlineData("=no")]
  public void Reports_I18N009_for_malformed_entries(string entry)
  {
    var resolver = Generate(["en", "no"], fallback: "en", aliases: entry + ";nn=no");

    var diagnostic = Assert.Single(resolver.Diagnostics);
    Assert.Equal("I18N009", diagnostic.Id);
    Assert.Contains("malformed", diagnostic.GetMessage(), StringComparison.Ordinal);
    Assert.Equal("no", resolver.Resolve("nn-NO"));
  }

  [Fact]
  public void ResolveLanguage_is_generated_without_static_state_and_without_allocating()
  {
    var (_, _, compilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        CreateFiles(["en", "no"]),
        fallbackLanguage: "en");

    var source = GeneratorTestHelpers.GetGeneratedSource(compilation, "LanguageStrings.g.cs");
    var languageSource = GeneratorTestHelpers.GetGeneratedSource(compilation, "Language.g.cs");

    Assert.Contains("public static string ResolveLanguage(global::System.Globalization.CultureInfo culture)", source, StringComparison.Ordinal);
    Assert.Contains("global::System.StringComparison.OrdinalIgnoreCase", source, StringComparison.Ordinal);
    Assert.DoesNotContain("ToLower", source, StringComparison.Ordinal);
    Assert.DoesNotContain("static readonly", source, StringComparison.Ordinal);
    Assert.DoesNotContain("static LanguageStrings(", source, StringComparison.Ordinal);
    Assert.DoesNotContain("TwoLetterISOLanguageName", languageSource, StringComparison.Ordinal);
    Assert.Contains(
        "LanguageStrings.ResolveLanguage(global::System.Globalization.CultureInfo.CurrentUICulture)",
        languageSource,
        StringComparison.Ordinal);
  }

  [Fact]
  public void GetStringOrNull_falls_back_per_key_to_the_fallback_language()
  {
    var (_, _, compilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [
            new TestAdditionalFile("./en.json", """{ "both": "english", "onlyEn": "only english" }"""),
            new TestAdditionalFile("./no.json", """{ "both": "norsk" }""")
        ],
        fallbackLanguage: "en");

    var strings = LoadType(compilation, GeneratedNamespace + ".LanguageStrings");
    var getStringOrNull = strings.GetMethod("GetStringOrNull", [typeof(string)])!;
    var getString = strings.GetMethod("GetString", [typeof(string)])!;
    var original = CultureInfo.CurrentUICulture;
    try
    {
      CultureInfo.CurrentUICulture = new CultureInfo("nb-NO");
      Assert.Equal("norsk", getStringOrNull.Invoke(null, ["both"]));
      Assert.Equal("only english", getStringOrNull.Invoke(null, ["onlyEn"]));
      Assert.Null(getStringOrNull.Invoke(null, ["missing"]));
      Assert.Equal("missing", getString.Invoke(null, ["missing"]));
    }
    finally
    {
      CultureInfo.CurrentUICulture = original;
    }
  }

  [Fact]
  public void Model_is_cached_when_the_input_is_unchanged()
  {
    var (_, secondRun) = GeneratorTestHelpers.RunGeneratorTwice(
        new I18NSourceGenerator(),
        CreateFiles(["en", "no"]),
        fallbackLanguage: "en");

    var secondResult = Assert.Single(secondRun.Results);
    Assert.All(
        secondResult.TrackedSteps["TranslationModelSet"].SelectMany(static step => step.Outputs),
        static output => Assert.Equal(IncrementalStepRunReason.Cached, output.Reason));
  }

  private static TestAdditionalFile[] CreateFiles(string[] languages) =>
      languages.Select(static language => new TestAdditionalFile($"./{language}.json", """{ "hello": "hi" }""")).ToArray();

  private static Resolver Generate(string[] languages, string fallback, string? aliases = null)
  {
    var (_, runResult, compilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        CreateFiles(languages),
        fallbackLanguage: fallback,
        languageAliases: aliases);

    var diagnostics = GeneratorTestHelpers.GetGeneratorDiagnostics(runResult).ToArray();
    var method = LoadType(compilation, GeneratedNamespace + ".LanguageStrings").GetMethod("ResolveLanguage")!;
    return new Resolver(method, diagnostics);
  }

  private static Type LoadType(Compilation compilation, string typeName)
  {
    using var stream = new MemoryStream();
    var emitResult = compilation.Emit(stream);
    Assert.True(
        emitResult.Success,
        string.Join(Environment.NewLine, emitResult.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));

    stream.Position = 0;
    var assembly = new AssemblyLoadContext(null, isCollectible: true).LoadFromStream(stream);
    return assembly.GetType(typeName, throwOnError: true)!;
  }

  private sealed class Resolver
  {
    private readonly MethodInfo _method;

    public Resolver(MethodInfo method, Diagnostic[] diagnostics)
    {
      _method = method;
      Diagnostics = diagnostics;
    }

    public Diagnostic[] Diagnostics { get; }

    public string Resolve(string cultureName) =>
        (string)_method.Invoke(null, [CultureInfo.GetCultureInfo(cultureName)])!;
  }
}
