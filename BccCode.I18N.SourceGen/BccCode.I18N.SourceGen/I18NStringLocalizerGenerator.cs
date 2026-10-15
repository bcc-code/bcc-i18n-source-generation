using System;
using Microsoft.CodeAnalysis;

namespace BccCode.I18N.SourceGen;

/// <summary>What the compilation offers of Microsoft.Extensions.Localization.Abstractions. Equatable for incremental caching.</summary>
internal readonly struct StringLocalizerSupport : IEquatable<StringLocalizerSupport>
{
    private const string Namespace = "Microsoft.Extensions.Localization.";

    public StringLocalizerSupport(bool isAvailable, bool hasWithCulture)
    {
        IsAvailable = isAvailable;
        HasWithCulture = hasWithCulture;
    }

    public bool IsAvailable { get; }

    /// <summary>IStringLocalizer.WithCulture exists in 2.x and 3.x of the package and was removed in 5.0.</summary>
    public bool HasWithCulture { get; }

    public static StringLocalizerSupport Detect(Compilation compilation)
    {
        var localizer = compilation.GetTypeByMetadataName(Namespace + "IStringLocalizer");
        if (localizer is null ||
            compilation.GetTypeByMetadataName(Namespace + "IStringLocalizerFactory") is null ||
            compilation.GetTypeByMetadataName(Namespace + "LocalizedString") is null)
        {
            return default;
        }

        return new StringLocalizerSupport(true, localizer.GetMembers("WithCulture").Length > 0);
    }

    public bool Equals(StringLocalizerSupport other) => IsAvailable == other.IsAvailable && HasWithCulture == other.HasWithCulture;

    public override bool Equals(object? obj) => obj is StringLocalizerSupport other && Equals(other);

    public override int GetHashCode() => (IsAvailable.GetHashCode() * 397) ^ HasWithCulture.GetHashCode();
}

/// <summary>Generates <c>LanguageStringLocalizer</c> and <c>LanguageStringLocalizerFactory</c> when IStringLocalizer is available.</summary>
internal static class I18NStringLocalizerGenerator
{
    private const string LocalizerClass = "LanguageStringLocalizer";
    private const string FactoryClass = "LanguageStringLocalizerFactory";
    private const string Localization = "global::Microsoft.Extensions.Localization.";
    private const string CultureInfoType = "global::System.Globalization.CultureInfo";

    public static void AddSource(SourceProductionContext context, string generatedNamespace, StringLocalizerSupport support)
    {
        var strings = string.IsNullOrWhiteSpace(generatedNamespace)
            ? "global::LanguageStrings"
            : "global::" + generatedNamespace + ".LanguageStrings";

        var writer = GeneratedFile.Begin(generatedNamespace, LocalizerClass, "public sealed class " + LocalizerClass + " : " + Localization + "IStringLocalizer");
        writer.Append("private readonly ").Append(CultureInfoType).AppendLine("? _culture;");
        writer.AppendLine();
        writer.AppendLine("/// <summary>Uses the current UI culture at the time of each call.</summary>");
        writer.Append("public ").Append(LocalizerClass).AppendLine("()");
        writer.OpenBlock();
        writer.CloseBlock();
        writer.AppendLine();
        writer.AppendLine("/// <summary>Always uses <paramref name=\"culture\"/>.</summary>");
        writer.Append("public ").Append(LocalizerClass).Append('(').Append(CultureInfoType).AppendLine(" culture)");
        writer.OpenBlock();
        writer.AppendLine("_culture = culture;");
        writer.CloseBlock();
        writer.AppendLine();

        writer.Append("public ").Append(Localization).AppendLine("LocalizedString this[string name]");
        writer.OpenBlock();
        writer.AppendLine("get");
        writer.OpenBlock();
        writer.Append("var value = _culture == null ? ").Append(strings).Append(".GetStringOrNull(name) : ").Append(strings)
            .AppendLine(".GetStringOrNull(name, _culture);");
        writer.Append("return new ").Append(Localization).AppendLine("LocalizedString(name, value ?? name, value == null);");
        writer.CloseBlock();
        writer.CloseBlock();
        writer.AppendLine();

        writer.Append("public ").Append(Localization).AppendLine("LocalizedString this[string name, params object[] arguments]");
        writer.OpenBlock();
        writer.AppendLine("get");
        writer.OpenBlock();
        writer.Append("var value = _culture == null ? ").Append(strings).Append(".GetStringOrNull(name) : ").Append(strings)
            .AppendLine(".GetStringOrNull(name, _culture);");
        writer.AppendLine("if (value == null)");
        writer.OpenBlock();
        writer.Append("return new ").Append(Localization).AppendLine("LocalizedString(name, name, true);");
        writer.CloseBlock();
        writer.AppendLine();
        writer.Append("var formatted = _culture == null ? ").Append(strings).Append(".Format(name, arguments) : ").Append(strings)
            .AppendLine(".Format(name, _culture, arguments);");
        writer.Append("return new ").Append(Localization).AppendLine("LocalizedString(name, formatted, false);");
        writer.CloseBlock();
        writer.CloseBlock();
        writer.AppendLine();

        writer.AppendLine("/// <summary>The strings of the language of the culture. <paramref name=\"includeParentCultures\"/> adds fallback language strings the language lacks.</summary>");
        writer.Append("public global::System.Collections.Generic.IEnumerable<").Append(Localization).AppendLine("LocalizedString> GetAllStrings(bool includeParentCultures)");
        writer.OpenBlock();
        writer.Append("var strings = ").Append(strings).Append(".GetAllStrings(_culture ?? ").Append(CultureInfoType)
            .AppendLine(".CurrentUICulture, includeParentCultures);");
        writer.Append("var result = new ").Append(Localization).AppendLine("LocalizedString[strings.Count];");
        writer.AppendLine("for (var index = 0; index < result.Length; index++)");
        writer.OpenBlock();
        writer.Append("result[index] = new ").Append(Localization).AppendLine("LocalizedString(strings[index].Key, strings[index].Value, false);");
        writer.CloseBlock();
        writer.AppendLine("return result;");
        writer.CloseBlock();

        if (support.HasWithCulture)
        {
            writer.AppendLine();
            writer.Append("public ").Append(Localization).Append("IStringLocalizer WithCulture(").Append(CultureInfoType).AppendLine(" culture) =>");
            writer.Indent();
            writer.Append("new ").Append(LocalizerClass).AppendLine("(culture);");
            writer.Outdent();
        }

        GeneratedFile.AddSource(context, writer, generatedNamespace, LocalizerClass);

        var factory = GeneratedFile.Begin(generatedNamespace, FactoryClass, "public sealed class " + FactoryClass + " : " + Localization + "IStringLocalizerFactory");
        factory.AppendLine("/// <summary>All resources share the translation files, so the type is ignored.</summary>");
        factory.Append("public ").Append(Localization).AppendLine("IStringLocalizer Create(global::System.Type resourceSource) =>");
        factory.Indent();
        factory.Append("new ").Append(LocalizerClass).AppendLine("();");
        factory.Outdent();
        factory.AppendLine();
        factory.AppendLine("/// <summary>All resources share the translation files, so the names are ignored.</summary>");
        factory.Append("public ").Append(Localization).AppendLine("IStringLocalizer Create(string baseName, string location) =>");
        factory.Indent();
        factory.Append("new ").Append(LocalizerClass).AppendLine("();");
        factory.Outdent();
        GeneratedFile.AddSource(context, factory, generatedNamespace, FactoryClass);
    }
}
