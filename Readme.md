# BccCode.I18N.SourceGen

A Roslyn incremental source generator that reads JSON translation files (the format vue-i18n uses) and generates strongly typed C# for them. Texts are checked at compile time and selected from the current culture at run time.

**[Read the user manual](https://github.com/bcc-code/bcc-i18n-source-generation/blob/main/docs/index.md)**

## Features

- `Language`: a typed member per key, for example `Language.menu.save` or `Language.greeting(name)`.
- `LanguageKeys`: `const string` key paths, for example to send keys to a frontend.
- `LanguageStrings`: lookups, listings and formatting by key at run time.
- `LocalizedLanguage`: the same members as `Language` for an explicit culture.
- Culture matching through parent cultures and language aliases (`nb-NO` uses `no.json`), with per-key fallback to a fallback language.
- vue-i18n message syntax: placeholders, plural forms, linked messages and literals, plus .NET format specifiers such as `{date:d}`.
- Optional `IStringLocalizer` implementation when the project references `Microsoft.Extensions.Localization`.
- Build diagnostics for invalid files, missing or extra keys and placeholder mismatches.
- Generated code is shaped so that hot reload applies translation edits.

## Quick start

```bash
dotnet add package BccCode.I18N.SourceGen
```

Add one JSON file per language, named after the language (`en.json`, `no.json`):

```json
{
  "greeting": "Hello, {name}!",
  "menu": { "save": "Save" },
  "files": "no files | one file | {count} files"
}
```

Mark the files and choose the fallback language and namespace in the project file:

```xml
<PropertyGroup>
  <FallbackLanguage>en</FallbackLanguage>
  <GeneratedNamespace>MyApp.Localization</GeneratedNamespace>
</PropertyGroup>

<ItemGroup>
  <AdditionalFiles Include="en.json" I18NTranslation="true" />
  <AdditionalFiles Include="no.json" I18NTranslation="true" />
</ItemGroup>
```

Build once, then use the generated classes:

```csharp
using MyApp.Localization;

string save = Language.menu.save;               // "Save", or "Lagre" for a Norwegian UI culture
string greeting = Language.greeting("Kari");    // "Hello, Kari!"
string files = Language.files(3);               // "3 files"
string key = LanguageKeys.menu.save;            // "menu.save"
string byKey = LanguageStrings.GetString(key);  // "Save"
```

The fallback language defaults to `no` when `FallbackLanguage` is not set. Without `GeneratedNamespace`, the classes are generated in the global namespace.

## Documentation

- [Tutorial: your first translated app](https://github.com/bcc-code/bcc-i18n-source-generation/blob/main/docs/tutorials/getting-started.md)
- [How-to guides](https://github.com/bcc-code/bcc-i18n-source-generation/blob/main/docs/index.md#how-to-guides): aliases, explicit cultures, formatting, plurals, vue-i18n, IStringLocalizer, diagnostics, hot reload, ProjectReference
- Reference: [MSBuild properties](https://github.com/bcc-code/bcc-i18n-source-generation/blob/main/docs/reference/msbuild.md), [generated API](https://github.com/bcc-code/bcc-i18n-source-generation/blob/main/docs/reference/generated-api.md), [message syntax](https://github.com/bcc-code/bcc-i18n-source-generation/blob/main/docs/reference/message-syntax.md), [diagnostics](https://github.com/bcc-code/bcc-i18n-source-generation/blob/main/docs/reference/diagnostics.md)
- Explanation: [culture matching and fallback](https://github.com/bcc-code/bcc-i18n-source-generation/blob/main/docs/explanation/culture-matching-and-fallback.md), [plural rules](https://github.com/bcc-code/bcc-i18n-source-generation/blob/main/docs/explanation/plural-selection.md), [hot reload](https://github.com/bcc-code/bcc-i18n-source-generation/blob/main/docs/explanation/hot-reload.md), [vue-i18n](https://github.com/bcc-code/bcc-i18n-source-generation/blob/main/docs/explanation/vue-i18n.md), [no runtime dependency](https://github.com/bcc-code/bcc-i18n-source-generation/blob/main/docs/explanation/no-runtime-dependency.md)

## Development

### Packaging and integration testing

The NuGet package is built as an analyzer package:

- `BccCode.I18N.SourceGen.dll` is packed under `analyzers/dotnet/cs`
- normal `lib/` output is disabled so consumers do not reference the generator assembly directly
- `build/BccCode.I18N.SourceGen.props` is included so `FallbackLanguage` remains compiler-visible for package consumers

The repository contains two integration test projects under `BccCode.I18N.SourceGen/BccCode.I18N.SourceGen`:

- `BccCode.I18N.SourceGen.IntegrationTests` tests consumption through an analyzer-style `ProjectReference`
- `BccCode.I18N.SourceGen.NuGetIntegrationTests` tests the packed `.nupkg` through `PackageReference`

Use the following commands from the nested repository root `BccCode.I18N.SourceGen/BccCode.I18N.SourceGen`:

```powershell
dotnet test .\BccCode.I18N.SourceGen.IntegrationTests\BccCode.I18N.SourceGen.IntegrationTests.csproj

dotnet pack .\BccCode.I18N.SourceGen\BccCode.I18N.SourceGen.csproj -c Release -p:PackageVersion=0.0.0-local -o .\artifacts

Remove-Item .\packages -Recurse -Force -ErrorAction SilentlyContinue
dotnet restore .\BccCode.I18N.SourceGen.NuGetIntegrationTests -p:BccCodeI18NSourceGenPackageVersion=0.0.0-local --packages .\packages
dotnet test .\BccCode.I18N.SourceGen.NuGetIntegrationTests -c Release --no-restore -p:BccCodeI18NSourceGenPackageVersion=0.0.0-local
```

CI runs the same steps (`.github/workflows/test.yml`) on every pull request and push to main.

### Releasing

Push a tag `vX.Y.Z` (or a prerelease such as `vX.Y.Z-beta.1`) on a commit that is on `main`. `.github/workflows/publish-nuget.yml` then runs all tests, packs with that version, tests the packed package, pushes it to nuget.org via trusted publishing and creates a GitHub release with generated notes and the `.nupkg` attached.

This mirrors the source-generator publishing flow described by Andrew Lock: first verify compiler integration through an analyzer-style project reference, then verify the actual packed analyzer from a local NuGet source without polluting the machine-wide NuGet cache.
Clearing the local `packages` folder before restore is important when reusing the same package version locally, otherwise NuGet can keep an older packed analyzer and stale build props.
