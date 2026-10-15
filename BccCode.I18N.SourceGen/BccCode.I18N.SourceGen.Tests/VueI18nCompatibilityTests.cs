using System;
using System.Linq;
using BccCode.I18N.SourceGen.Tests.Utils;
using Microsoft.CodeAnalysis;
using Xunit;

namespace BccCode.I18N.SourceGen.Tests;

/// <summary>vue-i18n compatible plural rules, linked messages, literals and list placeholders.</summary>
public class VueI18nCompatibilityTests
{
  private const string PluralEn = """
                                  {
                                    "p": {
                                      "two": "car | cars",
                                      "three": "none | one | many",
                                      "four": "zero | one | two | many"
                                    }
                                  }
                                  """;

  private const string PluralNo = """
                                  {
                                    "p": {
                                      "two": "a | b | c",
                                      "three": "x | y"
                                    }
                                  }
                                  """;

  private static string Run(TestAdditionalFile[] files, string body) => LocalizedLanguageTests.Run(files, body);

  [Fact]
  public void Two_forms_use_the_first_form_only_for_a_count_of_one()
  {
    var result = Run(
        [new TestAdditionalFile("./en.json", PluralEn)],
        """return string.Join("|", new[] { 0, 1, 2, 5, -1, -2 }.Select(n => Language.p.two(n)));""");

    Assert.Equal("cars|car|cars|cars|car|cars", result);
  }

  [Fact]
  public void Three_forms_use_zero_one_and_many()
  {
    var result = Run(
        [new TestAdditionalFile("./en.json", PluralEn)],
        """return string.Join("|", new[] { 0, 1, 2, 5, -1, -2 }.Select(n => Language.p.three(n)));""");

    Assert.Equal("none|one|many|many|one|many", result);
  }

  [Fact]
  public void Four_forms_keep_the_count_as_the_index_until_the_last_form()
  {
    var result = Run(
        [new TestAdditionalFile("./en.json", PluralEn)],
        """return string.Join("|", new[] { 0, 1, 2, 3, 5, -1, -2 }.Select(n => Language.p.four(n)));""");

    Assert.Equal("zero|one|two|many|many|one|two", result);
  }

  [Fact]
  public void Each_language_uses_the_rule_for_its_own_number_of_forms()
  {
    var result = Run(
        [new TestAdditionalFile("./en.json", PluralEn), new TestAdditionalFile("./no.json", PluralNo)],
        """
        CultureInfo.CurrentUICulture = new CultureInfo("nb-NO");
        var no = new LocalizedLanguage(new CultureInfo("nb-NO"));
        var en = new LocalizedLanguage(new CultureInfo("en-US"));
        return string.Join("|", no.p.two(0), no.p.two(1), no.p.two(2), no.p.three(0), no.p.three(1), no.p.three(2), no.p.three(-1),
            en.p.two(1), en.p.three(1), Language.p.two(1));
        """);

    Assert.Equal("a|b|c|y|x|y|x|car|one|b", result);
  }

  private static readonly string LinkEn = """
                                          {
                                            "common": { "save": "Save", "cancel": "cancel", "who": "{name}", "greet": "hello {name}" },
                                            "only": "fallback text",
                                            "btn": {
                                              "basic": "Press @:common.save.",
                                              "braced": "@:{'common.save'}!",
                                              "lower": "@.lower:common.save",
                                              "upper": "@.upper:{'common.save'}",
                                              "cap": "@.capitalize:common.cancel",
                                              "chain": "x @:btn.chain2",
                                              "chain2": "y @:common.save",
                                              "usesOnly": "@:only",
                                              "param": "Hi @:common.who!",
                                              "upperParam": "@.upper:common.greet",
                                              "mail": "post@bcc.no",
                                              "item": "@:common.save item | @:common.save items",
                                              "twice": "@:common.save and @.upper:common.save"
                                            }
                                          }
                                          """;

  private static readonly string LinkNo = """
                                          {
                                            "common": { "save": "Lagre" },
                                            "btn": {
                                              "basic": "Trykk @:common.save.",
                                              "usesOnly": "@:only og @:common.cancel"
                                            }
                                          }
                                          """;

  [Fact]
  public void Linked_messages_are_resolved_in_the_same_language_or_the_fallback()
  {
    var result = Run(
        [new TestAdditionalFile("./en.json", LinkEn), new TestAdditionalFile("./no.json", LinkNo)],
        """
        var no = new LocalizedLanguage(new CultureInfo("nb-NO"));
        var en = new LocalizedLanguage(new CultureInfo("en-US"));
        return string.Join("|", en.btn.basic, en.btn.braced, en.btn.lower, en.btn.upper, en.btn.cap, en.btn.chain, en.btn.usesOnly,
            no.btn.basic, no.btn.usesOnly, no.btn.braced, no.btn.twice, en.btn.twice);
        """);

    Assert.Equal(
        "Press Save.|Save!|save|SAVE|Cancel|x y Save|fallback text|Trykk Lagre.|fallback text og cancel|Save!|Save and SAVE|Save and SAVE",
        result);
  }

  [Fact]
  public void Link_targets_with_placeholders_become_parameters_and_modifiers_skip_them()
  {
    var result = Run(
        [new TestAdditionalFile("./en.json", LinkEn)],
        """return string.Join("|", Language.btn.param("Kari"), Language.btn.upperParam("kari"), Language.btn.item(1), Language.btn.item(3));""");

    Assert.Equal("Hi Kari!|HELLO kari|Save item|Save items", result);
  }

