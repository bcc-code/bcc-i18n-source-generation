# How to pluralize a message

Separate the forms with `|`:

```json
{
  "car": "car | cars",
  "files": "no files | one file | {count} files"
}
```

The generated member takes the count as an `int`:

```csharp
Language.car(1);    // "car"
Language.car(2);    // "cars"
Language.files(0);  // "no files"
Language.files(1);  // "one file"
Language.files(7);  // "7 files"
```

## Choose the number of forms

| Forms | Use for | Selection |
| --- | --- | --- |
| 2 | singular / plural | 1 → first, anything else → second |
| 3 | zero / one / many | 0 → first, 1 → second, anything else → third |
| 4 or more | explicit counts | count n → form n, larger counts → last form |

Negative counts use their absolute value to select the form. Each language can use a different number of forms for the same key:

`en.json`:

```json
{ "files": "no files | one file | {count} files" }
```

`no.json`:

```json
{ "files": "{count} fil | {count} filer" }
```

Here `Language.files(0)` is `no files` in English and `0 filer` in Norwegian.

## Name the count

The first placeholder in the fallback text is the count, whatever its name:

```json
{ "banana": "no bananas | {n} banana | {n} bananas" }
```

This generates `banana(int n)`. Without a placeholder, the parameter is named `count`. Other placeholders follow the count:

```json
{ "inFolder": "{count} file in {folder} | {count} files in {folder}" }
```

This generates `inFolder(int count, object folder)`.

## Pluralize in some languages only

A key is plural when any language uses `|`. A language with a single form returns it for every count. If the fallback text has no `|`, the count parameter is named `count`, so use `{count}` in the plural languages:

`en.json` (fallback):

```json
{ "results": "Results: {count}" }
```

`no.json`:

```json
{ "results": "Ett resultat | {count} resultater" }
```

## Write a literal pipe

A `|` always separates forms. To show the character, write `{'|'}`:

```json
{ "path": "Home {'|'} Settings" }
```

## Pluralize by key at run time

`LanguageStrings.Format("files", 3)` selects the form the same way, with the count as the first argument. See [Format a message by key](format-a-message-by-key.md).

[Plural selection](../explanation/plural-selection.md) explains where these rules come from.
