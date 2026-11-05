using System;
using System.Linq;
using System.Text;
using BccCode.I18N.SourceGen.Tests.Utils;
using Xunit;

namespace BccCode.I18N.SourceGen.Tests;

/// <summary>
/// The translations are emitted as data arrays with a key index, so the compiler time grows linearly with the number
/// of keys (a switch over all keys grows with the square). These tests run a large set through the generated code.
/// </summary>
public class LargeTranslationSetTests
{
  private const int KeyCount = 600;

  // Every key has a placeholder, so the parameter names array is large too. Norwegian translates every other key.
  private static TestAdditionalFile[] Files()
  {
    var en = new StringBuilder("{");
    var no = new StringBuilder("{");
    for (var index = 0; index < KeyCount; index++)
    {
      var separator = index == 0 ? "" : ",";
      en.Append(separator).Append($"\"group{index % 7}_key{index}\": \"en {index} {{name}}\"");
      if (index % 2 == 0)
      {
        no.Append(no.Length == 1 ? "" : ",").Append($"\"group{index % 7}_key{index}\": \"no {index} {{name}}\"");
      }
    }

    return [new TestAdditionalFile("./en.json", en.Append('}').ToString()), new TestAdditionalFile("./no.json", no.Append('}').ToString())];
  }

  private static string Run(string body, bool checkedOverflow = false) =>
      LocalizedLanguageTests.Run(Files(), body, checkedOverflow: checkedOverflow);

  [Fact]
  public void Generated_lookups_are_arrays_and_not_switches_over_the_keys()
  {
    var (_, _, compilation) = GeneratorTestHelpers.RunGenerator(new I18NSourceGenerator(), Files(), fallbackLanguage: "en");
    var source = GeneratorTestHelpers.GetGeneratedSource(compilation, "LanguageStrings.g.cs");

    Assert.Contains("private static string[] KeysData() =>", source);
    Assert.Contains("private static string[]?[] ParameterNamesData() =>", source);
    Assert.Contains("private static string?[] StringsData0() =>", source);
    Assert.Contains("private static string?[] StringsData1() =>", source);
    Assert.DoesNotContain("GetKeyHash", source);
    Assert.DoesNotContain("key switch", source);
    Assert.DoesNotContain("GetStringOrNull0", source);
  }

  [Fact]
  public void Typed_members_call_the_helper_with_the_key_index()
  {
    var (_, _, compilation) = GeneratorTestHelpers.RunGenerator(new I18NSourceGenerator(), Files(), fallbackLanguage: "en");
    var source = GeneratorTestHelpers.GetGeneratedSource(compilation, "Language.g.cs");

    // Ordinal order: group0_key0, group0_key105, ... The index of group1_key1 is its position in that order.
    var keys = Enumerable.Range(0, KeyCount).Select(static index => $"group{index % 7}_key{index}").OrderBy(static key => key, StringComparer.Ordinal).ToList();
    var position = keys.IndexOf("group1_key1");
    Assert.Contains($"LanguageStrings.Translate({position} /* group1_key1 */, ", source);
  }

  [Fact]
  public void Every_key_resolves_in_its_language_with_fallback()
  {
    var result = Run(
        $$"""
        var nb = new CultureInfo("nb-NO");
        var en = new CultureInfo("en");
        var wrong = 0;
        for (var index = 0; index < {{KeyCount}}; index++)
        {
            var key = "group" + (index % 7) + "_key" + index;
            var expectedNo = (index % 2 == 0 ? "no " : "en ") + index + " {name}";
            if (LanguageStrings.GetStringOrNull(key, en) != "en " + index + " {name}") wrong++;
            if (LanguageStrings.GetStringOrNull(key, nb) != expectedNo) wrong++;
            if (LanguageStrings.Format(key, nb, "Kari") != expectedNo.Replace("{name}", "Kari")) wrong++;
        }

        return string.Join("|", wrong, LanguageStrings.GetStringOrNull("group0_key1", nb) ?? "null",
            LanguageStrings.GetString("missing", nb), LanguageStrings.GetStringOrNull(null!, nb) ?? "null");
        """);

    Assert.Equal("0|null|missing|null", result);
  }

  [Fact]
  public void Lookups_work_in_a_checked_context()
  {
    var result = Run("""return LanguageStrings.GetString("group1_key1", new CultureInfo("en"));""", checkedOverflow: true);

    Assert.Equal("en 1 {name}", result);
  }

  [Fact]
  public void Typed_members_and_Format_agree_in_a_large_set()
  {
    var result = Run(
        $$"""
        var nb = new LocalizedLanguage(new CultureInfo("nb-NO"));
        var wrong = 0;
        for (var index = 0; index < {{KeyCount}}; index += 7)
        {
            var key = "group" + (index % 7) + "_key" + index;
            if (LanguageStrings.Format(key, new CultureInfo("nb-NO"), "Kari") != (index % 2 == 0 ? "no " : "en ") + index + " Kari") wrong++;
        }

        return string.Join("|", wrong, nb.group0_key0("Kari"), nb.group1_key1("Kari"), Language.group0_key0("Kari"));
        """);

    Assert.Equal("0|no 0 Kari|en 1 Kari|en 0 Kari", result);
  }
}
