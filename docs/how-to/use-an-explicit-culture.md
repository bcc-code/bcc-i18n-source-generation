# How to translate for a specific culture

`Language` reads `CultureInfo.CurrentUICulture`. In background jobs, when sending an email in the recipient's language, or in tests, pass the culture instead of changing the thread's culture.

## Typed access: `LocalizedLanguage`

```csharp
var norwegian = new LocalizedLanguage(new CultureInfo("nb-NO"));

string save = norwegian.menu.save;          // "Lagre"
string greeting = norwegian.greeting("Kari"); // "Hei, Kari!"
string files = norwegian.files(3);         // "3 filer"
```

`LocalizedLanguage` has the same members as `Language`, as instance members. The constructor resolves the language once. Numbers and dates in placeholders are formatted with the same culture.

It is a small `readonly struct`, so creating one per request or per message is cheap. You can store it in a field or pass it to methods.

## Lookup by key: `LanguageStrings`

```csharp
var culture = new CultureInfo("nb-NO");

string text = LanguageStrings.GetString("menu.save", culture);         // "Lagre"
string? maybe = LanguageStrings.GetStringOrNull("menu.unknown", culture); // null
```

These return the stored text without formatting. To fill in placeholders for a culture, use `LanguageStrings.Format(key, culture, args)` (see [Format a message by key](format-a-message-by-key.md)).

## Which culture is used for what

| API | Language selection | Number and date formatting |
| --- | --- | --- |
| `Language.*` | `CultureInfo.CurrentUICulture` | `CultureInfo.CurrentCulture` |
| `new LocalizedLanguage(culture).*` | `culture` | `culture` |
| `LanguageStrings.GetString(key)` | `CultureInfo.CurrentUICulture` | None (stored text) |
| `LanguageStrings.GetString(key, culture)` | `culture` | None (stored text) |
| `LanguageStrings.Format(key, args)` | `CultureInfo.CurrentUICulture` | `CultureInfo.CurrentCulture` |
| `LanguageStrings.Format(key, culture, args)` | `culture` | `culture` |

All of them fall back to the fallback language per key.

## Watch out for `default`

`default(LocalizedLanguage)` (for example an uninitialized field) uses the fallback language and invariant formatting. Always create it with the constructor.

## Example: an email in the user's language

```csharp
static string BuildSubject(string userName, CultureInfo userCulture)
{
    var text = new LocalizedLanguage(userCulture);
    return text.greeting(userName);
}
```
