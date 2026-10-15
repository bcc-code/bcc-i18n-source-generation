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

/// <summary>Listings, LanguageStrings.Format and the IStringLocalizer support.</summary>
public class LanguageStringsApiTests
{
  private const string EnJson = """
                                {
                                  "plain": "hello",
                                  "greet": "Hi {name}",
                                  "when": "Due {date:d}",
                                  "price": "{amount:N2} kr",
                                  "lit": "{'@'} and {'{'}x{'}'}",
                                  "two": "car | cars",
                                  "three": "none | one | many",
                                  "mixed": "x",
                                  "list": "{0} and {1}",
                                  "rev": "{1} then {0}",
                                  "p": { "item": "{count:N0} item | {count:N0} items" },
                                  "onlyEn": "only english"
                                }
                                """;

  private const string NoJson = """
                                {
                                  "plain": "hallo",
                                  "greet": "Hei {name}",
                                  "two": "bil | biler",
                                  "mixed": "ingen | en | mange"
                                }
                                """;

  private static TestAdditionalFile[] Files() =>
      [new TestAdditionalFile("./en.json", EnJson), new TestAdditionalFile("./no.json", NoJson)];

  private static string Run(string body) => LocalizedLanguageTests.Run(Files(), body);

  [Fact]
  public void Listings_expose_the_fallback_language_languages_and_keys()
  {
    var result = Run(
        """
        return string.Join("|", LanguageStrings.FallbackLanguage, string.Join(",", LanguageStrings.Languages),
            string.Join(",", LanguageStrings.Keys));
        """);

    Assert.Equal("en|en,no|greet,list,lit,mixed,onlyEn,p.item,plain,price,rev,three,two,when", result);
  }

  [Fact]
  public void GetAllStrings_returns_stored_values_for_a_language_and_nothing_for_unknown_ones()
  {
    var result = Run(
        """
        string Dump(System.Collections.Generic.IReadOnlyList<System.Collections.Generic.KeyValuePair<string, string>> list) =>
            string.Join(",", list.Select(p => p.Key + "=" + p.Value));
        return string.Join("|", Dump(LanguageStrings.GetAllStrings("NO")), LanguageStrings.GetAllStrings("de").Count,
            LanguageStrings.GetAllStrings("en").Count, LanguageStrings.GetAllStrings((string)null!).Count);
        """);

    Assert.Equal("greet=Hei {name},mixed=ingen | en | mange,plain=hallo,two=bil | biler|0|12|0", result);
  }

  [Fact]
  public void GetAllStrings_for_a_culture_can_include_the_fallback_language()
  {
    var result = Run(
        """
        var nb = new CultureInfo("nb-NO");
        var own = LanguageStrings.GetAllStrings(nb, includeFallback: false);
        var all = LanguageStrings.GetAllStrings(nb);
        var de = LanguageStrings.GetAllStrings(new CultureInfo("de-DE"), includeFallback: false);
        return string.Join("|", own.Count, all.Count, all.First(p => p.Key == "plain").Value, all.First(p => p.Key == "onlyEn").Value,
            all.Count(p => p.Key == "greet"), de.Count);
        """);

    Assert.Equal("4|12|hallo|only english|1|12", result);
  }

  [Fact]
  public void Format_formats_plain_named_and_list_messages_by_culture()
  {
    var result = Run(
        """
        var nb = new CultureInfo("nb-NO");
        var en = new CultureInfo("en-US");
        return string.Join("|", LanguageStrings.Format("plain", nb), LanguageStrings.Format("plain", en), LanguageStrings.Format("greet", nb, "Kari"),
            LanguageStrings.Format("greet", en, "Kari"), LanguageStrings.Format("list", en, "a", "b"), LanguageStrings.Format("rev", en, "first", "second"),
            LanguageStrings.Format("onlyEn", nb));
        """);

    Assert.Equal("hallo|hello|Hei Kari|Hi Kari|a and b|first then second|only english", result);
  }

