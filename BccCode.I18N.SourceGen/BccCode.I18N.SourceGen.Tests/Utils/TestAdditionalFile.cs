using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace BccCode.I18N.SourceGen.Tests.Utils;

public class TestAdditionalFile : AdditionalText
{
    private readonly SourceText _text;

    public TestAdditionalFile(string path, string text, string? translationMetadata = null)
    {
        Path = path;
        TranslationMetadata = translationMetadata;
        _text = SourceText.From(text);
    }

    public override SourceText GetText(CancellationToken cancellationToken = new()) => _text;

    public override string Path { get; }

    /// <summary>Value of the I18NTranslation item metadata, or null when the metadata is not set.</summary>
    public string? TranslationMetadata { get; }
}
