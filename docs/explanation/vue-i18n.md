# Relation to vue-i18n

## Why vue-i18n compatibility

A common setup is a .NET backend with a Vue frontend. Both need the same texts: the backend for emails, validation messages and generated documents, the frontend for the UI. Keeping one set of JSON files for both avoids translating the same text twice and keeps wording consistent.

vue-i18n's file format is plain JSON with nested objects, one file per language, which maps well to C#: objects become nested classes and strings become members. The generator therefore reads the same files and understands the same message syntax where it can.

## What is shared

| Feature | vue-i18n | Generator |
| --- | --- | --- |
| Nested JSON objects, dot-separated keys | Yes | Yes |
| Named placeholders `{name}` | Yes | Yes, as typed parameters |
| List placeholders `{0}` | Yes | Yes, as parameters `_0`, `_1` |
| Plural forms with <code>&#124;</code>, default rules | Yes | Yes, same rules |
| Linked messages `@:key`, `@:{'key'}` | Yes | Yes, resolved at build time |
| Modifiers `lower`, `upper`, `capitalize` | Yes | Yes |
| Literal interpolation `{'text'}` with escapes | Yes | Yes |

## Where they differ

**Linked messages are resolved at build time.** vue-i18n resolves `@:key` each time the message is rendered. The generator replaces links with the target text when it generates code, so `Language`, `LocalizedLanguage` and `LanguageStrings` all contain the final text. A broken link is reported as a warning ([I18N010](../reference/diagnostics.md#i18n010)) instead of showing up at run time.

**Linking a plural message is not supported.** The generator reports it as I18N010 and keeps the link text, because the linked text would need its own count.

**Format specifiers are .NET-only.** `{date:d}` formats a value with .NET format strings. vue-i18n has no such syntax and shows the text as written.

**Custom rules are not supported.** vue-i18n lets you add `pluralizationRules` per locale and custom link modifiers in JavaScript. These live in code, not in the JSON files, so the generator cannot see them. It uses the default rules and the three built-in modifiers.

**No HTML handling.** vue-i18n has options for HTML messages and component interpolation. The generator outputs text as is.

**Unknown braces are plain text.** A `{` that does not start a placeholder or literal is plain text in .NET. vue-i18n's compiler may reject it.

## Literals in `LanguageStrings`

`Language` and `LocalizedLanguage` output the content of a literal: `{'@'}` becomes `@`. `LanguageStrings.GetString` returns the stored text with links resolved but literals kept, so `info{'@'}example.com` stays as written. The reason is that `LanguageStrings` is often used to pass texts on to a frontend that applies the same syntax. If the literal were removed, a frontend would see a bare `@` and might read it as the start of a link.

## Keys for the frontend

`LanguageKeys` gives compile-time checked keys. A backend can return `LanguageKeys.validation.required` in an API error, and the frontend translates it with vue-i18n. A renamed key then breaks the backend build instead of showing a raw key in the UI.
