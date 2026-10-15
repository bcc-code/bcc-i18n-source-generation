# Why the generated code is shaped for hot reload

## The constraint

.NET hot reload applies an edit by replacing method bodies in the running process. It cannot run a static field initializer or static constructor again. A static field keeps the value it got when the type was first used, even after the code that computed it changes.

A natural way to generate translations would be a static dictionary:

```csharp
private static readonly Dictionary<string, string> English = new() { ["menu.save"] = "Save", ... };
```

With that shape, an edit to a JSON file produces a new initializer, which hot reload cannot apply. `dotnet watch` then has to restart the app on every translation edit. Earlier versions of the generator had this problem with `LanguageStrings`.

## The shape the generator uses instead

All translation data lives in method bodies, as `switch` expressions:

```csharp
private static string? GetStringOrNull0(string key) =>
    key switch
    {
        "menu.save" => "Save",
        "menu.cancel" => "Cancel",
        _ => null,
    };
```

- `Language` and `LocalizedLanguage` members call methods in `LanguageTranslations`, one per key, each with a `switch` over the language.
- `LanguageStrings` uses one lookup method per language, each a `switch` over the key.
- `ResolveLanguage` is a loop with `string.Equals` checks, with the aliases written into the method.
- `Languages`, `Keys` and `GetAllStrings` build a new array or list on every call instead of returning a cached static one.
- The run-time formatter behind `LanguageStrings.Format` is a set of static methods without static fields.

When a text changes, only these bodies change, and hot reload can replace them.

## The cost

- The listing members allocate on every call. They are meant for occasional use (export, a locale endpoint, a language picker). Cache the result yourself if you need it often.
- `Language` resolves the language on every member access. Resolution is a few string comparisons without allocation.

The C# compiler turns a `switch` over many string constants into a hash or length based lookup rather than a sequence of comparisons, so lookups by key stay fast with many keys.

## What still needs a restart

Adding or removing keys, or changing placeholders, changes the set of members and their signatures. Whether such edits can be applied depends on the hot reload capabilities of the runtime and IDE, not on the generator. See [How to edit translations while the app runs](../how-to/use-hot-reload.md).
