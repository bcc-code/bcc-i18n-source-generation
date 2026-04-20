using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using BccCode.I18N.SourceGen.Tests.Utils;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BccCode.I18N.SourceGen.Tests;

public class SourceGeneratorWithAdditionalFilesTests
{
    private const string no = """
                              {
                                "message": {
                                  "hello1": "hei verden",
                                  "hello2": "{msg} verden"
                                },
                                "plural":
                                {
                                  "car": "bil | biler",
                                  "apple": "ingen epler | et eple | {count} epler",
                                  "banana": "ingen bananer | {n} banan | {n} bananer"
                                }
                              }
                              """;
    private const string en = """
                              {
                                "message": {
                                  "hello1": "hello world",
                                  "hello2": "{msg} world"
                                },
                                "plural":
                                {
                                  "car": "car | cars",
                                  "apple": "no apples | one apple | {count} apple",
                                  "banana": "no bananas | {n} banana | {n} bananas"
                              
                                }
                              }
                              """;
    

    [Fact]
    public void GenerateClassesBasedOnDDDRegistry()
    {
        // Create an instance of the source generator.
        var generator = new I18NLanguageGenerator();

        // Source generators should be tested using 'GeneratorDriver'.
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        // Add the additional file separately from the compilation.
        driver = driver.AddAdditionalTexts(
        [
            new TestAdditionalFile("./no.json", no),
            new TestAdditionalFile("./en.json", en)
        ]);

        // To run generators, we can use an empty compilation.
        var compilation = CSharpCompilation.Create(nameof(SourceGeneratorWithAdditionalFilesTests));

        // Run generators. Don't forget to use the new compilation rather than the previous one.
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var newCompilation, out _);

        // Retrieve all files in the compilation.
        var generatedFiles = newCompilation.SyntaxTrees
            .Select(t => Path.GetFileName(t.FilePath))
            .ToArray();

        // In this case, it is enough to check the file name.
        Assert.Equivalent(new[]
        {
            "Language.g.cs",
        }, generatedFiles);
    }
}