  [Fact]
  public void Format_uses_the_culture_for_format_specifiers()
  {
    var date = new DateTime(2024, 1, 31);
    var enUs = new CultureInfo("en-US");
    var nbNo = new CultureInfo("nb-NO");

    var result = Run(
        """
        var date = new DateTime(2024, 1, 31);
        var nb = new CultureInfo("nb-NO");
        var en = new CultureInfo("en-US");
        CultureInfo.CurrentUICulture = en;
        CultureInfo.CurrentCulture = nb;
        return string.Join("|", LanguageStrings.Format("when", en, date), LanguageStrings.Format("when", nb, date),
            LanguageStrings.Format("price", en, 1234.5m), LanguageStrings.Format("price", nb, 1234.5m),
            LanguageStrings.Format("price", 1234.5m), LanguageStrings.Format("p.item", en, 1234), LanguageStrings.Format("p.item", en, 1));
        """);

    Assert.Equal(
        string.Join(
            "|",
            "Due " + date.ToString("d", enUs),
            "Due " + date.ToString("d", nbNo),
            1234.5m.ToString("N2", enUs) + " kr",
            1234.5m.ToString("N2", nbNo) + " kr",
            1234.5m.ToString("N2", nbNo) + " kr",
            1234.ToString("N0", enUs) + " items",
            "1 item"),
        result);
  }

  [Fact]
  public void Format_turns_literals_into_text_and_keeps_placeholders_without_arguments_as_written()
  {
    var result = Run(
        """
        return string.Join("|", LanguageStrings.Format("lit", CultureInfo.InvariantCulture), LanguageStrings.Format("greet", CultureInfo.InvariantCulture),
            LanguageStrings.Format("list", CultureInfo.InvariantCulture, "only"), LanguageStrings.Format("unknown.key", CultureInfo.InvariantCulture, 1),
            LanguageStrings.Format("price", CultureInfo.InvariantCulture, new object[] { null }));
        """);

    Assert.Equal("@ and {x}|Hi {name}|only and {1}|unknown.key| kr", result);
  }

  [Fact]
  public void Format_decodes_literal_escapes_like_the_language_members()
  {
    var files = new[]
    {
        new TestAdditionalFile(
            "./en.json",
            """
            {
              "quote": "{'It\\'s'}",
              "slash": "{'a\\\\b'}",
              "unicode": "{'\\u0041\\U01F600'}",
              "pipe": "{'it\\'s | x'} | other",
              "badEscape": "{'bad\\x'}",
              "badHex": "{'\\u00zz'}",
              "surrogate": "{'\\uD800'}",
              "unclosed": "{'open",
              "withParam": "{'{'}{name}{'}'}"
            }
            """)
    };

    var result = LocalizedLanguageTests.Run(
        files,
        """
        var c = CultureInfo.InvariantCulture;
        string F(string key, params object[] args) => LanguageStrings.Format(key, c, args);
        return string.Join("|", F("quote"), F("slash"), F("unicode"), F("pipe", 1), F("pipe", 2), F("badEscape"), F("badHex"),
            F("surrogate"), F("unclosed"), F("withParam", "x"),
            Language.quote, Language.slash, Language.unicode, Language.pipe(1), Language.pipe(2), Language.badEscape, Language.badHex,
            Language.surrogate, Language.unclosed);
        """);

    var expected = new[]
    {
        "It's", "a\\b", "A" + char.ConvertFromUtf32(0x1F600), "it's | x", "other", "{'bad\\x'}", "{'\\u00zz'}", "{'\\uD800'}", "{'open",
        "{x}",
    };
    var languageMembers = new[]
    {
        "It's", "a\\b", "A" + char.ConvertFromUtf32(0x1F600), "it's | x", "other", "{'bad\\x'}", "{'\\u00zz'}", "{'\\uD800'}", "{'open",
    };
    Assert.Equal(string.Join("|", expected.Concat(languageMembers)), result);
  }

  [Fact]
  public void Format_does_not_overflow_for_the_smallest_counts_in_a_checked_context()
  {
    var result = LocalizedLanguageTests.Run(
        Files(),
        """
        var en = new CultureInfo("en-US");
        return string.Join("|", LanguageStrings.Format("two", en, int.MinValue), LanguageStrings.Format("two", en, long.MinValue),
            LanguageStrings.Format("three", en, int.MinValue), LanguageStrings.Format("three", en, long.MinValue), LanguageStrings.Format("two", en, -1L));
        """,
        checkedOverflow: true);

    Assert.Equal("cars|cars|many|many|car", result);
  }

