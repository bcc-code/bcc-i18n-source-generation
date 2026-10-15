using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using BccCode.I18N.SourceGen.Tests.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BccCode.I18N.SourceGen.Tests;

/// <summary>Compiles the generated code together with a small consumer and runs it.</summary>
public class LocalizedLanguageTests
{
  private const string EnJson = """
                                {
                                  "message": { "hello": "hello world", "greet": "hi {name}" },
                                  "plural": { "car": "car | cars" },
                                  "deep": { "inner": { "leaf": "deep en" } },
                                  "onlyEn": "only english"
                                }
                                """;

  private const string NoJson = """
                                {
                                  "message": { "hello": "hei verden", "greet": "hei {name}" },
                                  "plural": { "car": "bil | biler" },
                                  "deep": { "inner": { "leaf": "dyp no" } }
                                }
                                """;

  [Fact]
  public void LocalizedLanguage_selects_the_language_from_the_given_culture()
  {
    var result = Run(
        [new TestAdditionalFile("./en.json", EnJson), new TestAdditionalFile("./no.json", NoJson)],
        """
        string Hello(string culture) => new LocalizedLanguage(new CultureInfo(culture)).message.hello;
        return string.Join("|", Hello("nb-NO"), Hello("nn"), Hello("no"), Hello("en-US"), Hello("de-DE"), Hello(""));
        """);

    Assert.Equal("hei verden|hei verden|hei verden|hello world|hello world|hello world", result);
  }

  [Fact]
  public void LocalizedLanguage_mirrors_nested_groups_parameters_and_plurals()
  {
    var result = Run(
        [new TestAdditionalFile("./en.json", EnJson), new TestAdditionalFile("./no.json", NoJson)],
        """
        var no = new LocalizedLanguage(new CultureInfo("nb-NO"));
        var en = new LocalizedLanguage(new CultureInfo("en-US"));
        return string.Join("|", no.deep.inner.leaf, en.deep.inner.leaf, no.message.greet("Kari"), en.message.greet("Kari"),
            no.plural.car(0), no.plural.car(3), en.plural.car(0), en.plural.car(5), no.onlyEn);
        """);

    Assert.Equal("dyp no|deep en|hei Kari|hi Kari|bil|biler|car|cars|only english", result);
  }

  [Fact]
  public void LocalizedLanguage_does_not_change_or_use_the_thread_culture_for_language_selection()
  {
    var result = Run(
        [new TestAdditionalFile("./en.json", EnJson), new TestAdditionalFile("./no.json", NoJson)],
        """
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");
        var no = new LocalizedLanguage(new CultureInfo("nb-NO"));
        return no.message.hello + "|" + Language.message.hello + "|" + CultureInfo.CurrentUICulture.Name;
        """);

    Assert.Equal("hei verden|hello world|en-US", result);
  }

  [Fact]
  public void Default_LocalizedLanguage_uses_the_fallback_language_with_invariant_formatting()
  {
    var result = Run(
        [
            new TestAdditionalFile("./en.json", """{ "hello": "hello", "when": "due {date:d}", "n": "{amount:N2}" }"""),
            new TestAdditionalFile("./no.json", """{ "hello": "hei", "when": "frist {date:d}", "n": "{amount:N2}" }""")
        ],
        """
        CultureInfo.CurrentCulture = new CultureInfo("nb-NO");
        CultureInfo.CurrentUICulture = new CultureInfo("nb-NO");
        var language = default(LocalizedLanguage);
        return string.Join("|", language.hello, language.when(new DateTime(2024, 1, 31)), language.n(1234.5m));
        """);

    Assert.Equal("hello|due 01/31/2024|1,234.50", result);
  }

  [Fact]
  public void Group_types_do_not_collide_for_key_paths_that_sanitize_to_the_same_name()
  {
    var result = Run(
        [new TestAdditionalFile("./en.json", """{ "a_b": { "x": "flat" }, "a": { "b": { "x": "nested" } } }""")],
        """
        var language = new LocalizedLanguage(CultureInfo.InvariantCulture);
        return language.a_b.x + "|" + language.a.b.x + "|" + Language.a_b.x + "|" + Language.a.b.x;
        """);

    Assert.Equal("flat|nested|flat|nested", result);
  }

