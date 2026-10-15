# Plural selection rules

## The rules

A plural message has forms separated by `|`. The generator chooses a form from the count with the same default rules as vue-i18n. The count's absolute value is used, and the rule depends only on how many forms the text has:

| Forms | 0 | 1 | 2 | 3 | 5 |
| --- | --- | --- | --- | --- | --- |
| 2 (`car \| cars`) | cars | car | cars | cars | cars |
| 3 (`none \| one \| many`) | none | one | many | many | many |
| 4 (`a \| b \| c \| d`) | a | b | c | d | d |

- **Two forms:** singular and plural. Only 1 is singular. 0 is plural, as in English ("0 cars").
- **Three forms:** zero, one and many.
- **Four or more forms:** the count is the index of the form. Counts past the end use the last form.

## Why these rules

The rules match vue-i18n's defaults, so a JSON file shared between a Vue frontend and a .NET backend shows the same form for the same count on both sides. See [Relation to vue-i18n](vue-i18n.md).

They are not the CLDR plural categories (`zero`, `one`, `two`, `few`, `many`, `other`) that some languages need, for example Polish or Arabic. The position of a form decides when it is used, not a category name. For languages with more complex rules, use four or more forms to list explicit counts, or choose between separate keys in your own code.

## Each language uses its own number of forms

The rule is chosen per language, from that language's text. English can use three forms and Norwegian two for the same key:

```text
en: no files | one file | {count} files
no: {count} fil | {count} filer
```

For a count of 0, English shows `no files` and Norwegian shows `0 filer`.

A language with one form (no `|`) shows that form for every count. That is how a key can be plural in some languages but not in others.

## The absolute value of the count

`files(-1)` selects the singular form, and `files(-3)` the plural. The placeholder still shows the original value, `-3 files`. The count is widened to `long` before taking the absolute value, so `int.MinValue` is handled without an overflow.

## `LanguageStrings.Format`

`Format` selects forms at run time from the first argument. Unlike the generated members, which take an `int`, it accepts any numeric type, so fractions are possible:

- Two forms: only exactly 1 selects the first form. `1.5` selects the second.
- Three forms: only exactly 0 and exactly 1 select the first and second forms.
- Four or more forms: an integral count is the index; a fractional count, or one past the end, uses the last form.
- If the first argument is missing or not a number, the count is 1.

## How the generated code selects a form

The generated method switches on the pair of count and language, so the choice is a single `switch` expression:

```csharp
(global::System.Math.Abs((long)count), __language) switch
{
    (1, "no") => "bil",
    (_, "no") => "biler",
    (1, _) => "car",
    (_, _) => "cars",
};
```

The `_` language arms belong to the fallback language and also handle languages without their own text for the key.
