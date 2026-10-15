# BccCode.I18N.SourceGen

A Roslyn incremental source generator that reads JSON translation files (additional files) and generates strongly-typed C# classes for internationalization (i18n).

**You must build this project to see the result (generated code) in the IDE.**

## How It Works

Add your translation files as `AdditionalFiles` in your `.csproj`. Each file is one language, named by its file name (e.g. `en.json`, `no.json`). The fallback language is `no` unless `FallbackLanguage` says otherwise. The NuGet package's `build/BccCode.I18N.SourceGen.props` sets `no` when the property is unset; the generator's own default of `en` only applies if the property is not visible to it at all (see below). The language used at runtime is matched from the current culture (see [Language matching](#language-matching)).

The JSON files can contain nested objects (mapped to nested static classes) and string values (mapped to properties or constants). Strings support:

- **Interpolation**: `{paramName}` placeholders become method parameters (e.g. `"Hello {name}"` → `Language.hello(object name)`)
- **Pluralization**: pipe-separated variants `"no items | one item | {count} items"` — the first parameter becomes `int count`, and the variant is selected at runtime with the same rules as vue-i18n. The count is first turned into its absolute value. With two variants, a count of 1 selects the first and every other count the second (`"car | cars"`: 1 is `car`, 0 and 2 are `cars`). With three variants, 0 selects the first, 1 the second and larger counts the third. With four or more, the count is the index and larger counts use the last variant. Each language uses the rule for its own number of variants.
  - A key is pluralized when *any* language uses pipe-separated variants. Languages that use a single form for that key return it for every count. When the fallback language has no plural variants for the key, the count parameter is always named `count` (use `{count}` in the other languages).

## Generated Classes

The generator produces three generated classes from the same JSON translation pipeline:

### `Language` (I18NLanguageGenerator)

Generated file: `Language.g.cs`. The translations themselves live in the internal `LanguageTranslations` class (`LanguageTranslations.g.cs`), which `Language` and `LocalizedLanguage` both call.

A strongly-typed static class that mirrors the structure of your JSON files. Each key becomes a `static string` property or method that returns the correct translation for `CultureInfo.CurrentUICulture` at runtime, falling back to the fallback language (`no` by default, see below) if no translation file matches the current culture. A key that is missing in the matched language also uses the fallback language's text.

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

A static class that looks up translations by dot-separated key path for the language matched from `CultureInfo.CurrentUICulture` (see [Language matching](#language-matching)). A key that is missing in that language is looked up in the fallback language, the same way as in `Language`. The lookups are generated as `switch` expressions, so hot reload can apply changes to translation files without restarting the app. Exposes two lookup methods:

```csharp
string value = LanguageStrings.GetString("message.hello");      // returns key if not found
string? value = LanguageStrings.GetStringOrNull("message.hello"); // returns null if not found
```

`GetStringOrNull` returns `null` only when neither the matched language nor the fallback language has the key. `GetString` returns the key in that case.

It also exposes the language matching as a method:

```csharp
string language = LanguageStrings.ResolveLanguage(new CultureInfo("nb-NO")); // "no" when no.json exists
```

## Explicit culture

`Language` and the one-argument `LanguageStrings` methods read `CultureInfo.CurrentUICulture`. For background jobs, emails in a user's language and tests, pass a culture instead of changing the thread culture:

```csharp
var language = new LocalizedLanguage(new CultureInfo("nb-NO"));

string greeting = language.message.hello;     // same members as Language, but instance members
string cars = language.plural.car(3);

string value = LanguageStrings.GetString("message.hello", new CultureInfo("nb-NO"));
string? maybe = LanguageStrings.GetStringOrNull("message.hello", new CultureInfo("nb-NO"));
```

`LocalizedLanguage` is a readonly struct in the generated namespace. Its members have the same names, parameters and return types as in `Language`. It resolves the language once in the constructor with `LanguageStrings.ResolveLanguage(culture)`. Groups are returned as small structs in the generated `LocalizedLanguageGroups` class (hidden from completion; the type names are derived from the key path and given a numeric suffix on a collision, so they never clash with your keys). `default(LocalizedLanguage)` uses the fallback language and invariant formatting.

A struct member cannot have the name of its struct. When a root key is named `LocalizedLanguage`, the instance member in `LocalizedLanguage` gets a `_` suffix (`LocalizedLanguage_`, and a number if that name is taken too). `Language` is not affected. The same rule applies to a group member that has the name of its group struct.

Which culture is used for what:

| API | Language selection | Number/date formatting |
| --- | --- | --- |
| `Language.*` | `CultureInfo.CurrentUICulture` | `CultureInfo.CurrentCulture` |
| `new LocalizedLanguage(culture).*` | `culture` | `culture` |
| `LanguageStrings.GetString(key)` | `CultureInfo.CurrentUICulture` | none (raw stored value) |
| `LanguageStrings.GetString(key, culture)` | `culture` | none (raw stored value) |

The `LanguageStrings` overloads use the same per-key fallback to the fallback language as the one-argument methods.

## Formatting

Placeholders are formatted with `string.Format`, so values that implement `IFormattable` (numbers, `DateTime`, `DateOnly`, ...) are formatted with the culture shown in the table above. A placeholder can carry a .NET format specifier after a colon:

```json
{ "due": "Due {date:d}", "price": "{amount:N2} kr", "items": "{count:N0} item | {count:N0} items" }
```

```csharp
Language.due(DateTime.Today);       // "Due 1/31/2024" or "Due 31.01.2024", depending on CurrentCulture
Language.price(1234.5m);            // "1,234.50 kr" or "1 234,50 kr"
```

The parameter name is the part before the colon. The same name can appear several times with different formats, and the format may not contain `{` or `}`. The placeholder check (I18N006) compares names only, so languages can use different formats for the same name.

Format specifiers are .NET-only. If a frontend such as vue-i18n shares the same JSON files, it shows `{date:d}` literally. `LanguageStrings` returns the stored text unchanged, including format specifiers.

## Listing languages and keys

`LanguageStrings` has members that list what the translation files contain:

```csharp
string fallback = LanguageStrings.FallbackLanguage;                  // "en"
IReadOnlyList<string> languages = LanguageStrings.Languages;         // all file languages, ordinal order
IReadOnlyList<string> keys = LanguageStrings.Keys;                   // key paths of the fallback file, ordinal order

IReadOnlyList<KeyValuePair<string, string>> norwegian = LanguageStrings.GetAllStrings("no");   // language is case-insensitive, empty if unknown
var forUser = LanguageStrings.GetAllStrings(new CultureInfo("nb-NO"));                          // language matched from the culture, plus fallback strings it lacks
var onlyNorwegian = LanguageStrings.GetAllStrings(new CultureInfo("nb-NO"), includeFallback: false);
```

The values are the stored texts with linked messages resolved, as returned by `GetStringOrNull`. These members are method bodies and allocate a new array or list on every call, so cache the result if you call them often. They do not use static fields, so hot reload still applies translation edits.

## Formatting by key (LanguageStrings.Format)

`LanguageStrings.Format` formats a stored message by key when you only have the key, for example from a database or a validation attribute:

```csharp
string text = LanguageStrings.Format("message.hello2", "Kari");                          // language from CurrentUICulture, formatting with CurrentCulture
string text = LanguageStrings.Format("message.hello2", new CultureInfo("nb-NO"), "Kari"); // language and formatting from the culture
```

The arguments are matched to the parameters in the same order as the parameters of the `Language` member for the key (list placeholders in order of first appearance, and the count first for plural messages). It does the same as the `Language` member:

- Placeholders are replaced by `string.Format`-style formatting of the argument with the culture as format provider, and `{name:format}` is honored.
- `{'text'}` becomes `text`.
- A placeholder without a matching argument stays as written, and an unknown key returns the key.
- For a plural message the first argument is the count (any integral or floating point number). The variant is selected with the vue-i18n rules per number of variants (see below). Without a numeric first argument the count is 1.

Use `Language` or `LocalizedLanguage` when you can, because they check the parameters at compile time.

## IStringLocalizer

When the project references `Microsoft.Extensions.Localization.Abstractions` (directly or through `Microsoft.Extensions.Localization`), the generator also emits `LanguageStringLocalizer` and `LanguageStringLocalizerFactory` in the generated namespace. Nothing is generated when the package is not referenced.

- `new LanguageStringLocalizer()` uses the current UI culture at the time of each call, and `new LanguageStringLocalizer(culture)` always uses that culture.
- `localizer["key"]` returns the stored text, with `ResourceNotFound` set (and the key as value) when the key is unknown. `localizer["key", args]` formats the text with `LanguageStrings.Format`.
- `GetAllStrings(includeParentCultures)` returns `LanguageStrings.GetAllStrings(culture, includeParentCultures)`, so `true` adds the fallback language strings the language lacks.
- `WithCulture` is generated only for versions of the abstractions that still declare it (2.x and 3.x).
- `LanguageStringLocalizerFactory` returns a `LanguageStringLocalizer` for every resource type, because the whole application shares one set of translation files.

There is no dependency injection extension method, so register the factory yourself:

```csharp
services.AddSingleton<IStringLocalizerFactory, LanguageStringLocalizerFactory>();
services.AddTransient(typeof(IStringLocalizer<>), typeof(StringLocalizer<>));
```

If you also call `AddLocalization()`, register these after it, because the last registration of `IStringLocalizerFactory` wins. DataAnnotations and validation message localization use the factory, so keys in validation attributes (for example `[Required(ErrorMessage = "validation.required")]`) resolve through the translation files.

## vue-i18n compatibility

Translation files are often shared with a Vue frontend that uses vue-i18n (v9+). The generator understands these parts of the message syntax:

- **Named placeholders**: `{name}` and `{name:format}` (see Formatting).
- **List placeholders**: `{0}`, `{1}`. They become parameters `_0`, `_1`, in order of first appearance in the fallback text. In a plural message the first placeholder in the fallback text is the `int` count (`"{0} item | {0} items"` gives `item(int _0)`).
- **Plural rules**: vue-i18n's default rules for 2, 3 and more variants, using the absolute value of the count (see above).
- **Linked messages**: `@:key`, `@:{'key'}`, and the modifiers `@.lower:key`, `@.upper:key`, `@.capitalize:key` (also with `{'key'}`). A bare key may contain letters, digits, `_`, `-`, `$` and `.`. A trailing `.` ends the sentence and is not part of the key, so `"Press @:common.save."` links `common.save`. `post@bcc.no` is not a link.
- **Literal interpolation**: `{'text'}` is output as `text`. Use it for `{'@'}`, `{'{'}`, `{'}'}` and `{'|'}`. A `|` inside a literal does not separate plural variants, and an `@` inside a literal does not start a link. Like in vue-i18n, a literal can contain the escapes `\'`, `\\`, `\uXXXX` (4 hex digits) and `\UXXXXXX` (6 hex digits), so `{'It\'s'}` is `It's`. A literal with an invalid escape (a surrogate or a value above U+10FFFF is also invalid) or without an end is not a literal and stays plain text.

Linked messages are resolved when the code is generated, so `Language`, `LocalizedLanguage` and `LanguageStrings` all contain the final text. A link is replaced by the target key's value in the same language, or in the fallback language when that language does not have the key. Links inside the target are resolved too, and placeholders in the target become part of the message and its parameters. Modifiers change the text only, not placeholder names. Capitalize changes the first character of the message when it is text. Keys do not change, so `LanguageKeys` is unaffected.

Warning `I18N010` (Invalid linked message) is reported once per language and key when a link points to an unknown key, is part of a cycle (`a` links `b` and `b` links `a`), or points to a plural message (a target containing `|`). The link text is then kept as written.

Literals in `Language` and `LocalizedLanguage` become plain text. `LanguageStrings` returns the stored text, so it contains resolved links but keeps literals such as `{'@'}` as written. This lets you send the text to a frontend that interprets the same syntax.

Not supported: custom pluralization rules (`pluralizationRules` in vue-i18n), custom `@.modifier` names, and HTML in messages (no escaping or rendering is done). Format specifiers such as `{date:d}` are .NET-only, as described under Formatting.

## Language matching

File languages are the lowercased file names without extension (`en.json` is `en`, `pt-BR.json` is `pt-br`). `LanguageStrings.ResolveLanguage(CultureInfo)` picks the file language for a culture, and `Language` and `LanguageStrings` both use it with `CultureInfo.CurrentUICulture`.

The method walks the culture and its parents (`nb-NO`, then `nb`, and so on, until the invariant culture). At each step:

1. If a file language equals the culture name (ignoring case), that language is used.
2. Otherwise, if an alias exists for the culture name and its target is an existing file language, the target is used.

If nothing matches, the fallback language is used. Examples:

- `nb-NO` with `no.json`: no `nb-no` or `nb` file, so the built-in alias `nb=no` selects `no`.
- `en-US` with `en.json` selects `en`.
- `pt-BR` selects `pt-br` when `pt-br.json` exists, otherwise `pt` when `pt.json` exists.
- `zh-TW` is tried as `zh-TW`, `zh-Hant`, `zh`.

A file for a culture name always wins over an alias for the same name. The generated method does not allocate and has no static state, so hot reload still applies translation edits.

### How to configure language aliases

Aliases map a culture name to the file language that should be used for it. Set `I18NLanguageAliases` in your `.csproj` as `culture=language` pairs separated by `;`:

```xml
<PropertyGroup>
  <I18NLanguageAliases>nb=no;nn=no;pt-pt=pt</I18NLanguageAliases>
</PropertyGroup>
```

Names are case-insensitive, whitespace is trimmed and empty entries are ignored. The built-in aliases are `nb=no`, `nn=no` and `no=nb`. They are only used when their target language has a file, and are ignored silently otherwise. An alias in `I18NLanguageAliases` replaces the built-in alias with the same name.

The generator reports warning `I18N009` (Language alias target missing) when an alias you configured points to a language without a translation file, or when an entry is malformed (for example `nb` without `=`).

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

The packaged `build/BccCode.I18N.SourceGen.props` sets `FallbackLanguage` to `no` when it is unset, so for NuGet consumers the default fallback language is `no`. (The generator's internal default `en` only applies if the property is not visible to it at all, for example with a `ProjectReference` that does not import the props file.) You can change the fallback by setting `FallbackLanguage` in your `.csproj`:

```xml
<PropertyGroup>
  <FallbackLanguage>no</FallbackLanguage>
</PropertyGroup>
```

The value must match the filename of one of your translation JSON files (without the `.json` extension). All three generated classes — `Language`, `LanguageKeys`, and `LanguageStrings` — will use this language as the default when no translation file matches `CultureInfo.CurrentUICulture`. `Language` and `LanguageStrings` also use it for a key that the matched language does not contain.

### How to configure the generated namespace

The generated classes default to the consuming project's `RootNamespace`. You can override that with `GeneratedNamespace`:

```xml
<PropertyGroup>
  <GeneratedNamespace>MyApp.Localization</GeneratedNamespace>
</PropertyGroup>
```

With that configuration, the generator emits `MyApp.Localization.Language`, `MyApp.Localization.LanguageKeys`, and `MyApp.Localization.LanguageStrings` instead of generating them in the global namespace.

> **Note for project references:** When referencing the generator as a `ProjectReference` (rather than via NuGet), import the props file manually so `FallbackLanguage`, `GeneratedNamespace` and `I18NLanguageAliases` are visible to the analyzer:
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
| I18N009 | Warning | A language alias in `I18NLanguageAliases` is malformed or points to a language without a translation file. |
| I18N010 | Warning | A linked message (`@:key`) points to an unknown key, is part of a cycle or points to a plural message. |

These diagnostics are reported on the JSON files, so `.editorconfig` sections such as `[*.cs]` or `[*.json]` do not change their severity. Use a `.globalconfig` file in the project folder instead (the SDK picks it up automatically):

```ini
is_global = true
dotnet_diagnostic.I18N007.severity = none
dotnet_diagnostic.I18N008.severity = error
```

Or set it in the project file:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);I18N007</NoWarn>
  <WarningsAsErrors>$(WarningsAsErrors);I18N008</WarningsAsErrors>
</PropertyGroup>
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