  [Fact]
  public void Keys_named_like_generated_types_compile_and_work()
  {
    var result = Run(
        [
            new TestAdditionalFile(
                "./en.json",
                """
                {
                  "LanguageStrings": "ls",
                  "LanguageTranslations": { "x": "translations" },
                  "LocalizedLanguageGroups": { "y": "groups" },
                  "System": { "global": "sys" },
                  "CultureInfo": { "z": "ci" }
                }
                """)
        ],
        """
        var language = new LocalizedLanguage(CultureInfo.InvariantCulture);
        return string.Join("|", language.LanguageStrings, language.LanguageTranslations.x, language.LocalizedLanguageGroups.y,
            language.System.@global, language.CultureInfo.z, Language.LanguageStrings, Language.LanguageTranslations.x,
            global::BccCode.I18N.Tests.Generated.LanguageStrings.GetString("LanguageStrings"));
        """);

    Assert.Equal("ls|translations|groups|sys|ci|ls|translations|ls", result);
  }

  [Fact]
  public void Group_struct_types_are_hidden_from_completion()
  {
    var (_, _, compilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [new TestAdditionalFile("./en.json", EnJson)],
        fallbackLanguage: "en");

    var groups = GeneratorTestHelpers.GetGeneratedSource(compilation, "LocalizedLanguageGroups.g.cs");
    Assert.Contains("EditorBrowsableState.Never", groups, StringComparison.Ordinal);
    Assert.DoesNotContain("static readonly", groups, StringComparison.Ordinal);
    foreach (var name in new[] { "LocalizedLanguage.g.cs", "LocalizedLanguageGroups.g.cs", "LanguageTranslations.g.cs" })
    {
      var source = GeneratorTestHelpers.GetGeneratedSource(compilation, name);
      Assert.DoesNotContain("static readonly", source, StringComparison.Ordinal);
      Assert.DoesNotContain("static LanguageTranslations(", source, StringComparison.Ordinal);
    }
  }

  [Fact]
  public void Format_specifiers_use_the_culture_of_the_localized_language()
  {
    var date = new DateTime(2024, 1, 31);
    var enUs = new CultureInfo("en-US");
    var nbNo = new CultureInfo("nb-NO");

    var result = Run(
        [
            new TestAdditionalFile(
                "./en.json",
                """{ "when": "due {date:d}", "price": "{amount:N2} kr", "items": "{count:N0} item | {count:N0} items", "twice": "{n:N0} and {n:N2}" }""")
        ],
        """
        var date = new DateTime(2024, 1, 31);
        var us = new LocalizedLanguage(new CultureInfo("en-US"));
        var nb = new LocalizedLanguage(new CultureInfo("nb-NO"));
        return string.Join("|", us.when(date), nb.when(date), us.price(1234.5m), nb.price(1234.5m), us.items(1234), nb.items(1234), nb.items(0), us.twice(1234.5), nb.twice(1234.5));
        """);

    var expected = string.Join(
        "|",
        "due " + date.ToString("d", enUs),
        "due " + date.ToString("d", nbNo),
        1234.5m.ToString("N2", enUs) + " kr",
        1234.5m.ToString("N2", nbNo) + " kr",
        1234.ToString("N0", enUs) + " items",
        1234.ToString("N0", nbNo) + " items",
        "0 item",
        1234.5.ToString("N0", enUs) + " and " + 1234.5.ToString("N2", enUs),
        1234.5.ToString("N0", nbNo) + " and " + 1234.5.ToString("N2", nbNo));
    Assert.Equal(expected, result);
    Assert.StartsWith("due 1/31/2024|due 31.01.2024|1,234.50 kr|", result, StringComparison.Ordinal);
  }

