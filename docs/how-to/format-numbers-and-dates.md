# How to format numbers and dates in messages

Add a .NET format specifier after the placeholder name, separated by a colon:

```json
{
  "due": "Due {date:d}",
  "price": "{amount:N2} kr",
  "updated": "Updated {when:yyyy-MM-dd HH:mm}"
}
```

The generated members take the value as `object` and format it with `string.Format`:

```csharp
CultureInfo.CurrentCulture = new CultureInfo("en-US");
Language.due(new DateTime(2024, 1, 31));  // "Due 1/31/2024"
Language.price(1234.5m);                   // "1,234.50 kr"

CultureInfo.CurrentCulture = new CultureInfo("nb-NO");
Language.due(new DateTime(2024, 1, 31));  // "Due 31.01.2024"
```

Any format string that the argument's type accepts works: standard ones like `d`, `D`, `N0`, `P1`, `C`, and custom ones like `yyyy-MM-dd` or `0.00`. The format may not contain `{` or `}`.

## Which culture formats the value

- `Language` uses `CultureInfo.CurrentCulture`. This is separate from `CurrentUICulture`, which selects the language.
- `LocalizedLanguage` and `LanguageStrings.Format(key, culture, ...)` use the culture you pass.
- `default(LocalizedLanguage)` uses the invariant culture.

In ASP.NET Core with `UseRequestLocalization`, both cultures are set per request.

## Different formats per language

Each language may use its own format for the same placeholder name:

`en.json`:

```json
{ "due": "Due {date:MM/dd}" }
```

`no.json`:

```json
{ "due": "Forfaller {date:dd.MM.}" }
```

Placeholder checks ([I18N006](../reference/diagnostics.md#i18n006)) compare names only.

## Plural counts

The count of a plural message can have a format too:

```json
{ "items": "{count:N0} item | {count:N0} items" }
```

`Language.items(1500)` returns `1,500 items` with `en-US` formatting.

## If you share the files with vue-i18n

Format specifiers are a .NET feature. vue-i18n shows `{date:d}` as text. For keys used by both, format the value in each app and pass a plain `{date}` placeholder, or use separate keys.

`LanguageStrings.GetString` returns the stored text with the specifier (`Due {date:d}`).
