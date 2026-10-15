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

    [Fact]
    public void LocalizedLanguage_uses_the_given_culture_without_changing_the_thread_culture()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");

        var norwegian = new LocalizedLanguage(new CultureInfo("nb-NO"));
        var english = new LocalizedLanguage(new CultureInfo("en-US"));
        var unknown = new LocalizedLanguage(new CultureInfo("de-DE"));

        Assert.Equal("hei verden", norwegian.message.hello1);
        Assert.Equal("hello world", english.message.hello1);
        Assert.Equal("hello world", unknown.message.hello1);
        Assert.Equal("x verden", norwegian.message.hello2("x"));
        Assert.Equal("3 epler", norwegian.plural.apple(3));
        Assert.Equal("3 apples", english.plural.apple(3));
        Assert.Equal("only in the fallback language", norwegian.message.fallbackOnly);
        Assert.Equal("hello world", Language.message.hello1);
        Assert.Equal("en", CultureInfo.CurrentUICulture.Name);
    }

    [Fact]
    public void Default_LocalizedLanguage_uses_the_fallback_language()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("nb-NO");

        Assert.Equal("hello world", default(LocalizedLanguage).message.hello1);
    }

    [Fact]
    public void Format_specifiers_are_applied_with_the_culture_of_the_language()
    {
        var norwegian = new CultureInfo("nb-NO");
        var american = new CultureInfo("en-US");

        Assert.Equal("Pris: " + 1234.5m.ToString("N2", norwegian), new LocalizedLanguage(norwegian).message.price(1234.5m));
        Assert.Equal("Price: 1,234.50", new LocalizedLanguage(american).message.price(1234.5m));

        CultureInfo.CurrentUICulture = american;
        CultureInfo.CurrentCulture = norwegian;
        Assert.Equal("Price: " + 1234.5m.ToString("N2", norwegian), Language.message.price(1234.5m));
    }

    [Fact]
    public void LanguageStrings_culture_overloads_do_not_depend_on_the_current_culture()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");

        Assert.Equal("hei verden", LanguageStrings.GetString("message.hello1", new CultureInfo("nb-NO")));
        Assert.Equal("only in the fallback language", LanguageStrings.GetString("message.fallbackOnly", new CultureInfo("nb-NO")));
        Assert.Null(LanguageStrings.GetStringOrNull("message.unknown", new CultureInfo("nb-NO")));
    }

    [Fact]
    public void Linked_messages_are_resolved_at_generation_time()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        Assert.Equal("Press Save.", Language.message.press);
        Assert.Equal("Press Save.", LanguageStrings.GetString("message.press"));

        CultureInfo.CurrentUICulture = new CultureInfo("nb-NO");
        Assert.Equal("Trykk Lagre.", Language.message.press);
        Assert.Equal("Trykk Lagre.", new LocalizedLanguage(new CultureInfo("nb-NO")).message.press);
    }

    [Fact]
    public void Two_form_plurals_use_the_first_form_only_for_a_count_of_one()
    {
        var english = new LocalizedLanguage(new CultureInfo("en"));
        var norwegian = new LocalizedLanguage(new CultureInfo("nb-NO"));

        Assert.Equal("cars", english.plural.car(0));
        Assert.Equal("car", english.plural.car(1));
        Assert.Equal("cars", english.plural.car(2));
        Assert.Equal("car", english.plural.car(-1));
        Assert.Equal("bil", norwegian.plural.car(1));
        Assert.Equal("biler", norwegian.plural.car(2));
    }
}
