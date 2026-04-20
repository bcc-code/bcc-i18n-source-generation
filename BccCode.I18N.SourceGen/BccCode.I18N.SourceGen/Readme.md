# BccCode.I18N.SourceGen

A Roslyn incremental source generator that reads JSON translation files (additional files) and generates strongly-typed C# classes for internationalization (i18n).

**You must build this project to see the result (generated code) in the IDE.**

## How It Works

Add your translation files as `AdditionalFiles` in your `.csproj`. The generator uses `no.json` as the base/default language (Norwegian fallback) and any other `.json` files (e.g. `en.json`) as additional locales.

The JSON files can contain nested objects (mapped to nested static classes) and string values (mapped to properties or constants). Strings support:
- **Interpolation**: `{paramName}` placeholders become method parameters (e.g. `"Hello {name}"` → `Language.hello(object name)`)
- **Pluralization**: pipe-separated variants `"no items | one item | {count} items"` — the first parameter becomes `int count`, and the correct variant is selected at runtime

## Generated Classes

Three generators run against your JSON translation files:

### `Language` (I18NLanguageGenerator)
Generated file: `Language.g.cs`

A strongly-typed static class that mirrors the structure of your JSON files. Each key becomes a `static string` property or method that returns the correct translation for `CultureInfo.CurrentUICulture` at runtime, falling back to Norwegian (`no`) if the current culture is not available.

Example — given `no.json`:
```json
{
  "message": { "hello": "Hei verden" },
  "plural": { "car": "bil | biler" }
}
```
Usage:
```csharp
string greeting = Language.message.hello;         // plain string
string cars = Language.plural.car(3);             // pluralized: "biler"
```

### `I18N` (I18NLanguageKeyGenerator)
Generated file: `I18N.g.cs`

A static class of `const string` keys that mirror the JSON structure. Useful for passing keys to `I18NStrings.GetString()` or for use with external i18n frameworks.

Example:
```csharp
string key = I18N.message.hello; // == "message.hello"
```

### `I18NStrings` (I18NLanguageDictGenerator)
Generated file: `I18NStrings.g.cs`

A static class that holds all translations in a `Dictionary<string, Dictionary<string, string>>` (keyed by two-letter ISO language code and then by dot-separated key path). Exposes two lookup methods:

```csharp
string value = I18NStrings.GetString("message.hello");      // returns key if not found
string? value = I18NStrings.GetStringOrNull("message.hello"); // returns null if not found
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