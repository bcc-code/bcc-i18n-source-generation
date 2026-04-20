using System.Globalization;
using Xunit;

namespace BccCode.I18N.SourceGen.IntegrationTests;

public class GeneratedApiIntegrationTests
{
    [Fact]
    public void Generated_api_is_available_when_consumed_as_an_analyzer_project_reference()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");

        Assert.Equal("hello world", IntegrationTests.Language.message.hello1);
        Assert.Equal("hello world", IntegrationTests.LanguageStrings.GetString(LanguageKeys.message.hello1));
        Assert.Equal("message.hello2", IntegrationTests.LanguageKeys.message.hello2);
        Assert.Equal("one apple", IntegrationTests.Language.plural.apple(1));
    }

    [Fact]
    public void Generated_api_uses_the_configured_fallback_language()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("de");

        Assert.Equal("hello world", Language.message.hello1);
        Assert.Equal("message.unknown", IntegrationTests.LanguageStrings.GetString("message.unknown"));
        Assert.Null(IntegrationTests.LanguageStrings.GetStringOrNull("message.unknown"));
    }
}