  [Fact]
  public void An_at_sign_without_a_link_marker_is_plain_text()
  {
    var result = Run(
        [new TestAdditionalFile("./en.json", LinkEn)],
        """return Language.btn.mail + "|" + LanguageStrings.GetString("btn.mail");""");

    Assert.Equal("post@bcc.no|post@bcc.no", result);
  }

  [Fact]
  public void LanguageStrings_returns_values_with_links_resolved()
  {
    var result = Run(
        [new TestAdditionalFile("./en.json", LinkEn)],
        """return LanguageStrings.GetString("btn.basic") + "|" + LanguageStrings.GetString("btn.item") + "|" + LanguageStrings.GetString("btn.param");""");

    Assert.Equal("Press Save.|Save item | Save items|Hi {name}!", result);
  }

  [Fact]
  public void Reports_I18N010_for_unknown_cyclic_and_plural_targets_and_keeps_the_link_text()
  {
    var (_, runResult, compilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [
            new TestAdditionalFile(
                "./en.json",
                """
                {
                  "a": "x @:b",
                  "b": "y @:a",
                  "u": "@:missing",
                  "pl": "@:target",
                  "target": "one | two",
                  "ok": "fine @:b2",
                  "b2": "z"
                }
                """)
        ],
        fallbackLanguage: "en");

    var diagnostics = GeneratorTestHelpers.GetGeneratorDiagnostics(runResult).Where(static d => d.Id == "I18N010").ToArray();
    Assert.Equal(4, diagnostics.Length);
    Assert.All(diagnostics, static d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
    Assert.All(diagnostics, static d => Assert.Equal("./en.json", d.Location.GetLineSpan().Path));
    Assert.Single(diagnostics, static d => d.GetMessage().Contains("'a'", StringComparison.Ordinal) && d.GetMessage().Contains("cycle", StringComparison.Ordinal));
    Assert.Single(diagnostics, static d => d.GetMessage().Contains("'b'", StringComparison.Ordinal) && d.GetMessage().Contains("cycle", StringComparison.Ordinal));
    Assert.Single(diagnostics, static d => d.GetMessage().Contains("'u'", StringComparison.Ordinal) && d.GetMessage().Contains("does not match", StringComparison.Ordinal));
    Assert.Single(diagnostics, static d => d.GetMessage().Contains("'pl'", StringComparison.Ordinal) && d.GetMessage().Contains("plural", StringComparison.Ordinal));
    Assert.DoesNotContain(compilation.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);

    var result = Run(
        [new TestAdditionalFile("./en.json", """{ "u": "@:missing", "a": "x @:b", "b": "y @:a", "ok": "fine @:b2", "b2": "z" }""")],
        """return Language.u + "|" + Language.a + "|" + Language.ok;""");
    Assert.Equal("@:missing|x y @:a|fine z", result);
  }

  [Fact]
  public void Reports_I18N010_once_per_language_and_key()
  {
    var (_, runResult, _) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [
            new TestAdditionalFile("./en.json", """{ "u": "@:one and @:two", "t": "x" }"""),
            new TestAdditionalFile("./no.json", """{ "u": "@:three" }""")
        ],
        fallbackLanguage: "en");

    var diagnostics = GeneratorTestHelpers.GetGeneratorDiagnostics(runResult).Where(static d => d.Id == "I18N010").ToArray();
    Assert.Equal(2, diagnostics.Length);
    Assert.Equal(new[] { "./en.json", "./no.json" }, diagnostics.Select(static d => d.Location.GetLineSpan().Path).OrderBy(static p => p, StringComparer.Ordinal).ToArray());
  }

  [Fact]
  public void Literals_produce_text_and_do_not_start_links_or_split_plural_forms()
  {
    var result = Run(
        [
            new TestAdditionalFile(
                "./en.json",
                """
                {
                  "lit": "{'@'}:x",
                  "braces": "{'{'}x{'}'}",
                  "litParam": "{'{'}{name}{'}'}",
                  "pipe": "one {'|'} two | other",
                  "t": { "x": "target" }
                }
                """)
        ],
        """
        return string.Join("|", Language.lit, Language.braces, Language.litParam("Kari"), Language.pipe(1), Language.pipe(2),
            LanguageStrings.GetString("lit"), LanguageStrings.GetString("pipe"));
        """);

    Assert.Equal("@:x|{x}|{Kari}|one | two|other|{'@'}:x|one {'|'} two | other", result);
  }

  [Fact]
  public void List_placeholders_become_parameters_in_order_of_first_appearance()
  {
    var result = Run(
        [
            new TestAdditionalFile(
                "./en.json",
                """
                {
                  "l": {
                    "list": "{0} and {1}",
                    "rev": "{1} then {0}",
                    "item": "{0} item | {0} items",
                    "of": "{0} of {1} | {0} of {1} items"
                  }
                }
                """)
        ],
        """
        return string.Join("|", Language.l.list("a", "b"), Language.l.rev("first", "second"), Language.l.item(1), Language.l.item(2),
            Language.l.of(1, "x"), Language.l.of(2, "x"));
        """);

    Assert.Equal("a and b|first then second|1 item|2 items|1 of x|2 of x items", result);
  }

  [Fact]
  public void Link_resolution_keeps_the_model_cached_for_unchanged_input()
  {
    var (_, secondRun) = GeneratorTestHelpers.RunGeneratorTwice(
        new I18NSourceGenerator(),
        [new TestAdditionalFile("./en.json", LinkEn)],
        fallbackLanguage: "en");

    var secondResult = Assert.Single(secondRun.Results);
    Assert.All(
        secondResult.TrackedSteps["TranslationModelSet"].SelectMany(static step => step.Outputs),
        static output => Assert.Equal(IncrementalStepRunReason.Cached, output.Reason));
  }
}
