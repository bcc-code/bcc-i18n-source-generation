# How culture matching and fallback work

Two separate decisions happen when you ask for a translated text:

1. **Which language?** The user's culture is matched to one of the translation files.
2. **Which text?** The key is looked up in that language. If it is not there, the fallback language's text is used.

## Languages are file names

A translation file's language is its file name without `.json`, lowercased. `en.json` is `en`, `pt-BR.json` is `pt-br`. The generator does not check that the name is a real culture; `customer-a.json` is a valid language called `customer-a`, which only a culture or alias with that name would select.

## Matching a culture to a language

`LanguageStrings.ResolveLanguage(culture)` does the matching. `Language`, `LocalizedLanguage`, `LanguageStrings` and `LanguageStringLocalizer` all use it.

It walks the culture and its parents, as .NET defines them, until the invariant culture:

```text
nb-NO → nb → (invariant)
zh-TW → zh-Hant → zh → (invariant)
pt-BR → pt → (invariant)
```

At each step it tries two things, in this order:

1. **A file with the culture's name.** The comparison ignores case.
2. **An alias for the culture's name** whose target has a file.

The first match wins. If no step matches, the result is the fallback language.

### Examples

With the files `en.json`, `no.json` and `pt-br.json`, and fallback `en`:

| Culture | Steps | Result |
| --- | --- | --- |
| `en-US` | `en-US`: no file, no alias. `en`: file. | `en` |
| `nb-NO` | `nb-NO`: nothing. `nb`: no file, built-in alias `nb=no`, `no.json` exists. | `no` |
| `pt-BR` | `pt-BR`: file `pt-br`. | `pt-br` |
| `pt-PT` | `pt-PT`: nothing. `pt`: nothing. | `en` (fallback) |
| `de-DE` | Nothing matches. | `en` (fallback) |

### Why a file wins over an alias

The more specific source of truth is the file. If you add `nb.json` later, Bokmål users get it automatically, even though the built-in alias `nb=no` still exists. Aliases fill gaps; they never hide a file.

### Why aliases are needed at all

.NET culture names and the names people give translation files do not always agree. The common case is Norwegian: many projects use `no.json`, while users' cultures are `nb-NO` (Bokmål) or `nn-NO` (Nynorsk). `nb-NO`'s parent chain is `nb-NO → nb`, and never reaches `no`. The built-in aliases `nb=no` and `nn=no` cover this. The reverse alias `no=nb` covers projects that named the file `nb.json` and users whose culture is `no`.

Use `I18NLanguageAliases` for other cases, such as `pt-pt=pt` or mapping several regional cultures to one file. See [How to map cultures to languages](../how-to/configure-language-aliases.md).

## Fallback per key

After the language is chosen, each key is looked up separately:

```text
key in chosen language? → that text
otherwise               → the fallback language's text
```

So a Norwegian user sees Norwegian for the keys that `no.json` has, and the fallback text for the keys it lacks. The build reports the missing keys as [I18N007](../reference/diagnostics.md#i18n007).

There is one more case where the fallback text is used: when the chosen language's text uses placeholders that the fallback text does not have ([I18N006](../reference/diagnostics.md#i18n006)). The generated method has no value to fill them with, so the language's text is skipped for that key.

There is no fallback chain between non-fallback languages. A `pt-br` text missing a key falls back to the fallback language, not to `pt`.

## Two cultures: language and formatting

.NET has two current cultures:

- `CultureInfo.CurrentUICulture` decides the **language** in `Language` and the one-argument `LanguageStrings` methods.
- `CultureInfo.CurrentCulture` decides how **numbers and dates** in placeholders are formatted in `Language` and `LanguageStrings.Format(key, args)`.

They are usually the same, but they can differ, for example an English UI with Norwegian number formatting. `LocalizedLanguage` and the culture overloads use the one culture you pass for both.

## Why the fallback file defines the API

The fallback file is the only file guaranteed to be used for every user, because every lookup can end there. That makes it the natural source for the list of keys, the member names and the parameters. Other files are checked against it ([I18N006](../reference/diagnostics.md#i18n006), [I18N007](../reference/diagnostics.md#i18n007), [I18N008](../reference/diagnostics.md#i18n008)), but they do not add members.

## Performance

`ResolveLanguage` is generated as a loop over the parent cultures with a chain of `string.Equals(..., OrdinalIgnoreCase)` checks. It does not allocate and has no caches. `Language` calls it on every member access, which is cheap for the small number of files a project has. `LocalizedLanguage` calls it once, in its constructor.
