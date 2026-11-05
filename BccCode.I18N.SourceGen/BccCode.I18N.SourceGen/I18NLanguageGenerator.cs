using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace BccCode.I18N.SourceGen;

/// <summary>
/// Generates <c>Language</c> (static, current culture) and <c>LocalizedLanguage</c> (explicit culture) with its groups.
/// Every member calls <c>LanguageStrings.Translate</c> with the index of its key. Reports I18N006.
/// </summary>
internal static class I18NLanguageGenerator
{
    private const string LanguageClass = "Language";
    private const string LocalizedStruct = "LocalizedLanguage";
    private const string GroupsClass = "LocalizedLanguageGroups";
    private const string StringsClass = "LanguageStrings";

    private const string CultureInfoType = "global::System.Globalization.CultureInfo";
    private const string LanguageFieldBase = "__language";
    private const string CultureFieldBase = "__culture";

    public static void AddSource(SourceProductionContext context, TranslationModelSet translations)
    {
        var ns = translations.GeneratedNamespace;

        var translationsByKey = BuildTranslationsByKey(translations.Files);
        var filePathsByLanguage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in translations.Files)
        {
            filePathsByLanguage[file.Language] = file.FilePath;
        }

        var root = TranslationTreeBuilder.BuildTree(translations.FallbackFile.Entries);
        var keyIndex = TranslationKeyIndex.Create(translations.Files);
        var generationContext = new GenerationContext(context, translationsByKey, filePathsByLanguage, translations.FallbackLanguage, ns, keyIndex);
        var plan = Plan.Create(root, generationContext);
        foreach (var leaf in plan.Leaves)
        {
            ReportPlaceholderMismatches(generationContext, leaf);
        }

        var languageWriter = GeneratedFile.Begin(ns, LanguageClass);
        WriteLanguageMembers(languageWriter, generationContext, root, plan);
        GeneratedFile.AddSource(context, languageWriter, ns, LanguageClass);

        var structWriter = GeneratedFile.Begin(ns, LocalizedStruct, "public readonly struct " + LocalizedStruct);
        var rootFields = plan.GetFields(root);
        WriteFields(structWriter, rootFields);
        structWriter.AppendLine();
        structWriter.AppendLine("/// <summary>Creates a language accessor that selects translations and formats values for <paramref name=\"culture\"/>.</summary>");
        structWriter.Append("public ").Append(LocalizedStruct).Append('(').Append(CultureInfoType).AppendLine(" culture)");
        structWriter.OpenBlock();
        structWriter.Append("this.").Append(rootFields.Culture).AppendLine(" = culture;");
        structWriter.Append("this.").Append(rootFields.Language).Append(" = ").Append(Qualify(ns, StringsClass))
            .AppendLine(".ResolveLanguage(culture);");
        structWriter.CloseBlock();
        WriteInstanceMembers(structWriter, generationContext, root, plan);
        GeneratedFile.AddSource(context, structWriter, ns, LocalizedStruct);

        var groupsWriter = GeneratedFile.Begin(
            ns,
            GroupsClass,
            null,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        var firstGroup = true;
        foreach (var group in plan.Groups)
        {
            if (!firstGroup)
            {
                groupsWriter.AppendLine();
            }

            firstGroup = false;
            groupsWriter.Append("public readonly struct ").AppendLine(group.TypeName);
            groupsWriter.OpenBlock();
            var groupFields = plan.GetFields(group.Node);
            WriteFields(groupsWriter, groupFields);
            groupsWriter.AppendLine();
            groupsWriter.Append("internal ").Append(group.TypeName).Append("(string? language, ").Append(CultureInfoType).AppendLine("? culture)");
            groupsWriter.OpenBlock();
            groupsWriter.Append("this.").Append(groupFields.Language).AppendLine(" = language;");
            groupsWriter.Append("this.").Append(groupFields.Culture).AppendLine(" = culture;");
            groupsWriter.CloseBlock();
            WriteInstanceMembers(groupsWriter, generationContext, group.Node, plan);
            groupsWriter.CloseBlock();
        }

        GeneratedFile.AddSource(context, groupsWriter, ns, GroupsClass);
    }

    private static string Qualify(string generatedNamespace, string typeName) =>
        string.IsNullOrWhiteSpace(generatedNamespace)
            ? "global::" + typeName
            : "global::" + generatedNamespace + "." + typeName;

