# Reference: message syntax

A translation file is a JSON object. Values are strings (messages) or nested objects (groups). Any other value type is reported as [I18N003](diagnostics.md#i18n003).

Messages use a subset of the [vue-i18n](https://vue-i18n.intlify.dev/guide/essentials/syntax) message syntax, plus .NET format specifiers. This page lists every construct the generator recognizes. Text that does not match a construct is plain text.

## Summary

| Syntax | Name | Example |
| --- | --- | --- |
| `{name}` | Named placeholder | `"Hello, {name}!"` |
| `{name:format}` | Placeholder with format specifier | `"Due {date:d}"` |
| `{0}` | List placeholder | `"{0} is required."` |
| `a \| b \| c` | Plural forms | `"no files \| one file \| {count} files"` |
| `@:key` | Linked message | `"Press @:menu.save."` |
| `@:{'key'}` | Linked message, quoted key | `"@:{'menu.save'}"` |
| `@.lower:key`, `@.upper:key`, `@.capitalize:key` | Linked message with modifier | `"@.upper:menu.save"` |
| `{'text'}` | Literal | `"info{'@'}example.com"` |

## Placeholders

```text
{ name }
{ name : format }
```

- `name` is one or more letters, digits or `_` (the regular expression class `\w`). Whitespace around the name is allowed.
- `format` is everything after the first `:` up to `}`. It may not contain `{` or `}`. It is passed to `string.Format` unchanged, so it is any .NET format string valid for the argument type (`d`, `N2`, `yyyy-MM-dd`, `0.0`, ...).
- The same name may appear several times, with or without different formats. It is one parameter.
- Names that are only digits (`{0}`, `{1}`) are list placeholders. They become parameters `_0`, `_1`, in order of first appearance in the fallback text (not by number).
- A `{` that does not start a placeholder or literal is plain text.

The parameter list of a key comes from the fallback text. Another language may use fewer placeholders, or the same placeholders in another order or with other formats. A language that uses a placeholder the fallback text does not have is reported as [I18N006](diagnostics.md#i18n006), and the fallback text is used for that language.

Format specifiers are a .NET addition. vue-i18n does not support them. See [Format numbers and dates](../how-to/format-numbers-and-dates.md).

## Plural forms

```text
form0 | form1 | form2 | ...
```

- A `|` outside of placeholders, literals and links separates forms. Whitespace around each form is trimmed.
- A key is plural when the text has at least one such `|` in any language.
- The member for a plural key takes the count as its first parameter, of type `int`. The first placeholder in the fallback text is the count; if there is none, the count is named `count`.
- Each language selects a form with the rule for its own number of forms:

| Forms | Selection (by absolute value of the count) |
| --- | --- |
| 1 | Always the only form |
| 2 | 1 → form 0; anything else → form 1 |
| 3 | 0 → form 0; 1 → form 1; anything else → form 2 |
| 4 or more | n → form n; counts past the last form → the last form |

Example:

```json
{ "files": "no files | one file | {count} files" }
```

`files(0)` is `no files`, `files(1)` is `one file`, `files(-3)` is `-3 files`. The value shown is the count itself, not its absolute value.

To write a literal `|` in a message, use `{'|'}`.

See [Plural selection](../explanation/plural-selection.md) for the reasoning and for `LanguageStrings.Format`.

## Linked messages

```text
@:key
@:{'key'}
@.modifier:key
@.modifier:{'key'}
```

- A bare `key` consists of letters, digits, `_`, `-`, `$` and `.`. Trailing `.` characters are not part of the key, so `"Press @:menu.save."` links `menu.save` and keeps the period.
- A quoted key `{'...'}` may contain any character except `'`.
- `modifier` is `lower`, `upper` or `capitalize`. Any other modifier name is not recognized, and the text stays as written.
- `@` that is not followed by `:` or `.modifier:` is plain text, so `post@example.com` is not a link.

Links are resolved when the code is generated:

1. The target key is looked up in the same language, then in the fallback language.
2. Links inside the target are resolved too.
3. Placeholders in the target become part of the message. They add parameters to the key if the fallback text links them.
4. The modifier is applied to the target's text. Placeholders, links and format specifiers inside it are not changed.
   - `lower` and `upper` use invariant casing rules.
   - `capitalize` uppercases the first character of the target when the target starts with text (or a literal). Text after a leading placeholder is not changed.

A link is kept as written, and [I18N010](diagnostics.md#i18n010) is reported, when the target key does not exist in the language or the fallback language, when the link is part of a cycle, or when the target is a plural message.

`LanguageKeys` is not affected by links. `LanguageStrings` returns the text with links resolved.

## Literals

```text
{'text'}
```

- The content is output as written, without interpreting placeholders, `|` or `@`.
- Escapes inside a literal:

| Escape | Result |
| --- | --- |
| `\'` | `'` |
| `\\` | `\` |
| `\uXXXX` | The character with that code point (exactly 4 hex digits) |
| `\UXXXXXX` | The character with that code point (exactly 6 hex digits) |

- A literal with another escape, a surrogate code point (U+D800–U+DFFF), a code point above U+10FFFF, or no closing `'}` is not a literal. It is plain text.
- In JSON, a backslash must itself be escaped: the JSON string `"{'It\\'s'}"` is the message `{'It\'s'}`, which outputs `It's`.

`Language` and `LocalizedLanguage` output the literal's content. `LanguageStrings.GetString` returns the literal as written (`{'@'}`), so a frontend using the same syntax can interpret it. `LanguageStrings.Format` and `LanguageStringLocalizer` with arguments output the content.

## Writing special characters

| To output | Write |
| --- | --- |
| `{` | `{'{'}` |
| `}` | `{'}'}` |
| `\|` | `{'\|'}` |
| `@` before `:` or `.` | `{'@'}` |

## Not supported

- Custom plural rules (vue-i18n `pluralizationRules`).
- Custom link modifiers.
- HTML in messages. Text is output as is, without escaping or rendering.
- Named plural forms or ICU MessageFormat (`{count, plural, ...}`).
- Arrays and non-string values in translation files.
