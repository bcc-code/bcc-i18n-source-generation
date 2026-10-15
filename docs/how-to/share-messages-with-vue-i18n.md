# How to share translation files with vue-i18n

Use this when a Vue frontend with vue-i18n (v9 or later) and a .NET backend read the same JSON files.

## 1. Point both apps at the same files

Keep the files in one folder and add them to the .NET project with a relative path:

```xml
<ItemGroup>
  <AdditionalFiles Include="..\frontend\src\locales\*.json" I18NTranslation="true" />
</ItemGroup>
```

The file names must be the language names in both apps (`en.json`, `no.json`).

## 2. Use the syntax both understand

These constructs mean the same in vue-i18n and in the generated code:

| Need | Write |
| --- | --- |
| A value | `"Hello, {name}!"` |
| Positional values | `"{0} of {1}"` |
| Plural forms | `"no files \| one file \| {count} files"` |
| Reuse another message | `"Press @:menu.save to continue."` |
| Reuse with case change | `"@.upper:menu.save"`, `"@.lower:menu.save"`, `"@.capitalize:menu.save"` |
| A special character | `"info{'@'}example.com"`, `"{'{'}"`, `"{'|'}"` |

Example `en.json`:

```json
{
  "menu": { "save": "save" },
  "hint": "Press @.capitalize:menu.save to keep your changes.",
  "contact": "Write to post{'@'}example.com",
  "files": "no files | one file | {count} files"
}
```

In C#:

```csharp
Language.hint;      // "Press Save to keep your changes."
Language.contact;   // "Write to post@example.com"
Language.files(2);  // "2 files"
```

## 3. Send keys or stored texts to the frontend

- `LanguageKeys.menu.save` is the key (`"menu.save"`). The frontend translates it with `t('menu.save')`.
- `LanguageStrings.GetString("contact")` returns `Write to post{'@'}example.com`, with links resolved but literals kept, so the frontend can interpret it with the same syntax.
- `LanguageStrings.GetAllStrings(culture)` returns all key/value pairs of a language, for example to serve a locale file from an API.

## 4. Avoid the parts that only one side supports

- **.NET only:** format specifiers like `{date:d}`. vue-i18n shows them as text. Format in each app instead.
- **vue-i18n only:** custom pluralization rules (`pluralizationRules`), custom link modifiers, HTML messages and component interpolation. The generator does not apply custom rules or modifiers, and does not escape or render HTML.
- **Linking a plural message** (`@:files` where `files` contains `|`) is reported as [I18N010](../reference/diagnostics.md#i18n010) and the link is kept as written.
- **Braces that are not placeholders** (`"Use { and }"`) are plain text in .NET but may be a syntax error in vue-i18n. Write `{'{'}` and `{'}'}`.

See [Relation to vue-i18n](../explanation/vue-i18n.md) for background, and the [message syntax reference](../reference/message-syntax.md) for exact rules.