    private sealed class GenerationContext
    {
        public GenerationContext(
            SourceProductionContext sourceContext,
            Dictionary<string, Dictionary<string, string>> translationsByKey,
            Dictionary<string, string> filePathsByLanguage,
            string fallbackLanguage,
            string generatedNamespace,
            TranslationKeyIndex keyIndex)
        {
            SourceContext = sourceContext;
            TranslationsByKey = translationsByKey;
            FilePathsByLanguage = filePathsByLanguage;
            FallbackLanguage = fallbackLanguage;
            KeyIndex = keyIndex;
            StringsType = Qualify(generatedNamespace, StringsClass);
            GroupsType = Qualify(generatedNamespace, GroupsClass);
            // Fully qualified so that a key named LanguageStrings cannot shadow it.
            CurrentLanguage = StringsType + ".ResolveLanguage(" + CultureInfoType + ".CurrentUICulture)";
        }

        public SourceProductionContext SourceContext { get; }

        public Dictionary<string, Dictionary<string, string>> TranslationsByKey { get; }

        public Dictionary<string, string> FilePathsByLanguage { get; }

        public string FallbackLanguage { get; }

        public TranslationKeyIndex KeyIndex { get; }

        /// <summary>Fully qualified, so that a key named LanguageStrings cannot shadow it.</summary>
        public string StringsType { get; }

        public string GroupsType { get; }

        public string CurrentLanguage { get; }
    }

    private sealed class LeafInfo
    {
        public LeafInfo(
            TranslationNode node,
            bool isPlural,
            string[] parameters,
            Dictionary<string, string> translations,
            int keyIndex)
        {
            Node = node;
            IsPlural = isPlural;
            Parameters = parameters;
            Identifiers = TranslationGeneratorUtilities.GetParameterIdentifiers(parameters);
            Translations = translations;
            KeyIndex = keyIndex;
        }

        public TranslationNode Node { get; }

        /// <summary>The index of the key in <c>LanguageStrings</c>.</summary>
        public int KeyIndex { get; }

        public bool IsPlural { get; }

        public string[] Parameters { get; }

        public string[] Identifiers { get; }

        public Dictionary<string, string> Translations { get; }

        public string KeyPath => Node.KeyPath!;

        public string FallbackValue => Node.Value!;

        public string MemberName => TranslationGeneratorUtilities.SanitizeName(Node.Name);

        public string GetParameterType(int index) => IsPlural && index == 0 ? "int" : "object";
    }

    private static string AllocateName(string baseName, HashSet<string> taken)
    {
        var name = baseName;
        for (var suffix = 2; !taken.Add(name); suffix++)
        {
            name = baseName + suffix;
        }

        return name;
    }

    private sealed class FieldNames
    {
        public FieldNames(string language, string culture)
        {
            Language = language;
            Culture = culture;
        }

        public string Language { get; }

        public string Culture { get; }
    }

    private sealed class GroupInfo
    {
        public GroupInfo(TranslationNode node, string typeName)
        {
            Node = node;
            TypeName = typeName;
        }

        public TranslationNode Node { get; }

        public string TypeName { get; }
    }

    /// <summary>
    /// Names and analysis shared by the generated files. Group struct types get
    /// names derived from the key path (sanitized, joined with '_'). Collisions get a numeric suffix, and a
    /// group type also avoids the names of its own members.
    /// </summary>
    private sealed class Plan
    {
        private readonly Dictionary<TranslationNode, LeafInfo> _leavesByNode = new();
        private readonly Dictionary<TranslationNode, GroupInfo> _groupsByNode = new();
        private readonly HashSet<string> _usedGroupNames = new(StringComparer.Ordinal) { GroupsClass };
        private readonly Dictionary<TranslationNode, string> _instanceMemberNames = new();
        private readonly Dictionary<TranslationNode, FieldNames> _fieldNames = new();

        public List<LeafInfo> Leaves { get; } = new();

        public List<GroupInfo> Groups { get; } = new();

        public LeafInfo GetLeaf(TranslationNode node) => _leavesByNode[node];

        public bool TryGetLeaf(TranslationNode node, out LeafInfo leaf) => _leavesByNode.TryGetValue(node, out leaf!);

        public GroupInfo GetGroup(TranslationNode node) => _groupsByNode[node];

        /// <summary>The name of the child as a member of the LocalizedLanguage struct or a group struct.</summary>
        public string GetInstanceMemberName(TranslationNode child) => _instanceMemberNames[child];

        /// <summary>The backing field names of the struct for <paramref name="node"/> (the root, or a group).</summary>
        public FieldNames GetFields(TranslationNode node) => _fieldNames[node];

        public static Plan Create(TranslationNode root, GenerationContext context)
        {
            var plan = new Plan();
            plan.Visit(root, string.Empty, LocalizedStruct, context);
            return plan;
        }

