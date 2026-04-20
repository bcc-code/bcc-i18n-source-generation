using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using BccCode.I18N.SourceGen.Tests.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BccCode.I18N.SourceGen.Tests;

internal static class GeneratorTestHelpers
{
    private const string PlaceholderSource = "public sealed class Placeholder { }";

    public static (GeneratorDriver Driver, GeneratorDriverRunResult RunResult, Compilation OutputCompilation) RunGenerator(
        IIncrementalGenerator generator,
        IEnumerable<TestAdditionalFile> additionalFiles,
        string? fallbackLanguage = null,
        bool trackIncrementalSteps = false)
    {
        var compilation = CreateCompilation();
        var driver = CreateDriver(generator, additionalFiles, fallbackLanguage, trackIncrementalSteps);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        return (driver, driver.GetRunResult(), outputCompilation);
    }

    public static (GeneratorDriverRunResult FirstRun, GeneratorDriverRunResult SecondRun) RunGeneratorTwice(
        IIncrementalGenerator generator,
        IEnumerable<TestAdditionalFile> additionalFiles,
        string? fallbackLanguage = null)
    {
        var driver = CreateDriver(generator, additionalFiles, fallbackLanguage, trackIncrementalSteps: true);

        driver = driver.RunGenerators(CreateCompilation());
        var firstRun = driver.GetRunResult();

        driver = driver.RunGenerators(CreateCompilation());
        var secondRun = driver.GetRunResult();

        return (firstRun, secondRun);
    }

    public static ImmutableArray<Diagnostic> GetGeneratorDiagnostics(GeneratorDriverRunResult runResult) =>
        runResult.Results.SelectMany(static result => result.Diagnostics).ToImmutableArray();

    public static string GetGeneratedSource(Compilation compilation, string hintName)
    {
        var tree = compilation.SyntaxTrees.FirstOrDefault(
            syntaxTree => string.Equals(Path.GetFileName(syntaxTree.FilePath), hintName, StringComparison.Ordinal));

        if (tree is null)
        {
            throw new InvalidOperationException($"Generated file '{hintName}' was not found.");
        }

        return tree.ToString();
    }

    private static GeneratorDriver CreateDriver(
        IIncrementalGenerator generator,
        IEnumerable<TestAdditionalFile> additionalFiles,
        string? fallbackLanguage,
        bool trackIncrementalSteps)
    {
        var sourceGenerator = generator.AsSourceGenerator();
        var driverOptions = new GeneratorDriverOptions(
            disabledOutputs: IncrementalGeneratorOutputKind.None,
            trackIncrementalGeneratorSteps: trackIncrementalSteps);

        return CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create(sourceGenerator),
            additionalTexts: additionalFiles.ToImmutableArray<AdditionalText>(),
            parseOptions: CSharpParseOptions.Default,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(fallbackLanguage),
            driverOptions: driverOptions);
    }

    private static CSharpCompilation CreateCompilation()
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "GeneratorTests",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(PlaceholderSource) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly TestAnalyzerConfigOptions _globalOptions;

        public TestAnalyzerConfigOptionsProvider(string? fallbackLanguage)
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(fallbackLanguage))
            {
                options["build_property.FallbackLanguage"] = fallbackLanguage;
            }

            _globalOptions = new TestAnalyzerConfigOptions(options);
        }

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _globalOptions;
    }

    private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> _options;

        public TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string> options)
        {
            _options = options;
        }

        public override bool TryGetValue(string key, out string value)
        {
            if (_options.TryGetValue(key, out var configuredValue))
            {
                value = configuredValue;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}