  [Fact]
  public void Static_language_uses_current_ui_culture_for_language_and_current_culture_for_formatting()
  {
    var result = Run(
        [
            new TestAdditionalFile("./en.json", """{ "price": "{amount:N2} kr" }"""),
            new TestAdditionalFile("./no.json", """{ "price": "{amount:N2} kroner" }""")
        ],
        """
        CultureInfo.CurrentUICulture = new CultureInfo("nb-NO");
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        return Language.price(1234.5m);
        """);

    Assert.Equal("1,234.50 kroner", result);
  }

  [Fact]
  public void Format_specifiers_do_not_affect_placeholder_name_matching()
  {
    var (_, runResult, compilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [
            new TestAdditionalFile("./en.json", """{ "price": "{amount:N2} and {amount:N0}" }"""),
            new TestAdditionalFile("./no.json", """{ "price": "{amount} og {amount:C}" }""")
        ],
        fallbackLanguage: "en");

    Assert.Empty(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult));
    Assert.DoesNotContain(compilation.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
    var source = GeneratorTestHelpers.GetGeneratedSource(compilation, "Language.g.cs");
    Assert.Contains("public static string price(object amount) =>", source, StringComparison.Ordinal);
    var translations = GeneratorTestHelpers.GetGeneratedSource(compilation, "LanguageTranslations.g.cs");
    Assert.Contains("\"{0:N2} and {0:N0}\"", translations, StringComparison.Ordinal);
  }

  [Fact]
  public void Unknown_placeholder_name_with_a_format_is_still_reported_as_a_mismatch()
  {
    var (_, runResult, _) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [
            new TestAdditionalFile("./en.json", """{ "price": "{amount:N2}" }"""),
            new TestAdditionalFile("./no.json", """{ "price": "{sum:N2}" }""")
        ],
        fallbackLanguage: "en");

    Assert.Contains(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult), static d => d.Id == "I18N006");
  }

  [Fact]
  public void LanguageStrings_culture_overloads_select_the_language_and_fall_back_per_key()
  {
    var result = Run(
        [
            new TestAdditionalFile("./en.json", """{ "both": "english {x:N2}", "onlyEn": "only english" }"""),
            new TestAdditionalFile("./no.json", """{ "both": "norsk" }""")
        ],
        """
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");
        var nb = new CultureInfo("nb-NO");
        return string.Join("|", LanguageStrings.GetString("both", nb), LanguageStrings.GetString("onlyEn", nb), LanguageStrings.GetStringOrNull("missing", nb) ?? "null",
            LanguageStrings.GetString("missing", nb), LanguageStrings.GetString("both"), LanguageStrings.GetStringOrNull("both", new CultureInfo("de-DE")));
        """);

    Assert.Equal("norsk|only english|null|missing|english {x:N2}|english {x:N2}", result);
  }

  private static string Run(TestAdditionalFile[] files, string body)
  {
    var (_, runResult, compilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        files,
        fallbackLanguage: "en");
    Assert.DoesNotContain(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult), static d => d.Severity == DiagnosticSeverity.Error);

    var probe = CSharpSyntaxTree.ParseText(
        "using System; using System.Globalization;\n" +
        "namespace BccCode.I18N.Tests.Generated { public static class Probe { public static string Run() {\n" +
        body +
        "\n} } }");
    compilation = compilation.AddSyntaxTrees(probe);

    using var stream = new MemoryStream();
    var emitResult = compilation.Emit(stream);
    Assert.True(
        emitResult.Success,
        string.Join(Environment.NewLine, emitResult.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));

    stream.Position = 0;
    var assembly = new AssemblyLoadContext(null, isCollectible: true).LoadFromStream(stream);
    var method = assembly.GetType("BccCode.I18N.Tests.Generated.Probe", throwOnError: true)!.GetMethod("Run")!;

    var originalCulture = CultureInfo.CurrentCulture;
    var originalUiCulture = CultureInfo.CurrentUICulture;
    try
    {
      return (string)method.Invoke(null, null)!;
    }
    catch (TargetInvocationException exception) when (exception.InnerException is not null)
    {
      throw exception.InnerException;
    }
    finally
    {
      CultureInfo.CurrentCulture = originalCulture;
      CultureInfo.CurrentUICulture = originalUiCulture;
    }
  }
}