        private void Visit(TranslationNode node, string path, string typeName, GenerationContext context)
        {
            AssignStructNames(node, typeName);
            foreach (var child in node.Children.Values)
            {
                var childPath = path.Length == 0 ? child.Name : path + "." + child.Name;
                if (child.Children.Count > 0)
                {
                    var group = new GroupInfo(child, AllocateGroupName(child, childPath));
                    _groupsByNode.Add(child, group);
                    Groups.Add(group);
                    Visit(child, childPath, group.TypeName, context);
                    continue;
                }

                if (child.KeyPath is null || child.Value is null || !context.TranslationsByKey.TryGetValue(child.KeyPath, out var translations))
                {
                    continue;
                }

                var leaf = CreateLeaf(child, translations, context.KeyIndex);
                _leavesByNode.Add(child, leaf);
                Leaves.Add(leaf);
            }
        }

        // A member cannot have the name of its enclosing type, and the fields must not collide with members.
        // A member named like the struct gets a '_' suffix (and a number if that collides too).
        private void AssignStructNames(TranslationNode node, string typeName)
        {
            var taken = new HashSet<string>(StringComparer.Ordinal) { typeName };
            foreach (var child in node.Children.Values)
            {
                taken.Add(TranslationGeneratorUtilities.SanitizeName(child.Name));
            }

            foreach (var child in node.Children.Values)
            {
                var name = TranslationGeneratorUtilities.SanitizeName(child.Name);
                if (string.Equals(name, typeName, StringComparison.Ordinal))
                {
                    var baseName = name + "_";
                    name = baseName;
                    for (var suffix = 2; taken.Contains(name); suffix++)
                    {
                        name = baseName + suffix;
                    }

                    taken.Add(name);
                }

                _instanceMemberNames[child] = name;
            }

            _fieldNames[node] = new FieldNames(
                AllocateName(LanguageFieldBase, taken),
                AllocateName(CultureFieldBase, taken));
        }

        private string AllocateGroupName(TranslationNode node, string path)
        {
            var memberNames = new HashSet<string>(
                node.Children.Values.Select(static child => TranslationGeneratorUtilities.SanitizeName(child.Name)),
                StringComparer.Ordinal);
            var baseName = TranslationGeneratorUtilities.SanitizeName(path.Replace('.', '_'));
            var name = baseName;
            for (var suffix = 2; _usedGroupNames.Contains(name) || memberNames.Contains(name); suffix++)
            {
                name = baseName + suffix;
            }

            _usedGroupNames.Add(name);
            return name;
        }

        private static LeafInfo CreateLeaf(TranslationNode node, Dictionary<string, string> translations, TranslationKeyIndex keyIndex)
        {
            var fallbackValue = node.Value!;

            // A key is plural when any language uses pipe-separated variants, so one language
            // can pluralize while the others (including the fallback) use a single form.
            var parameters = TranslationGeneratorUtilities.GetKeyParameters(fallbackValue, translations.Values, out var isPlural);

            return new LeafInfo(node, isPlural, parameters, translations, keyIndex.GetIndex(node.KeyPath!));
        }
    }

    private static Dictionary<string, Dictionary<string, string>> BuildTranslationsByKey(EquatableArray<LanguageFileModel> files)
    {
        var translationsByKey = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (var entry in file.Entries)
            {
                if (!translationsByKey.TryGetValue(entry.KeyPath, out var valuesByLanguage))
                {
                    valuesByLanguage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    translationsByKey.Add(entry.KeyPath, valuesByLanguage);
                }

                valuesByLanguage[file.Language] = entry.Value;
            }
        }

