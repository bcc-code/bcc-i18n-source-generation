# How to format a message when you only have the key

Use `LanguageStrings.Format` when the key comes from data, such as a database column or an error code, and you still need placeholders filled in.

```csharp
string key = "greeting"; // "Hello, {name}!"

string text = LanguageStrings.Format(key, "Kari");
// "Hello, Kari!" (language from CurrentUICulture, formatting with CurrentCulture)

string norwegian = LanguageStrings.Format(key, new CultureInfo("nb-NO"), "Kari");
// "Hei, Kari!" (language and formatting from the culture)
```

## Pass the arguments in parameter order

The arguments are matched by position to the parameters of the `Language` member for the same key. For `greeting(object name)` pass one argument; for `files(int count)` pass the count first:

```csharp
LanguageStrings.Format("files", 3);    // "3 files"
LanguageStrings.Format("files", 1);    // "one file"
```

Read the parameter order from the `Language` member (or its IntelliSense) for the key.

## What happens when something does not match

| Situation | Result |
| --- | --- |
| Unknown key | The key is returned. |
| Fewer arguments than placeholders | The remaining placeholders stay as written (`{name}`). |
| Plural message without a numeric first argument | The count is 1. |
| Fractional count, for example `1.5` | Not rounded. With two forms, only exactly 1 selects the first form. |
| The language's text uses placeholders the key does not have ([I18N006](../reference/diagnostics.md#i18n006)) | The fallback text is used. |

Literals are output without quotes (`{'@'}` becomes `@`), and format specifiers such as `{date:d}` are applied.

## Prefer the typed API when you can

`Language.greeting("Kari")` and `new LocalizedLanguage(culture).greeting("Kari")` are checked by the compiler. `Format` is checked only at run time. Use `Format` only when the key is not known at compile time.
