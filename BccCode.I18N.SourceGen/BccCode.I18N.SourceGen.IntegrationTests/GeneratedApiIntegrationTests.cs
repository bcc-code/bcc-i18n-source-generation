using System.Globalization;
using Xunit;

namespace BccCode.I18N.SourceGen.IntegrationTests;

public class GeneratedApiIntegrationTests
{
    [Fact]
    public void Generated_api_is_available_when_consumed_as_an_analyzer_project_reference()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");

        Assert.Equal("hello world", global::Language.message.hello1);
        Assert.Equal("hello world", global::I18NStrings.GetString(global::I18N.message.hello1));
        Assert.Equal("message.hello2", global::I18N.message.hello2);
        Assert.Equal("one apple", global::Language.plural.apple(1));
    }

    [Fact]
    public void Generated_api_uses_the_configured_fallback_language()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("de");

        Assert.Equal("hello world", global::Language.message.hello1);
        Assert.Equal("message.unknown", global::I18NStrings.GetString("message.unknown"));
        Assert.Null(global::I18NStrings.GetStringOrNull("message.unknown"));
    }
}