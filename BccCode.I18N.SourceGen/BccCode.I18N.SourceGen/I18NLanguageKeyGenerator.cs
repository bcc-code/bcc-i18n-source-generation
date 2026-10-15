using Microsoft.CodeAnalysis;

namespace BccCode.I18N.SourceGen;

internal static class I18NLanguageKeyGenerator
{
    public static void AddSource(SourceProductionContext context, LanguageKeysModel model)
    {
        const string className = "LanguageKeys";

        var writer = GeneratedFile.Begin(model.GeneratedNamespace, className);
        WriteMembers(writer, TranslationTreeBuilder.BuildTree(model.Entries));
        GeneratedFile.AddSource(context, writer, model.GeneratedNamespace, className);
    }

    private static void WriteMembers(IndentedWriter writer, TranslationNode node)
    {
        foreach (var child in node.Children.Values)
        {
            if (child.Children.Count > 0)
            {
                writer.Append("public static class ").AppendLine(TranslationGeneratorUtilities.SanitizeName(child.Name));
                writer.OpenBlock();
                WriteMembers(writer, child);
                writer.CloseBlock();
            }
            else
            {
                writer.Append("/// ").AppendLine(TranslationGeneratorUtilities.EscapeXmlDoc(child.Value ?? string.Empty));
                writer.Append("public const string ")
                    .Append(TranslationGeneratorUtilities.SanitizeName(child.Name))
                    .Append(" = ")
                    .Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(child.KeyPath ?? string.Empty))
                    .AppendLine(";");
            }
        }
    }
}
