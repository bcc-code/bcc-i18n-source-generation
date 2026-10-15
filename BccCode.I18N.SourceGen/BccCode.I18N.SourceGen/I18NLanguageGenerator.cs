using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BccCode.I18N.SourceGen;

/// <summary>
/// Generates <c>Language</c> (static, current culture), <c>LocalizedLanguage</c> (explicit culture) and the
/// internal <c>LanguageTranslations</c> that holds the single translation switch per key used by both.
/// </summary>
internal static class I18NLanguageGenerator
{
    private const string LanguageClass = "Language";
    private const string LocalizedStruct = "LocalizedLanguage";
    private const string GroupsClass = "LocalizedLanguageGroups";
    private const string TranslationsClass = "LanguageTranslations";

    private const string CultureInfoType = "global::System.Globalization.CultureInfo";
    private const string LanguageField = "__language";
    private const string CultureField = "__culture";

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
        var generationContext = new GenerationContext(context, translationsByKey, filePathsByLanguage, translations.FallbackLanguage, ns);
        var plan = Plan.Create(root, generationContext);

        var languageWriter = GeneratedFile.Begin(ns, LanguageClass);
        WriteLanguageMembers(languageWriter, generationContext, root, plan);
        GeneratedFile.AddSource(context, languageWriter, ns, LanguageClass);

        var translationsWriter = GeneratedFile.Begin(ns, TranslationsClass, "internal static class " + TranslationsClass);
        foreach (var leaf in plan.Leaves)
        {
            WriteTranslationMethod(translationsWriter, generationContext, leaf);
        }

        GeneratedFile.AddSource(context, translationsWriter, ns, TranslationsClass);