  [Fact]
  public void Format_selects_plural_forms_with_the_vue_rules_per_number_of_forms()
  {
    var result = Run(
        """
        var en = new CultureInfo("en-US");
        var nb = new CultureInfo("nb-NO");
        string F(string key, CultureInfo c, object count) => LanguageStrings.Format(key, c, count);
        return string.Join("|",
            string.Join(",", new[] { 0, 1, 2, 5, -1, -2 }.Select(n => F("two", en, n))),
            string.Join(",", new[] { 0, 1, 2, 5, -1, -2 }.Select(n => F("three", en, n))),
            string.Join(",", new[] { 0, 1, 2, 5 }.Select(n => F("two", nb, n))),
            string.Join(",", new[] { 0, 1, 2, 5, -1 }.Select(n => F("mixed", nb, n))),
            string.Join(",", new[] { 0, 5 }.Select(n => F("mixed", en, n))),
            F("two", en, 1L), F("two", en, 1m), F("two", en, (short)2), F("two", en, "abc"), LanguageStrings.Format("two", en));
        """);

    Assert.Equal(
        "cars,car,cars,cars,car,cars|none,one,many,many,one,many|biler,bil,biler,biler|ingen,en,mange,mange,en|x,x|car|car|cars|car|car",
        result);
  }

  [Fact]
  public void Format_arguments_follow_the_parameter_order_of_the_language_members()
  {
    var (_, runResult, compilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        Files(),
        fallbackLanguage: "en");
    Assert.DoesNotContain(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult), static d => d.Severity == DiagnosticSeverity.Error);
    var assembly = Load(compilation);
    var language = assembly.GetType("BccCode.I18N.Tests.Generated.Language", throwOnError: true)!;
    var strings = assembly.GetType("BccCode.I18N.Tests.Generated.LanguageStrings", throwOnError: true)!;
    var format = strings.GetMethod("Format", [typeof(string), typeof(CultureInfo), typeof(object[])])!;

