# Reference: MSBuild properties and item metadata

The generator reads four MSBuild properties and one item metadata value. The NuGet package's `build/BccCode.I18N.SourceGen.props` (also shipped as `buildTransitive/`) makes them visible to the compiler with `CompilerVisibleProperty` and `CompilerVisibleItemMetadata`.

## Properties

| Property | Default | Description |
| --- | --- | --- |
| `FallbackLanguage` | `no` (set by the props file). `en` if the props file is not imported. | The language used when no translation file matches a culture, and for keys that a language does not contain. Must equal the name of one translation file without `.json`. Compared case-insensitively; leading and trailing whitespace is ignored. |
| `GeneratedNamespace` | `$(RootNamespace)`. See below. | Namespace of all generated types. |
| `I18NLanguageAliases` | Empty | Extra language aliases as `culture=language` pairs separated by `;`, for example `nb=no;nn=no;pt-pt=pt`. |
| `RootNamespace` | Set by the .NET SDK. | Standard MSBuild property. Used as the namespace when `GeneratedNamespace` is empty. |

### `GeneratedNamespace` default

When `GeneratedNamespace` is empty or whitespace, the generator uses `RootNamespace`. The generator reads both values at compile time, so it sees the final values from the project file, wherever the props file is imported.

- `RootNamespace` is the project name unless the project file sets it. The SDK only replaces spaces in the project name, so the generator replaces every character that is not valid in a C# identifier with `_`, adds a leading `_` to a segment that starts with a digit or another character that cannot start an identifier, and escapes a segment that is a C# keyword with `@`. A project named `My-App` gets `My_App`.
- `GeneratedNamespace` is used as written.
- If both are empty, the types are in the global namespace.

Versions up to 1.1.0 computed the default in the props file instead. NuGet imports that file before the project file body, so with a `PackageReference` the types were in the global namespace.

### `I18NLanguageAliases` format

- Entries are separated by `;`. Each entry is `source=target`.
- Names are trimmed and compared case-insensitively. Empty entries are ignored.
- If the same source appears twice, the last entry wins.
- An entry without `=`, or with an empty side, is malformed and reported as [I18N009](diagnostics.md#i18n009).
- An entry whose target has no translation file is reported as [I18N009](diagnostics.md#i18n009) and has no effect.
- An entry replaces the built-in alias with the same source.

Built-in aliases, used when the project does not override them:

| Source | Target |
| --- | --- |
| `nb` | `no` |
| `nn` | `no` |
| `no` | `nb` |

An alias (built-in or configured) is only used when its target has a translation file and its source does not. Built-in aliases whose target has no file are ignored without a diagnostic.

## Item metadata

| Item | Metadata | Values | Description |
| --- | --- | --- | --- |
| `AdditionalFiles` | `I18NTranslation` | `true` (case-insensitive) | Marks the file as a translation file. |

Rules for selecting translation files:

1. Only `AdditionalFiles` whose path ends with `.json` (case-insensitive) are considered.
2. If at least one of them has `I18NTranslation="true"`, only the marked files are translation files.
3. If none is marked, every `.json` file in `AdditionalFiles` is a translation file.

The language of a file is its file name without the extension, trimmed and lowercased: `en.json` is `en`, `pt-BR.json` is `pt-br`. The folder is not part of the language.

## Related standard properties

These are not specific to the generator but are useful with it:

| Property | Use |
| --- | --- |
| `EmitCompilerGeneratedFiles` | `true` writes the generated `.g.cs` files to `obj/<configuration>/<tfm>/generated/` so you can read them. |
| `NoWarn`, `WarningsAsErrors` | Change how the I18N diagnostics affect the build. See [Change the severity of a diagnostic](../how-to/change-diagnostic-severity.md). |
| `LangVersion` | The generated code needs C# 8 or later. |

## Example

```xml
<PropertyGroup>
  <FallbackLanguage>en</FallbackLanguage>
  <GeneratedNamespace>MyApp.Localization</GeneratedNamespace>
  <I18NLanguageAliases>nb=no;nn=no</I18NLanguageAliases>
</PropertyGroup>

<ItemGroup>
  <AdditionalFiles Include="Translations\*.json" I18NTranslation="true" />
</ItemGroup>
```
