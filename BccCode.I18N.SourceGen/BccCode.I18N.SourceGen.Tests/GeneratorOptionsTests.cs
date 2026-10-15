using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace BccCode.I18N.SourceGen.Tests;

public class GeneratorOptionsTests
{
    private static TranslationGeneratorOptions GetOptions(string? generatedNamespace, string? rootNamespace)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (generatedNamespace is not null)
        {
            values["build_property.GeneratedNamespace"] = generatedNamespace;
        }

        if (rootNamespace is not null)
        {
            values["build_property.RootNamespace"] = rootNamespace;
        }

        return TranslationFileParser.GetOptions(new OptionsProvider(values));
    }

    [Fact]
    public void GeneratedNamespace_takes_precedence_over_RootNamespace()
    {
        Assert.Equal("MyApp.Localization", GetOptions("MyApp.Localization", "MyApp").GeneratedNamespace);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Empty_GeneratedNamespace_falls_back_to_RootNamespace(string? generatedNamespace)
    {
        Assert.Equal("MyApp.Web", GetOptions(generatedNamespace, "MyApp.Web").GeneratedNamespace);
    }

    [Theory]
    [InlineData("My-App", "My_App")]
    [InlineData("1App.Web", "_1App.Web")]
    [InlineData("MyApp.class", "MyApp.@class")]
    [InlineData(" MyApp ", "MyApp")]
    public void RootNamespace_fallback_is_made_a_valid_namespace(string rootNamespace, string expected)
    {
        Assert.Equal(expected, GetOptions(null, rootNamespace).GeneratedNamespace);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Without_GeneratedNamespace_or_RootNamespace_the_global_namespace_is_used(string? rootNamespace)
    {
        var options = GetOptions(null, rootNamespace);

        Assert.Equal(string.Empty, options.GeneratedNamespace);
        Assert.False(options.HasGeneratedNamespace);
    }

    private sealed class OptionsProvider(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptionsProvider
    {
        private readonly Options _options = new(values);

        public override AnalyzerConfigOptions GlobalOptions => _options;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;
    }

    private sealed class Options(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (values.TryGetValue(key, out var configured))
            {
                value = configured;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
