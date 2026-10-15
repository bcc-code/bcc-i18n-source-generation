# Tutorial: your first translated app

In this tutorial you build a small console app that prints text in English and Norwegian. On the way you use the three classes the generator creates: `Language`, `LanguageKeys` and `LanguageStrings`.

It takes about 15 minutes. You need a recent .NET SDK (see [Requirements](../index.md#requirements)) and a terminal. The steps were written with the .NET 10 SDK.

## 1. Create the project

```bash
dotnet new console -n HelloI18n
```

```bash
cd HelloI18n
```

```bash
dotnet add package BccCode.I18N.SourceGen
```

The package contains a source generator. It runs while your project compiles and adds C# code to it. Nothing from the package is copied to your build output.

## 2. Add two translation files

Create `en.json` in the project folder:

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

Create `no.json` next to it:

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

Each file is one language. The file name without `.json` is the language: `en` and `no`.

## 3. Tell the generator about the files

Open `HelloI18n.csproj` and add the following inside `<Project>`:

```xml
<PropertyGroup>
  <FallbackLanguage>en</FallbackLanguage>
  <GeneratedNamespace>HelloI18n</GeneratedNamespace>
</PropertyGroup>

<ItemGroup>
  <AdditionalFiles Include="en.json" I18NTranslation="true" />
  <AdditionalFiles Include="no.json" I18NTranslation="true" />
</ItemGroup>
```

- `AdditionalFiles` with `I18NTranslation="true"` marks the files as translations.
- `FallbackLanguage` is the language used when no file matches the user's culture. Without this line it would be `no`.
- `GeneratedNamespace` is the namespace of the generated classes.

## 4. Build once

```bash
dotnet build
```

The build creates the classes `Language`, `LanguageKeys` and `LanguageStrings` in the `HelloI18n` namespace. Your IDE shows them in code completion after the first build.

## 5. Use `Language`

Replace the contents of `Program.cs` with:

```csharp
using System.Globalization;
using HelloI18n;

CultureInfo.CurrentUICulture = new CultureInfo("en-US");
Console.WriteLine(Language.greeting("Kari"));
Console.WriteLine(Language.menu.save);
Console.WriteLine(Language.files(0));
Console.WriteLine(Language.files(1));
Console.WriteLine(Language.files(5));
```

Run it:

```bash
dotnet run
```

```text
Hello, Kari!
Save
no files
one file
5 files
```

Each key in the JSON became a member:

- `menu.save` has no placeholders, so it is a property.
- `greeting` has the placeholder `{name}`, so it is a method with a `name` parameter.
- `files` has three variants separated by `|`, so it is a method with an `int count` parameter that picks the variant.

A typo such as `Language.menu.sav` is now a compile error.

## 6. Switch to Norwegian

Add these lines to the end of `Program.cs`:

```csharp
CultureInfo.CurrentUICulture = new CultureInfo("nb-NO");
Console.WriteLine(Language.greeting("Kari"));
Console.WriteLine(Language.files(5));
```

Run it again. The last two lines are:

```text
Hei, Kari!
5 filer
```

There is no `nb.json`, but the generator has a built-in rule that Norwegian Bokmål (`nb`) uses the `no` file. [How culture matching works](../explanation/culture-matching-and-fallback.md) explains the details.

## 7. Use `LanguageKeys`

Sometimes you need the key itself, for example to send it to a frontend that translates it. Add:

```csharp
Console.WriteLine(LanguageKeys.menu.cancel);
```

This prints `menu.cancel`. The keys are `const string` fields, so you can use them in attributes and `switch` statements.

## 8. Use `LanguageStrings`

When the key is only known at run time, look it up with `LanguageStrings`. Add:

```csharp
string key = "menu.cancel";
Console.WriteLine(LanguageStrings.GetString(key));
Console.WriteLine(LanguageStrings.GetString("menu.unknown"));
```

With `nb-NO` as the UI culture this prints:

```text
Avbryt
menu.unknown
```

`GetString` returns the key itself when no language has it.

## 9. See what happens with a missing translation

Remove the line `"cancel": "Avbryt"` from `no.json` (and the comma after `"Lagre"`), then build:

```bash
dotnet build
```

The build shows a warning:

```text
warning I18N007: Language 'no' is missing 1 key(s) present in fallback language 'en': menu.cancel
```

Run the app. `Language.menu.cancel` and `LanguageStrings.GetString("menu.cancel")` now return `Cancel` for Norwegian users, because a key that is missing in a language uses the fallback language's text. Put the line back when you are done.

## What you built

- Two translation files that the generator turned into typed C#.
- Calls to `Language` that are checked at compile time.
- Key constants from `LanguageKeys` and run-time lookups with `LanguageStrings`.

## Next steps

- [Use an explicit culture](../how-to/use-an-explicit-culture.md) instead of changing `CurrentUICulture`.
- [Format numbers and dates](../how-to/format-numbers-and-dates.md) in messages.
- [Share translation files with vue-i18n](../how-to/share-messages-with-vue-i18n.md).
- Browse the [reference for the generated API](../reference/generated-api.md).
