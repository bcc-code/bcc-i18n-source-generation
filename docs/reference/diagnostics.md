# Reference: diagnostics

The generator reports these diagnostics. All have the category `BccCode.I18N.SourceGen` and are enabled by default.

Diagnostics about a file are reported at the start of that file (line 1, column 1). I18N004 and I18N009 have no file location.

To change a severity, see [Change the severity of a diagnostic](../how-to/change-diagnostic-severity.md).

| ID | Default severity | Title |
| --- | --- | --- |
| [I18N001](#i18n001) | Error | Invalid translation file |
| [I18N002](#i18n002) | Error | Translation file must use an object root |
| [I18N003](#i18n003) | Error | Unsupported translation value |
| [I18N004](#i18n004) | Error | Fallback language file missing |
| [I18N005](#i18n005) | Error | Duplicate translation language |
| [I18N006](#i18n006) | Warning | Translation placeholders do not match fallback language |
| [I18N007](#i18n007) | Warning | Translation key missing from language |
| [I18N008](#i18n008) | Warning | Translation key not in fallback language |
| [I18N009](#i18n009) | Warning | Language alias target missing |
| [I18N010](#i18n010) | Warning | Invalid linked message |

## I18N001

**Invalid translation file** — Error

**Cause:** A translation file could not be read, or is not valid JSON. The message includes the parser's description of the problem.

**Effect:** The file is skipped. If it is the fallback file, I18N004 follows and nothing is generated.

**Fix:** Correct the JSON syntax. Check for trailing commas, missing quotes or brackets, and that the file is saved as UTF-8.

## I18N002

**Translation file must use an object root** — Error

**Cause:** The JSON is valid, but the root value is not an object (for example an array or a string).

**Effect:** The file is skipped.

**Fix:** Wrap the content in `{ ... }`.

## I18N003

**Unsupported translation value** — Error

**Cause:** A value in the file is not a string or an object, for example a number, `true`, `null` or an array. The message names the key.

**Effect:** That key is skipped. The rest of the file is used.

**Fix:** Change the value to a string (`"5"` instead of `5`), or to a nested object. For lists, use separate keys.

## I18N004

**Fallback language file missing** — Error

**Cause:** No translation file has the name of the fallback language. The message names the expected language.

**Effect:** No types are generated, so code that uses `Language`, `LanguageKeys` or `LanguageStrings` also fails to compile.

**Fix:** Do one of the following:

- Add a file for the fallback language (for example `no.json`).
- Set [`FallbackLanguage`](msbuild.md#properties) to the name of an existing file. Note that the package default is `no`.
- Check that the file is in `AdditionalFiles` and, if any file is marked, that it has `I18NTranslation="true"`.

## I18N005

**Duplicate translation language** — Error

**Cause:** Two or more translation files have the same language, for example `Translations/en.json` and `Other/en.json`, or `en.json` and `EN.json` in different folders. File names are compared case-insensitively, and the folder is not part of the language.

**Effect:** Reported on each of the files. The file whose path sorts first (ordinal) is used.

**Fix:** Keep one file per language. Remove the other file from `AdditionalFiles`, or stop marking it with `I18NTranslation`.

## I18N006

**Translation placeholders do not match fallback language** — Warning

**Cause:** A language's text for a key uses a placeholder that the fallback text for that key does not have. Example: the fallback text is `"Hello {name}"` and the Norwegian text is `"Hei {navn}"`.

**Effect:** For that language and key, the fallback text is used instead, in `Language`, `LocalizedLanguage` and `LanguageStrings.Format`. For a plural key, the whole language text is replaced, not single forms. `LanguageStrings.GetString` still returns the language's own stored text.

**Fix:** Use the same placeholder names as the fallback text. Using fewer placeholders, a different order or different format specifiers is allowed.

## I18N007

**Translation key missing from language** — Warning

**Cause:** A language file does not have one or more keys that exist in the fallback file. One diagnostic per file lists up to 10 keys and then `and N more`.

**Effect:** None at run time beyond the warning: those keys use the fallback text for that language.

**Fix:** Add the keys to the language file. If missing keys are expected (for example while translations are in progress), lower the severity or suppress it.

## I18N008

**Translation key not in fallback language** — Warning

**Cause:** A language file has one or more keys that do not exist in the fallback file. One diagnostic per file lists up to 10 keys and then `and N more`.

**Effect:** Those keys are not generated in `Language`, `LocalizedLanguage` or `LanguageKeys`, and are not in `LanguageStrings.Keys`. `LanguageStrings.GetString` and `GetAllStrings` still return them for that language.

**Fix:** Add the keys to the fallback file, or remove them from the language file (for example after renaming a key).

## I18N009

**Language alias target missing** — Warning

**Cause:** One of:

- An entry in [`I18NLanguageAliases`](msbuild.md#i18nlanguagealiases-format) is malformed: it has no `=`, or an empty side (`nb`, `=no`, `nb=`).
- An entry points to a language that has no translation file (`nb=no` without `no.json`).

Built-in aliases never cause this warning.

**Effect:** The entry is ignored.

**Fix:** Use the `source=target` format, separate entries with `;`, and point each alias to an existing file language.

## I18N010

**Invalid linked message** — Warning

**Cause:** A linked message (`@:key`) in a text:

- points to a key that exists neither in the same language nor in the fallback language,
- is part of a cycle (`a` links `b`, and `b` links `a`), or
- points to a plural message (a text containing `|`).

Reported once per language and key, for the key whose text is being resolved.

**Effect:** The link text is kept as written in the output.

**Fix:** Correct the key name, break the cycle, or copy the needed plural form into a separate non-plural key and link that.
