# How to list languages, keys and all strings

`LanguageStrings` has members that describe the translation files.

## List the languages

```csharp
IReadOnlyList<string> languages = LanguageStrings.Languages; // ["en", "no"]
string fallback = LanguageStrings.FallbackLanguage;          // "en"
```

Languages are the lowercased file names, in ordinal order. Use them for a language picker, or to check that a language a user asks for exists.

## List the keys

```csharp
IReadOnlyList<string> keys = LanguageStrings.Keys; // ["files", "greeting", "menu.cancel", "menu.save"]
```

These are the key paths of the fallback file, in ordinal order. Use them to export or audit translations.

## Get all strings of a language

By file language (case-insensitive):

```csharp
var norwegian = LanguageStrings.GetAllStrings("no");
foreach (var (key, value) in norwegian)
{
    Console.WriteLine($"{key} = {value}");
}
```

An unknown language returns an empty list.

By culture, for example to send a user's locale to a frontend:

```csharp
var forUser = LanguageStrings.GetAllStrings(new CultureInfo("nb-NO"));
var onlyOwn = LanguageStrings.GetAllStrings(new CultureInfo("nb-NO"), includeFallback: false);
```

With `includeFallback` (the default), keys the language lacks are added with the fallback text, after the language's own pairs. Without it, only the language's own pairs are returned.

The values are the stored texts with linked messages resolved, the same as `GetStringOrNull` returns. Literals and format specifiers are kept as written.

## Cache the results if you call them often

`Languages`, `Keys` and `GetAllStrings` create a new array or list on every call. This is on purpose, so that hot reload keeps working (see [Why the generated code is shaped for hot reload](../explanation/hot-reload.md)). If you call them in a hot path, store the result.