        var structWriter = GeneratedFile.Begin(ns, LocalizedStruct, "public readonly struct " + LocalizedStruct);
        WriteFields(structWriter);
        structWriter.AppendLine();
        structWriter.AppendLine("/// <summary>Creates a language accessor that selects translations and formats values for <paramref name=\"culture\"/>.</summary>");
        structWriter.Append("public ").Append(LocalizedStruct).Append('(').Append(CultureInfoType).AppendLine(" culture)");
        structWriter.OpenBlock();
        structWriter.Append(CultureField).AppendLine(" = culture;");
        structWriter.Append(LanguageField).Append(" = ").Append(Qualify(ns, "LanguageStrings"))
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
            WriteFields(groupsWriter);
            groupsWriter.AppendLine();
            groupsWriter.Append("internal ").Append(group.TypeName).Append("(string? language, ").Append(CultureInfoType).AppendLine("? culture)");
            groupsWriter.OpenBlock();
            groupsWriter.Append(LanguageField).AppendLine(" = language;");
            groupsWriter.Append(CultureField).AppendLine(" = culture;");
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
            string generatedNamespace)
        {
            SourceContext = sourceContext;
            TranslationsByKey = translationsByKey;
            FilePathsByLanguage = filePathsByLanguage;
            FallbackLanguage = fallbackLanguage;
            TranslationsType = Qualify(generatedNamespace, TranslationsClass);
            GroupsType = Qualify(generatedNamespace, GroupsClass);
            // Fully qualified so that a key named LanguageStrings cannot shadow it.
            CurrentLanguage = Qualify(generatedNamespace, "LanguageStrings") + ".ResolveLanguage(" + CultureInfoType + ".CurrentUICulture)";
        }

        public SourceProductionContext SourceContext { get; }

        public Dictionary<string, Dictionary<string, string>> TranslationsByKey { get; }

        public Dictionary<string, string> FilePathsByLanguage { get; }

        public string FallbackLanguage { get; }

        public string TranslationsType { get; }

        public string GroupsType { get; }

        public string CurrentLanguage { get; }
    }

    private sealed class LeafInfo
    {
        public LeafInfo(
            TranslationNode node,
            string methodName,
            bool isPlural,
            string[] parameters,
            Dictionary<string, string> translations)
        {
            Node = node;
            MethodName = methodName;
            IsPlural = isPlural;
            Parameters = parameters;
            Identifiers = TranslationGeneratorUtilities.GetParameterIdentifiers(parameters);
            Translations = translations;
        }

        public TranslationNode Node { get; }

        /// <summary>Name of the implementation method in <c>LanguageTranslations</c>.</summary>
        public string MethodName { get; }

        public bool IsPlural { get; }

        public string[] Parameters { get; }

        public string[] Identifiers { get; }

        public Dictionary<string, string> Translations { get; }

        public string KeyPath => Node.KeyPath!;

        public string FallbackValue => Node.Value!;

        public string MemberName => TranslationGeneratorUtilities.SanitizeName(Node.Name);

        public string GetParameterType(int index) => IsPlural && index == 0 ? "int" : "object";
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
    /// Names and analysis shared by the generated files. Implementation methods and group struct types get
    /// names derived from the key path (sanitized, joined with '_'). Collisions get a numeric suffix, and a
    /// group type also avoids the names of its own members.
    /// </summary>
    private sealed class Plan
    {
        private readonly Dictionary<TranslationNode, LeafInfo> _leavesByNode = new();
        private readonly Dictionary<TranslationNode, GroupInfo> _groupsByNode = new();
        private readonly HashSet<string> _usedMethodNames = new(StringComparer.Ordinal) { TranslationsClass };
        private readonly HashSet<string> _usedGroupNames = new(StringComparer.Ordinal) { GroupsClass };

        public List<LeafInfo> Leaves { get; } = new();

        public List<GroupInfo> Groups { get; } = new();

        public LeafInfo GetLeaf(TranslationNode node) => _leavesByNode[node];

        public bool TryGetLeaf(TranslationNode node, out LeafInfo leaf) => _leavesByNode.TryGetValue(node, out leaf!);

        public GroupInfo GetGroup(TranslationNode node) => _groupsByNode[node];

        public static Plan Create(TranslationNode root, GenerationContext context)
        {
            var plan = new Plan();
            plan.Visit(root, string.Empty, context);
            return plan;
        }

        private void Visit(TranslationNode node, string path, GenerationContext context)
        {
            foreach (var child in node.Children.Values)
            {
                var childPath = path.Length == 0 ? child.Name : path + "." + child.Name;
                if (child.Children.Count > 0)
                {
                    var group = new GroupInfo(child, AllocateGroupName(child, childPath));
                    _groupsByNode.Add(child, group);
                    Groups.Add(group);
                    Visit(child, childPath, context);
                    continue;
                }

                if (child.KeyPath is null || child.Value is null || !context.TranslationsByKey.TryGetValue(child.KeyPath, out var translations))
                {
                    continue;
                }

                var leaf = CreateLeaf(child, translations);
                _leavesByNode.Add(child, leaf);
                Leaves.Add(leaf);
            }
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

        private LeafInfo CreateLeaf(TranslationNode node, Dictionary<string, string> translations)
        {
            var baseName = TranslationGeneratorUtilities.SanitizeName(node.KeyPath!.Replace('.', '_'));
            var methodName = baseName;
            for (var suffix = 2; !_usedMethodNames.Add(methodName); suffix++)
            {
                methodName = baseName + suffix;
            }

            var fallbackValue = node.Value!;

            // A key is plural when any language uses pipe-separated variants, so one language
            // can pluralize while the others (including the fallback) use a single form.
            var fallbackIsPlural = TranslationGeneratorUtilities.IsPluralValue(fallbackValue);
            var isPlural = fallbackIsPlural || translations.Values.Any(TranslationGeneratorUtilities.IsPluralValue);
            var parameters = TranslationGeneratorUtilities.GetParameterNames(fallbackValue, fallbackIsPlural);
            if (isPlural && !fallbackIsPlural)
            {
                // The fallback has no plural forms to infer the count parameter from, so it is always named "count".
                parameters = new[] { TranslationGeneratorUtilities.DefaultCountParameter }
                    .Concat(parameters.Where(static parameter => parameter != TranslationGeneratorUtilities.DefaultCountParameter))
                    .ToArray();
            }

            return new LeafInfo(node, methodName, isPlural, parameters, translations);
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

    private static void WriteFields(IndentedWriter writer)
    {
        writer.Append("private readonly string? ").Append(LanguageField).AppendLine(";");
        writer.Append("private readonly ").Append(CultureInfoType).Append("? ").Append(CultureField).AppendLine(";");
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
        foreach (var child in node.Children.Values)
        {
            if (child.Children.Count > 0)
            {
                var group = plan.GetGroup(child);
                var groupType = context.GroupsType + "." + group.TypeName;
                writer.AppendLine();
                writer.Append("public ").Append(groupType).Append(' ').Append(TranslationGeneratorUtilities.SanitizeName(child.Name))
                    .Append(" => new ").Append(groupType).Append('(').Append(LanguageField).Append(", ").Append(CultureField).AppendLine(");");
                continue;
            }

            if (!plan.TryGetLeaf(child, out var leaf))
            {
                continue;
            }

            writer.AppendLine();
            WriteLeafSignature(writer, leaf, "public string ");
            writer.Indent();
            WriteTranslationCall(writer, context, leaf, LanguageField, CultureField + " ?? " + CultureInfoType + ".InvariantCulture");
            writer.Outdent();
        }
    }

    private static void WriteLeafSignature(IndentedWriter writer, LeafInfo leaf, string prefix)
    {
        writer.Append("/// ").AppendLine(TranslationGeneratorUtilities.EscapeXmlDoc(leaf.FallbackValue));
        writer.Append(prefix).Append(leaf.MemberName);
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

    private static void WriteTranslationCall(
        IndentedWriter writer,
        GenerationContext context,
        LeafInfo leaf,
        string languageExpression,
        string providerExpression)
    {
        writer.Append(context.TranslationsType).Append('.').Append(leaf.MethodName)
            .Append('(').Append(languageExpression).Append(", ").Append(providerExpression);
        foreach (var identifier in leaf.Identifiers)
        {
            writer.Append(", ").Append(identifier);
        }

        writer.AppendLine(");");
    }

    // LanguageTranslations: one implementation per key, shared by Language and LocalizedLanguage.
    private static void WriteTranslationMethod(IndentedWriter writer, GenerationContext context, LeafInfo leaf)
    {
        var fallbackLanguage = context.FallbackLanguage;
        var keyPath = leaf.KeyPath;
        var parameters = leaf.Parameters;
        var identifiers = leaf.Identifiers;
        var isPlural = leaf.IsPlural;
        var fallbackValue = leaf.FallbackValue;

        writer.Append("public static string ").Append(leaf.MethodName)
            .Append("(string? ").Append(TranslationGeneratorUtilities.LanguageParameter)
            .Append(", global::System.IFormatProvider? ").Append(TranslationGeneratorUtilities.FormatProviderParameter);
        for (var index = 0; index < identifiers.Length; index++)
        {
            writer.Append(", ").Append(leaf.GetParameterType(index)).Append(' ').Append(identifiers[index]);
        }

        writer.AppendLine(") =>");
        writer.Indent();

        var otherTranslations = leaf.Translations
            .Where(pair => !string.Equals(pair.Key, fallbackLanguage, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal);

        if (isPlural)
        {
            // vue-i18n uses the absolute value of the count to pick the variant.
            var count = identifiers[0];
            writer.Append('(').Append(count).Append(" < 0 ? -").Append(count).Append(" : ").Append(count).Append(", ")
                .Append(TranslationGeneratorUtilities.LanguageParameter).AppendLine(") switch");
            writer.OpenBlock();

            foreach (var translation in otherTranslations)
            {
                WritePluralCases(writer, context, keyPath, translation.Key, translation.Value, parameters, isFallback: false);
            }

            WritePluralCases(writer, context, keyPath, fallbackLanguage, fallbackValue, parameters, isFallback: true);
        }
        else
        {
            writer.Append(TranslationGeneratorUtilities.LanguageParameter).AppendLine(" switch");
            writer.OpenBlock();

            foreach (var translation in otherTranslations)
            {
                if (!TryCreateExpression(context, keyPath, translation.Key, translation.Value, parameters, out var expression))
                {
                    continue;
                }

                writer.Append(TranslationGeneratorUtilities.ToCSharpStringLiteral(translation.Key))
                    .Append(" => ").Append(expression).AppendLine(",");
            }

            TranslationGeneratorUtilities.TryCreateFormatExpression(fallbackValue, parameters, out var fallbackExpression);
            writer.Append("_ => ").Append(fallbackExpression).AppendLine(",");
        }

        writer.CloseBlock(";");
        writer.Outdent();
        writer.AppendLine();
    }

    private static void WritePluralCases(
        IndentedWriter writer,
        GenerationContext context,
        string keyPath,
        string language,
        string value,
        string[] parameters,
        bool isFallback)
    {
        // Every variant is validated before any arm is written, so a language with one invalid
        // variant falls back as a whole instead of mixing its own and the fallback's variants.
        var parts = TranslationGeneratorUtilities.SplitPluralParts(value);
        var expressions = new string[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!TryCreateExpression(context, keyPath, language, parts[index], parameters, out expressions[index]))
            {
                return;
            }
        }

        var languagePattern = isFallback ? "_" : TranslationGeneratorUtilities.ToCSharpStringLiteral(language);
        for (var index = 0; index < expressions.Length; index++)
        {
            writer.Append('(');
            if (index == expressions.Length - 1)
            {
                writer.Append('_');
            }
            else if (expressions.Length == 2)
            {
                // vue-i18n's rule for two forms: a count of 1 uses the first form, anything else the second.
                writer.Append('1');
            }
            else
            {
                // Three or more forms: the count selects the form, and the last form is used for larger counts.
                writer.Append(index.ToString());
            }

            writer.Append(", ").Append(languagePattern).Append(") => ").Append(expressions[index]).AppendLine(",");
        }
    }

    private static bool TryCreateExpression(
        GenerationContext context,
        string keyPath,
        string language,
        string value,
        string[] parameters,
        out string expression)
    {
        if (TranslationGeneratorUtilities.TryCreateFormatExpression(value, parameters, out expression))
        {
            return true;
        }

        var location = context.FilePathsByLanguage.TryGetValue(language, out var filePath)
            ? new LocationInfo(filePath).ToLocation()
            : Location.None;

        var unsupportedPlaceholders = TranslationGeneratorUtilities.GetUnsupportedPlaceholders(value, parameters);
        context.SourceContext.ReportDiagnostic(Diagnostic.Create(
            TranslationGeneratorDiagnostics.PlaceholderMismatch,
            location,
            $"Translation key '{keyPath}' in language '{language}' uses placeholders [{string.Join(", ", unsupportedPlaceholders)}] that are not present in the fallback language. The fallback translation will be used instead."));
        return false;
    }
}
