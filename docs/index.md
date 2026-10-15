# BccCode.I18N.SourceGen user manual

`BccCode.I18N.SourceGen` is a Roslyn source generator. It reads JSON translation files (the format vue-i18n uses) and generates strongly typed C# for them, so that texts are checked at compile time and selected by culture at run time.

The manual is organized in four parts, following the [Diátaxis](https://diataxis.fr/) framework.

## Tutorial

Learn the basics by building a small app.

- [Your first translated app](tutorials/getting-started.md): from an empty project to using `Language`, `LanguageKeys` and `LanguageStrings` with two translation files.

## How-to guides

Steps for specific tasks.

**Setup**

- [Add translation files](how-to/add-translation-files.md)
- [Set the fallback language](how-to/set-fallback-language.md)
- [Set the namespace of the generated code](how-to/set-generated-namespace.md)
- [Map cultures to languages with aliases](how-to/configure-language-aliases.md)
- [Reference the generator from source (ProjectReference)](how-to/reference-from-source.md)

**Writing messages**

- [Format numbers and dates in messages](how-to/format-numbers-and-dates.md)
- [Pluralize a message](how-to/pluralize-messages.md)
- [Share translation files with vue-i18n](how-to/share-messages-with-vue-i18n.md)

**Using the generated code**

- [Translate for a specific culture](how-to/use-an-explicit-culture.md)
- [Format a message when you only have the key](how-to/format-a-message-by-key.md)
- [List languages, keys and all strings](how-to/list-languages-and-keys.md)
- [Use the translations through IStringLocalizer](how-to/use-istringlocalizer.md)

**Development and build**

- [Change the severity of a diagnostic](how-to/change-diagnostic-severity.md)
- [Edit translations while the app runs (hot reload)](how-to/use-hot-reload.md)

## Reference

Exact descriptions of every setting, type and rule.

- [MSBuild properties and item metadata](reference/msbuild.md)
- [Generated API](reference/generated-api.md)
- [Message syntax](reference/message-syntax.md)
- [Diagnostics I18N001–I18N010](reference/diagnostics.md)

## Explanation

Background on how and why the generator works as it does.

- [How culture matching and fallback work](explanation/culture-matching-and-fallback.md)
- [Plural selection rules](explanation/plural-selection.md)
- [Why the generated code is shaped for hot reload](explanation/hot-reload.md)
- [Relation to vue-i18n](explanation/vue-i18n.md)
- [Why there is no runtime dependency](explanation/no-runtime-dependency.md)

## Requirements

- A compiler that can load analyzers built against Roslyn 4.14 (`Microsoft.CodeAnalysis.CSharp` 4.14.0). An older compiler skips the generator and reports warning CS8032. The repository builds and tests with the .NET 10 SDK.
- C# 8 or later in the consuming project.

## Examples in this manual

Unless a page shows its own files, examples use the translation files from the tutorial, with `en` as the fallback language:

`en.json`:

```json
{
  "greeting": "Hello, {name}!",
  "menu": {
    "save": "Save",
    "cancel": "Cancel"
  },
  "files": "no files | one file | {count} files"
}
```

`no.json`:

```json
{
  "greeting": "Hei, {name}!",
  "menu": {
    "save": "Lagre",
    "cancel": "Avbryt"
  },
  "files": "ingen filer | én fil | {count} filer"
}
```
