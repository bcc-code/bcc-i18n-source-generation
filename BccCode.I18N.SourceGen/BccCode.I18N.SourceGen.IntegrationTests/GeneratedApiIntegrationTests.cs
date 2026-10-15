using System.Globalization;
using Xunit;

namespace BccCode.I18N.SourceGen.IntegrationTests;

public class GeneratedApiIntegrationTests
{
    [Fact]
    public void Generated_api_is_available_when_consumed_as_an_analyzer_project_reference()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");

        Assert.Equal("hello world", Language.message.hello1);
        Assert.Equal("hello world", LanguageStrings.GetString(LanguageKeys.message.hello1));
        Assert.Equal("message.hello2", LanguageKeys.message.hello2);
        Assert.Equal("one apple", Language.plural.apple(1));
    }

    [Fact]
    public void Generated_api_pluralizes_when_only_some_languages_have_plural_forms()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("no");
        Assert.Equal("én ting", Language.plural.item(1));
        Assert.Equal("3 ting", Language.plural.item(3));

        CultureInfo.CurrentUICulture = new CultureInfo("en");
        Assert.Equal("item", Language.plural.item(3));
    }

    [Fact]
    public void Generated_api_uses_the_configured_fallback_language()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("de");

        Assert.Equal("hello world", Language.message.hello1);
        Assert.Equal("message.unknown", IntegrationTests.LanguageStrings.GetString("message.unknown"));
        Assert.Null(IntegrationTests.LanguageStrings.GetStringOrNull("message.unknown"));
        Assert.Equal("hello world", LanguageStrings.GetString("message.hello1"));
    }

    [Fact]
    public void LanguageStrings_uses_the_current_culture_when_it_has_translations()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("no");

        Assert.Equal("hei verden", LanguageStrings.GetString("message.hello1"));
        Assert.Equal("{msg} verden", LanguageStrings.GetStringOrNull("message.hello2"));
        Assert.Equal("message.unknown", LanguageStrings.GetString("message.unknown"));
    }

    [Theory]
    [InlineData("nb-NO")]
    [InlineData("nb")]
    [InlineData("nn-NO")]
    [InlineData("no-NO")]
    public void Norwegian_cultures_use_the_norwegian_translations(string culture)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);

        Assert.Equal("hei verden", Language.message.hello1);
        Assert.Equal("hei verden", LanguageStrings.GetString("message.hello1"));
        Assert.Equal("én ting", Language.plural.item(1));
    }

    [Fact]
    public void Region_specific_english_culture_uses_the_english_translations()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");

        Assert.Equal("hello world", Language.message.hello1);
        Assert.Equal("hello world", LanguageStrings.GetString("message.hello1"));
    }

    [Fact]
    public void Unknown_culture_uses_the_fallback_language()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("de-DE");

        Assert.Equal("hello world", Language.message.hello1);
        Assert.Equal("hello world", LanguageStrings.GetString("message.hello1"));
    }

    [Theory]
    [InlineData("nb-NO", "no")]
    [InlineData("nn", "no")]
    [InlineData("no", "no")]
    [InlineData("en-GB", "en")]
    [InlineData("de", "en")]
    [InlineData("", "en")]
    public void ResolveLanguage_returns_the_translation_file_language(string culture, string expected)
    {
        Assert.Equal(expected, LanguageStrings.ResolveLanguage(CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void LanguageStrings_falls_back_per_key_to_the_fallback_language()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("nb-NO");

        Assert.Equal("only in the fallback language", LanguageStrings.GetString("message.fallbackOnly"));
        Assert.Equal("only in the fallback language", LanguageStrings.GetStringOrNull("message.fallbackOnly"));
        Assert.Equal("only in the fallback language", Language.message.fallbackOnly);
        Assert.Null(LanguageStrings.GetStringOrNull("message.unknown"));
    }
}
