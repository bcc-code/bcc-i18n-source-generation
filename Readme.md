# BccCode.I18N.SourceGen

A Roslyn incremental source generator that reads JSON translation files (additional files) and generates strongly-typed C# classes for internationalization (i18n).

**You must build this project to see the result (generated code) in the IDE.**

## How It Works

Add your translation files as `AdditionalFiles` in your `.csproj`. Each file is one language, named by its file name (e.g. `en.json`, `no.json`). The fallback language is English (`en`) unless `FallbackLanguage` says otherwise (see below).

The JSON files can contain nested objects (mapped to nested static classes) and string values (mapped to properties or constants). Strings support:

- **Interpolation**: `{paramName}` placeholders become method parameters (e.g. `"Hello {name}"` → `Language.hello(object name)`)
- **Pluralization**: pipe-separated variants `"no items | one item | {count} items"` — the first parameter becomes `int count`, and the correct variant is selected at runtime
  - A key is pluralized when *any* language uses pipe-separated variants. Languages that use a single form for that key return it for every count. When the fallback language has no plural variants for the key, the count parameter is always named `count` (use `{count}` in the other languages).

## Generated Classes

The generator produces three generated classes from the same JSON translation pipeline:

### `Language` (I18NLanguageGenerator)

Generated file: `Language.g.cs`

A strongly-typed static class that mirrors the structure of your JSON files. Each key becomes a `static string` property or method that returns the correct translation for `CultureInfo.CurrentUICulture` at runtime, falling back to English (`en`) if the current culture is not available.

Example — given `en.json`:

```json
{
  "message": { "hello": "hello world" },
  "plural": { "car": "car | cars" }
}
```

Usage:

```csharp
string greeting = Language.message.hello;         // plain string
string cars = Language.plural.car(3);             // pluralized: "cars"
```

### `LanguageKeys` (I18NLanguageKeyGenerator)

Generated file: `LanguageKeys.g.cs`

A static class of `const string` keys that mirror the JSON structure. Useful for returning keys to the frontend in an API response.

Example:

```csharp
string key = LanguageKeys.message.hello; // == "message.hello"
```

### `LanguageStrings` (I18NLanguageDictGenerator)

Generated file: `LanguageStrings.g.cs`

A static class that looks up translations by dot-separated key path for the two-letter ISO language code of `CultureInfo.CurrentUICulture`, using the fallback language when there is no file for that language. The lookups are generated as `switch` expressions, so hot reload can apply changes to translation files without restarting the app. Exposes two lookup methods:

```csharp
string value = LanguageStrings.GetString("message.hello");      // returns key if not found
string? value = LanguageStrings.GetStringOrNull("message.hello"); // returns null if not found
```

## How To?

### How to add translation files

In your `.csproj`, include JSON files as `AdditionalFiles`:

```xml
<ItemGroup>
  <AdditionalFiles Include="no.json" I18NTranslation="true" />
  <AdditionalFiles Include="en.json" I18NTranslation="true" />
</ItemGroup>
```

Mark translation files with `I18NTranslation="true"`. When at least one `AdditionalFiles` item is marked, only marked `.json` files are read as translations. Other JSON files added as `AdditionalFiles` (for example configuration for other analyzers) are ignored.

If no file is marked, every `.json` file in `AdditionalFiles` is treated as a translation file, as in earlier versions. Marking your files is recommended so that unrelated JSON files are not parsed as translations.

The NuGet package registers the `I18NTranslation` metadata automatically. If you use a `ProjectReference` and import `build/BccCode.I18N.SourceGen.props` yourself, that import makes the metadata visible to the generator.

### How to configure the fallback language

By default the fallback language is English (`en`). You can change this by setting `FallbackLanguage` in your `.csproj`:

```xml
<PropertyGroup>
  <FallbackLanguage>no</FallbackLanguage>
</PropertyGroup>
```

The value must match the filename of one of your translation JSON files (without the `.json` extension). All three generated classes — `Language`, `LanguageKeys`, and `LanguageStrings` — will use this language as the default when no matching translation is found for `CultureInfo.CurrentUICulture`.

### How to configure the generated namespace

The generated classes default to the consuming project's `RootNamespace`. You can override that with `GeneratedNamespace`:

```xml
<PropertyGroup>
  <GeneratedNamespace>MyApp.Localization</GeneratedNamespace>
</PropertyGroup>
```

With that configuration, the generator emits `MyApp.Localization.Language`, `MyApp.Localization.LanguageKeys`, and `MyApp.Localization.LanguageStrings` instead of generating them in the global namespace.

> **Note for project references:** When referencing the generator as a `ProjectReference` (rather than via NuGet), import the props file manually so `FallbackLanguage` and `GeneratedNamespace` are visible to the analyzer:
>
> ```xml
> <Import Project="..\BccCode.I18N.SourceGen\build\BccCode.I18N.SourceGen.props" />
> ```
>
> When consumed via NuGet, this import happens automatically.

## Diagnostics

| ID | Severity | Meaning |
|----|----------|---------|
| I18N001 | Error | A translation file is not valid JSON. |
| I18N002 | Error | A translation file does not have an object as its root. |
| I18N003 | Error | A translation file contains an unsupported value. |
| I18N004 | Error | The fallback language file is missing. |
| I18N005 | Error | More than one file exists for the same language. |
| I18N006 | Warning | Placeholders in a translation do not match the fallback language. |
| I18N007 | Warning | A language file is missing keys that exist in the fallback language. One diagnostic per file lists up to 10 keys. |
| I18N008 | Warning | A language file has keys that do not exist in the fallback language. These keys are not generated in `Language` or `LanguageKeys`. |

Severity can be changed per rule in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.I18N007.severity = none
dotnet_diagnostic.I18N008.severity = error
```

## Packaging and Integration Testing

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