    var originalCulture = CultureInfo.CurrentCulture;
    var originalUiCulture = CultureInfo.CurrentUICulture;
    try
    {
      var checkedKeys = 0;
      foreach (var culture in new[] { "en-US", "nb-NO" })
      {
        var cultureInfo = new CultureInfo(culture);
        CultureInfo.CurrentCulture = cultureInfo;
        CultureInfo.CurrentUICulture = cultureInfo;

        foreach (var (key, member) in EnumerateLeaves(language, string.Empty))
        {
          if (key == "when")
          {
            continue;
          }

          object?[] args;
          string expected;
          if (member is PropertyInfo property)
          {
            args = [];
            expected = (string)property.GetValue(null)!;
          }
          else
          {
            var method = (MethodInfo)member;
            args = method.GetParameters().Select(static (p, i) => p.ParameterType == typeof(int) ? (object)(i + 2) : "v" + i).ToArray();
            expected = (string)method.Invoke(null, args)!;
          }

          Assert.Equal(expected, (string)format.Invoke(null, [key, cultureInfo, args])!);
          checkedKeys++;
        }
      }

      Assert.True(checkedKeys >= 20, "Expected to compare all keys but only compared " + checkedKeys);
    }
    finally
    {
      CultureInfo.CurrentCulture = originalCulture;
      CultureInfo.CurrentUICulture = originalUiCulture;
    }
  }

  private static System.Collections.Generic.IEnumerable<(string Key, MemberInfo Member)> EnumerateLeaves(Type type, string prefix)
  {
    foreach (var nested in type.GetNestedTypes(BindingFlags.Public))
    {
      foreach (var leaf in EnumerateLeaves(nested, prefix + nested.Name + "."))
      {
        yield return leaf;
      }
    }

    foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Static))
    {
      yield return (prefix + property.Name, property);
    }

    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(static m => !m.IsSpecialName))
    {
      yield return (prefix + method.Name, method);
    }
  }

  private const string FakeWithCulture = """
                                         namespace Microsoft.Extensions.Localization
                                         {
                                           public class LocalizedString
                                           {
                                             public LocalizedString(string name, string value, bool resourceNotFound) { Name = name; Value = value; ResourceNotFound = resourceNotFound; }
                                             public string Name { get; }
                                             public string Value { get; }
                                             public bool ResourceNotFound { get; }
                                           }
                                           public interface IStringLocalizer
                                           {
                                             LocalizedString this[string name] { get; }
                                             LocalizedString this[string name, params object[] arguments] { get; }
                                             System.Collections.Generic.IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures);
                                             IStringLocalizer WithCulture(System.Globalization.CultureInfo culture);
                                           }
                                           public interface IStringLocalizerFactory
                                           {
                                             IStringLocalizer Create(System.Type resourceSource);
                                             IStringLocalizer Create(string baseName, string location);
                                           }
                                         }
                                         """;

  [Fact]
  public void No_localizer_is_generated_without_a_reference_to_the_abstractions()
  {
    var (_, runResult, compilation) = GeneratorTestHelpers.RunGenerator(new I18NSourceGenerator(), Files(), fallbackLanguage: "en");

    Assert.DoesNotContain(compilation.SyntaxTrees, static t => Path.GetFileName(t.FilePath).StartsWith("LanguageStringLocalizer", StringComparison.Ordinal));
    Assert.DoesNotContain(compilation.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
    // The languages intentionally differ in keys, which is reported as I18N007.
    Assert.Empty(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult).Where(static d => d.Id != "I18N007"));
  }

  [Fact]
  public void Localizer_is_generated_and_works_with_the_abstractions_package()
  {
    var result = LocalizedLanguageTests.Run(
        Files(),
        """
        var nb = new LanguageStringLocalizer(new CultureInfo("nb-NO"));
        var factory = new LanguageStringLocalizerFactory();
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");
        var current = new LanguageStringLocalizer();
        var missing = nb["nope"];
        var all = nb.GetAllStrings(true).ToArray();
        var ownOnly = nb.GetAllStrings(false).ToArray();
        return string.Join("|", nb["greet"].Value, nb["greet"].ResourceNotFound, nb["greet", "Kari"].Value, missing.Value, missing.ResourceNotFound,
            nb["nope", "x"].ResourceNotFound, current["plain"].Value, current["two", 1].Value, all.Length, ownOnly.Length,
            factory.Create(typeof(string))["plain"].Value, factory.Create("a", "b")["plain"].Value);
        """,
        referenceLocalization: true);

    Assert.Equal("Hei {name}|False|Hei Kari|nope|True|True|hello|car|12|4|hello|hello", result);
  }

  [Fact]
  public void Localizer_has_no_WithCulture_when_the_interface_lacks_it()
  {
    var (_, runResult, compilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        Files(),
        fallbackLanguage: "en",
        referenceLocalization: true);

    // The languages intentionally differ in keys, which is reported as I18N007.
    Assert.Empty(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult).Where(static d => d.Id != "I18N007"));
    Assert.DoesNotContain(compilation.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
    Assert.DoesNotContain("WithCulture", GeneratorTestHelpers.GetGeneratedSource(compilation, "LanguageStringLocalizer.g.cs"), StringComparison.Ordinal);
  }

  [Fact]
  public void Localizer_implements_WithCulture_when_the_interface_declares_it()
  {
    var result = LocalizedLanguageTests.Run(
        Files(),
        """
        var localizer = new LanguageStringLocalizer().WithCulture(new CultureInfo("nb-NO"));
        return localizer["greet", "Kari"].Value + "|" + localizer["plain"].Value;
        """,
        extraSources: [FakeWithCulture]);

    Assert.Equal("Hei Kari|hallo", result);
  }

  [Fact]
  public void Localizer_is_not_generated_when_the_abstractions_are_incomplete()
  {
    var (_, _, compilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        Files(),
        fallbackLanguage: "en",
        extraSources: ["namespace Microsoft.Extensions.Localization { public interface IStringLocalizer { } }"]);

    Assert.DoesNotContain(compilation.SyntaxTrees, static t => Path.GetFileName(t.FilePath).StartsWith("LanguageStringLocalizer", StringComparison.Ordinal));
  }

  [Fact]
  public void Localizer_output_is_not_regenerated_when_only_translations_change()
  {
    var (_, secondRun) = GeneratorTestHelpers.RunGeneratorTwice(new I18NSourceGenerator(), Files(), fallbackLanguage: "en");

    var secondResult = Assert.Single(secondRun.Results);
    Assert.All(
        secondResult.TrackedSteps["StringLocalizerSupport"].SelectMany(static step => step.Outputs),
        static output => Assert.True(output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));
  }

  [Fact]
  public void Listings_and_format_add_no_static_state()
  {
    var (_, _, compilation) = GeneratorTestHelpers.RunGenerator(new I18NSourceGenerator(), Files(), fallbackLanguage: "en");
    var source = GeneratorTestHelpers.GetGeneratedSource(compilation, "LanguageStrings.g.cs");

    Assert.DoesNotContain("static readonly", source, StringComparison.Ordinal);
    Assert.DoesNotContain("yield ", source, StringComparison.Ordinal);
    Assert.DoesNotContain("static LanguageStrings(", source, StringComparison.Ordinal);
  }

  private static Assembly Load(Compilation compilation)
  {
    using var stream = new MemoryStream();
    var emitResult = compilation.Emit(stream);
    Assert.True(
        emitResult.Success,
        string.Join(Environment.NewLine, emitResult.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));
    stream.Position = 0;
    return new AssemblyLoadContext(null, isCollectible: true).LoadFromStream(stream);
  }
}
