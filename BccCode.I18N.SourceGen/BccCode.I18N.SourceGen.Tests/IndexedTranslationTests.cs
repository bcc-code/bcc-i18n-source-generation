using System;
using System.Globalization;
using System.Linq;
using BccCode.I18N.SourceGen.Tests.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BccCode.I18N.SourceGen.Tests;

/// <summary>
/// The translations are data arrays in LanguageStrings, cached in static fields and cleared by the hot reload
/// MetadataUpdateHandler. The typed members call LanguageStrings.Translate with the index of their key.
/// </summary>
public class IndexedTranslationTests
{
  private const string AttributeLine =
      "[assembly: global::System.Reflection.Metadata.MetadataUpdateHandler(typeof(global::BccCode.I18N.Tests.Generated.LanguageStrings))]";

  private static TestAdditionalFile[] Files() =>
  [
      new TestAdditionalFile("./en.json", """{ "a": "A", "b": "B {x}" }"""),
      new TestAdditionalFile("./no.json", """{ "a": "Aa" }""")
  ];

  [Fact]
  public void Metadata_update_handler_attribute_is_emitted_when_the_runtime_has_the_type()
  {
    var (_, _, compilation) = GeneratorTestHelpers.RunGenerator(new I18NSourceGenerator(), Files(), fallbackLanguage: "en");
    var source = GeneratorTestHelpers.GetGeneratedSource(compilation, "LanguageStrings.g.cs");

    Assert.Contains(AttributeLine, source, StringComparison.Ordinal);
    Assert.True(source.IndexOf(AttributeLine, StringComparison.Ordinal) < source.IndexOf("namespace ", StringComparison.Ordinal));
    Assert.Contains("internal static void ClearCache(global::System.Type[]? updatedTypes)", source, StringComparison.Ordinal);
    Assert.DoesNotContain(compilation.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void Metadata_update_handler_attribute_is_not_emitted_without_the_type_but_ClearCache_is()
  {
    var (_, _, compilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        Files(),
        fallbackLanguage: "en",
        configureCompilation: static compilation => compilation.WithReferences([]));
    var source = GeneratorTestHelpers.GetGeneratedSource(compilation, "LanguageStrings.g.cs");

    Assert.DoesNotContain("[assembly:", source, StringComparison.Ordinal);
    Assert.Contains("internal static void ClearCache(global::System.Type[]? updatedTypes)", source, StringComparison.Ordinal);
  }

  [Fact]
  public void Metadata_update_handler_attribute_names_the_type_in_the_global_namespace()
  {
    var (_, _, compilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        Files(),
        fallbackLanguage: "en",
        generatedNamespace: " ");
    var source = GeneratorTestHelpers.GetGeneratedSource(compilation, "LanguageStrings.g.cs");

    Assert.Contains("MetadataUpdateHandler(typeof(global::LanguageStrings))]", source, StringComparison.Ordinal);
    Assert.DoesNotContain(compilation.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void Metadata_update_handler_detection_is_not_run_again_when_translations_change()
  {
    var noFile = new TestAdditionalFile("./no.json", """{ "a": "Aa" }""");
    var (driver, _, _) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [new TestAdditionalFile("./en.json", """{ "a": "A" }"""), noFile],
        fallbackLanguage: "en",
        trackIncrementalSteps: true);

    driver = driver
        .ReplaceAdditionalText(noFile, new TestAdditionalFile("./no.json", """{ "a": "Changed" }"""))
        .RunGenerators(GeneratorTestHelpers.CreateCompilation());

    var result = Assert.Single(driver.GetRunResult().Results);
    Assert.All(
        result.TrackedSteps["MetadataUpdateHandlerSupport"].SelectMany(static step => step.Outputs),
        static output => Assert.True(output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));
  }

  [Fact]
  public void Assembly_declares_LanguageStrings_as_the_metadata_update_handler()
  {
    var result = LocalizedLanguageTests.Run(
        Files(),
        """
        var attribute = (System.Reflection.Metadata.MetadataUpdateHandlerAttribute)typeof(LanguageStrings).Assembly
            .GetCustomAttributes(typeof(System.Reflection.Metadata.MetadataUpdateHandlerAttribute), false).Single();
        return attribute.HandlerType.FullName!;
        """);

    Assert.Equal("BccCode.I18N.Tests.Generated.LanguageStrings", result);
  }

  [Fact]
  public void ClearCache_resets_the_caches_and_lookups_still_work()
  {
    var result = LocalizedLanguageTests.Run(
        Files(),
        """
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var type = typeof(LanguageStrings);
        var nb = new CultureInfo("nb-NO");
        var before = string.Join("/", LanguageStrings.GetString("a", nb), LanguageStrings.GetString("b", nb), Language.b("x"), LanguageStrings.Keys.Count, LanguageStrings.GetAllStrings("no").Count);

        string[] cacheFields = { "s_keys", "s_parameterNames", "s_keyIndex", "s_strings0", "s_strings1" };
        var filled = cacheFields.Count(name => type.GetField(name, flags)!.GetValue(null) != null);

        type.GetMethod("ClearCache", flags)!.Invoke(null, new object[] { null });
        var cleared = cacheFields.Count(name => type.GetField(name, flags)!.GetValue(null) == null);

        var after = string.Join("/", LanguageStrings.GetString("a", nb), LanguageStrings.GetString("b", nb), Language.b("x"), LanguageStrings.Keys.Count, LanguageStrings.GetAllStrings("no").Count);
        return string.Join("|", before, filled, cleared, after);
        """);

    Assert.Equal("Aa/B {x}/B x/2/1|5|5|Aa/B {x}/B x/2/1", result);
  }

  // Typed members compile the call at generation time and LanguageStrings.Format formats at run time. Both use the
  // same helpers, so for the same key, culture and arguments they must give the same text.
  [Fact]
  public void Typed_members_and_Format_agree_for_literals_plurals_format_specifiers_and_mismatches()
  {
    var files = new[]
    {
        new TestAdditionalFile(
            "./en.json",
            """
            {
              "lit": "user{'@'}host {'{'}braces{'}'} {'it\\'s'}",
              "price": "{amount:N2} kr",
              "when": "due {date:d}",
              "cars": "{count} car | {count} cars",
              "items": "item",
              "mismatch": "hello {name}",
              "mixed": "{count:N0} thing | {count:N0} things",
              "plain": "plain text"
            }
            """),
        new TestAdditionalFile(
            "./no.json",
            """
            {
              "lit": "bruker{'@'}vert",
              "price": "{amount:N2} kroner",
              "when": "frist {date:d}",
              "cars": "{count} bil | {count} biler | {count} mange",
              "items": "ingen | én | {count} stk",
              "mismatch": "hei {other}",
              "mixed": "{count:N0} ting",
              "plain": "ren tekst"
            }
            """)
    };

    var result = LocalizedLanguageTests.Run(
        files,
        """
        var problems = new System.Collections.Generic.List<string>();
        var date = new DateTime(2024, 1, 31);
        var counts = new[] { 0, 1, 2, 3, 5, -1, -2, 1000000, int.MaxValue, int.MinValue };
        foreach (var name in new[] { "nb-NO", "en-US", "de-DE" })
        {
            var culture = new CultureInfo(name);
            var language = new LocalizedLanguage(culture);

            void Check(string key, string typed, params object[] args)
            {
                var formatted = LanguageStrings.Format(key, culture, args);
                if (typed != formatted) problems.Add(name + ":" + key + ":" + typed + " != " + formatted);
            }

            Check("lit", language.lit);
            Check("plain", language.plain);
            Check("price", language.price(1234.5m), 1234.5m);
            Check("when", language.when(date), date);
            Check("mismatch", language.mismatch("Kari"), "Kari");
            foreach (var count in counts)
            {
                Check("cars", language.cars(count), count);
                Check("items", language.items(count), count);
                Check("mixed", language.mixed(count), count);
            }
        }

        var no = new LocalizedLanguage(new CultureInfo("nb-NO"));
        var en = new LocalizedLanguage(new CultureInfo("en-US"));
        return string.Join("|", string.Join(";", problems), no.lit, en.lit, no.mismatch("Kari"), en.mismatch("Kari"),
            no.cars(0), no.cars(1), no.cars(2), no.cars(int.MinValue), en.cars(1), en.cars(2), en.cars(int.MinValue),
            no.items(0), no.items(1), no.items(7), en.items(7), no.mixed(1), default(LocalizedLanguage).cars(1));
        """);

    var nbNo = new CultureInfo("nb-NO");
    var enUs = new CultureInfo("en-US");
    var expected = string.Join(
        "|",
        string.Empty,
        "bruker@vert",
        "user@host {braces} it's",
        "hello Kari",
        "hello Kari",
        "0 bil",
        "1 biler",
        "2 mange",
        int.MinValue.ToString(nbNo) + " mange",
        "1 car",
        "2 cars",
        int.MinValue.ToString(enUs) + " cars",
        "ingen",
        "én",
        "7 stk",
        "item",
        "1 ting",
        "1 car");
    Assert.Equal(expected, result);
  }

  [Fact]
  public void Typed_member_with_a_literal_and_no_arguments_returns_the_resolved_literal()
  {
    var result = LocalizedLanguageTests.Run(
        [new TestAdditionalFile("./en.json", """{ "mail": "a{'@'}b.com", "none": "no braces" }""")],
        """return string.Join("|", Language.mail, new LocalizedLanguage(CultureInfo.InvariantCulture).mail, Language.none, object.ReferenceEquals(Language.none, Language.none));""");

    Assert.Equal("a@b.com|a@b.com|no braces|True", result);
  }

  [Fact]
  public void Placeholder_mismatch_is_reported_once_per_key_and_language()
  {
    var (_, runResult, _) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [
            new TestAdditionalFile("./en.json", """{ "hello": "hi {name}", "cars": "car | cars" }"""),
            new TestAdditionalFile("./no.json", """{ "hello": "hei {other} {more}", "cars": "bil | {other}" }""")
        ],
        fallbackLanguage: "en");

    var diagnostics = GeneratorTestHelpers.GetGeneratorDiagnostics(runResult).Where(static d => d.Id == "I18N006").ToArray();
    Assert.Equal(2, diagnostics.Length);
    Assert.Contains(diagnostics, static d => d.GetMessage(CultureInfo.InvariantCulture).Contains("'hello' in language 'no' uses placeholders [other, more]", StringComparison.Ordinal));
    Assert.Contains(diagnostics, static d => d.GetMessage(CultureInfo.InvariantCulture).Contains("'cars' in language 'no' uses placeholders [other]", StringComparison.Ordinal));
  }

  [Fact]
  public void Comment_with_the_key_cannot_be_ended_by_the_key()
  {
    var (_, _, compilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [new TestAdditionalFile("./en.json", """{ "a*/b": "x", "line\nbreak": "y" }""")],
        fallbackLanguage: "en");

    Assert.DoesNotContain(compilation.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
    Assert.Contains("/* a* /b */", GeneratorTestHelpers.GetGeneratedSource(compilation, "Language.g.cs"), StringComparison.Ordinal);
  }
}
