using System.Text;
using BccCode.I18N.SourceGen.Tests.Utils;
using Xunit;

namespace BccCode.I18N.SourceGen.Tests;

/// <summary>
/// Key lookups with more arms than <see cref="I18NLanguageDictGenerator.MaxArmsPerKeySwitch"/> are split into
/// hash buckets, because the compiler time for one large string switch grows with the square of its arm count.
/// </summary>
public class LargeTranslationSetTests
{
  private const int KeyCount = 600;

  // Every key has a placeholder, so GetParameterNames is large too. Norwegian translates every other key.
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
  public void Generated_lookups_are_split_into_buckets()
  {
    var (_, _, compilation) = GeneratorTestHelpers.RunGenerator(new I18NSourceGenerator(), Files(), fallbackLanguage: "en");
    var source = GeneratorTestHelpers.GetGeneratedSource(compilation, "LanguageStrings.g.cs");

    Assert.Contains("private static uint GetKeyHash(string key)", source);
    Assert.Contains("private static string? GetStringOrNull0(string key) =>", source);
    Assert.Contains("(GetKeyHash(key) & 15u) switch", source);
    Assert.Contains("private static string? GetStringOrNull0_", source);
    Assert.Contains("private static string[]? GetParameterNames_", source);
  }

  [Fact]
  public void Small_lookups_stay_a_single_switch()
  {
    var files = new[] { new TestAdditionalFile("./en.json", """{ "a": "A", "b": "B {x}" }""") };
    var (_, _, compilation) = GeneratorTestHelpers.RunGenerator(new I18NSourceGenerator(), files, fallbackLanguage: "en");
    var source = GeneratorTestHelpers.GetGeneratedSource(compilation, "LanguageStrings.g.cs");

    Assert.DoesNotContain("GetKeyHash", source);
    Assert.Contains("private static string? GetStringOrNull0(string key) =>", source);
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
  public void Key_hash_does_not_overflow_in_a_checked_context()
  {
    var result = Run("""return LanguageStrings.GetString("group1_key1", new CultureInfo("en"));""", checkedOverflow: true);

    Assert.Equal("en 1 {name}", result);
  }

  [Theory]
  [InlineData("", 2166136261u)]
  [InlineData("a", 0xe40c292cu)]
  [InlineData("foobar", 0xbf9cf968u)]
  public void Key_hash_is_32_bit_fnv1a(string key, uint expected) =>
      Assert.Equal(expected, I18NLanguageDictGenerator.ComputeKeyHash(key));

  [Theory]
  [InlineData(1, 1)]
  [InlineData(64, 1)]
  [InlineData(65, 2)]
  [InlineData(600, 16)]
  [InlineData(4633, 128)]
  public void Bucket_count_is_a_power_of_two_with_at_most_64_arms_on_average(int arms, int expected) =>
      Assert.Equal(expected, I18NLanguageDictGenerator.GetBucketCount(arms));
}
