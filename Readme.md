# BccCode.I18N.SourceGen

A Roslyn incremental source generator that reads JSON translation files (additional files) and generates strongly-typed C# classes for internationalization (i18n).

**You must build this project to see the result (generated code) in the IDE.**

## How It Works

Add your translation files as `AdditionalFiles` in your `.csproj`. The generator uses `no.json` as the base/default language (English fallback) and any other `.json` files (e.g. `en.json`) as additional locales.

The JSON files can contain nested objects (mapped to nested static classes) and string values (mapped to properties or constants). Strings support:

- **Interpolation**: `{paramName}` placeholders become method parameters (e.g. `"Hello {name}"` → `Language.hello(object name)`)
- **Pluralization**: pipe-separated variants `"no items | one item | {count} items"` — the first parameter becomes `int count`, and the correct variant is selected at runtime

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

A static class that holds all translations in a `Dictionary<string, Dictionary<string, string>>` (keyed by two-letter ISO language code and then by dot-separated key path). Exposes two lookup methods:

```csharp
string value = LanguageStrings.GetString("message.hello");      // returns key if not found
string? value = LanguageStrings.GetStringOrNull("message.hello"); // returns null if not found
```

## How To?

### How to add translation files

In your `.csproj`, include JSON files as `AdditionalFiles`:

```xml
<ItemGroup>
  <AdditionalFiles Include="no.json" />
  <AdditionalFiles Include="en.json" />
</ItemGroup>
```

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

With that configuration, the generator emits `MyApp.Localization.Language`, `MyApp.Localization.Language`, and `MyApp.Localization.LanguageStrings` instead of generating them in the global namespace.

> **Note for project references:** When referencing the generator as a `ProjectReference` (rather than via NuGet), import the props file manually so `FallbackLanguage` and `GeneratedNamespace` are visible to the analyzer:
>
> ```xml
> <Import Project="..\BccCode.I18N.SourceGen\build\BccCode.I18N.SourceGen.props" />
> ```
>
> When consumed via NuGet, this import happens automatically.

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

dotnet pack .\BccCode.I18N.SourceGen\BccCode.I18N.SourceGen.csproj -c Release -o .\artifacts

Remove-Item .\packages -Recurse -Force -ErrorAction SilentlyContinue
dotnet restore .\BccCode.I18N.SourceGen.NuGetIntegrationTests\BccCode.I18N.SourceGen.NuGetIntegrationTests.csproj --packages .\packages --configfile .\nuget.integration-tests.config --force
dotnet build .\BccCode.I18N.SourceGen.NuGetIntegrationTests\BccCode.I18N.SourceGen.NuGetIntegrationTests.csproj -c Release --packages .\packages --no-restore
dotnet test .\BccCode.I18N.SourceGen.NuGetIntegrationTests\BccCode.I18N.SourceGen.NuGetIntegrationTests.csproj -c Release --no-build --no-restore
```

This mirrors the source-generator publishing flow described by Andrew Lock: first verify compiler integration through an analyzer-style project reference, then verify the actual packed analyzer from a local NuGet source without polluting the machine-wide NuGet cache.
Clearing the local `packages` folder before restore is important when reusing the same package version locally, otherwise NuGet can keep an older packed analyzer and stale build props.