        return translationsByKey;
    }

    private static void WriteFields(IndentedWriter writer, FieldNames fields)
    {
        writer.Append("private readonly string? ").Append(fields.Language).AppendLine(";");
        writer.Append("private readonly ").Append(CultureInfoType).Append("? ").Append(fields.Culture).AppendLine(";");
    }

    // Language: the static members select the language from CurrentUICulture and format with CurrentCulture.
    private static void WriteLanguageMembers(IndentedWriter writer, GenerationContext context, TranslationNode node, Plan plan)
    {
        foreach (var child in node.Children.Values)
        {
            if (child.Children.Count > 0)
            {
                writer.Append("public static class ").AppendLine(TranslationGeneratorUtilities.SanitizeName(child.Name));
                writer.OpenBlock();
                WriteLanguageMembers(writer, context, child, plan);
                writer.CloseBlock();
                continue;
            }

            if (!plan.TryGetLeaf(child, out var leaf))
            {
                continue;
            }

            WriteLeafSignature(writer, leaf, "public static string ");
            writer.Indent();
            WriteTranslationCall(writer, context, leaf, context.CurrentLanguage, CultureInfoType + ".CurrentCulture");
            writer.Outdent();
            writer.AppendLine();
        }
    }

    // LocalizedLanguage and its group structs: language and formatting both come from the stored culture.
    // default(LocalizedLanguage) has no culture and uses the fallback language with invariant formatting.
    private static void WriteInstanceMembers(IndentedWriter writer, GenerationContext context, TranslationNode node, Plan plan)
    {
        // Fields are accessed through this. so that a placeholder parameter with the same name cannot shadow them.
        var fields = plan.GetFields(node);
        foreach (var child in node.Children.Values)
        {
            if (child.Children.Count > 0)
            {
                var group = plan.GetGroup(child);
                var groupType = context.GroupsType + "." + group.TypeName;
                writer.AppendLine();
                writer.Append("public ").Append(groupType).Append(' ').Append(plan.GetInstanceMemberName(child))
                    .Append(" => new ").Append(groupType).Append("(this.").Append(fields.Language).Append(", this.").Append(fields.Culture).AppendLine(");");
                continue;
            }

            if (!plan.TryGetLeaf(child, out var leaf))
            {
                continue;
            }

            writer.AppendLine();
            WriteLeafSignature(writer, leaf, "public string ", plan.GetInstanceMemberName(child));
            writer.Indent();
            WriteTranslationCall(writer, context, leaf, "this." + fields.Language, "this." + fields.Culture + " ?? " + CultureInfoType + ".InvariantCulture");
            writer.Outdent();
        }
    }

    private static void WriteLeafSignature(IndentedWriter writer, LeafInfo leaf, string prefix, string? memberName = null)
    {
        writer.Append("/// ").AppendLine(TranslationGeneratorUtilities.EscapeXmlDoc(leaf.FallbackValue));
        writer.Append(prefix).Append(memberName ?? leaf.MemberName);
        if (leaf.Parameters.Length > 0)
        {
            writer.Append('(');
            for (var index = 0; index < leaf.Identifiers.Length; index++)
            {
                if (index > 0)
                {
                    writer.Append(", ");
                }

                writer.Append(leaf.GetParameterType(index)).Append(' ').Append(leaf.Identifiers[index]);
            }

            writer.Append(')');
        }

        writer.AppendLine(" =>");
    }

    // The call carries the key as a comment for debugging.
    private static void WriteTranslationCall(
        IndentedWriter writer,
        GenerationContext context,
        LeafInfo leaf,
        string languageExpression,
        string providerExpression)
    {
        writer.Append(context.StringsType).Append(".Translate(").Append(leaf.KeyIndex.ToString(CultureInfo.InvariantCulture))
            .Append(" /* ").Append(SanitizeComment(leaf.KeyPath)).Append(" */, ")
            .Append(languageExpression).Append(", ").Append(providerExpression);
        if (leaf.Identifiers.Length > 0)
        {
            writer.Append(", new object?[] { ").Append(string.Join(", ", leaf.Identifiers)).Append(" }");
        }

        writer.AppendLine(");");
    }

    // The text of a /* */ comment must not contain the end of the comment or a line break.
    private static string SanitizeComment(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            builder.Append(char.IsControl(character) || character == (char)0x2028 || character == (char)0x2029 ? ' ' : character);
        }

        return builder.ToString().Replace("*/", "* /");
    }

    // A language whose message uses placeholders that the key does not have is skipped as a whole in favor of the
    // fallback language. LanguageStrings knows these (language, key) pairs, and this reports each of them once.
    private static void ReportPlaceholderMismatches(GenerationContext context, LeafInfo leaf)
    {
        var fallbackLanguage = context.FallbackLanguage;
        var otherTranslations = leaf.Translations
            .Where(pair => !string.Equals(pair.Key, fallbackLanguage, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal);

        foreach (var translation in otherTranslations)
        {
            // For a plural message the first variant with an unsupported placeholder is reported.
            var parts = leaf.IsPlural
                ? TranslationGeneratorUtilities.SplitPluralParts(translation.Value)
                : new[] { translation.Value };
            foreach (var part in parts)
            {
                var unsupportedPlaceholders = TranslationGeneratorUtilities.GetUnsupportedPlaceholders(part, leaf.Parameters);
                if (unsupportedPlaceholders.Length == 0)
                {
                    continue;
                }

                var location = context.FilePathsByLanguage.TryGetValue(translation.Key, out var filePath)
                    ? new LocationInfo(filePath).ToLocation()
                    : Location.None;

                context.SourceContext.ReportDiagnostic(Diagnostic.Create(
                    TranslationGeneratorDiagnostics.PlaceholderMismatch,
                    location,
                    $"Translation key '{leaf.KeyPath}' in language '{translation.Key}' uses placeholders [{string.Join(", ", unsupportedPlaceholders)}] that are not present in the fallback language. The fallback translation will be used instead."));
                break;
            }
        }
    }
}
