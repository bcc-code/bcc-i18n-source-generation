# Why the generated code is shaped for hot reload

## The constraint

.NET hot reload applies an edit by replacing method bodies in the running process. It cannot run a static field initializer or static constructor again. A static field keeps the value it got when the type was first used, even after the code that computed it changes.

A natural way to generate translations would be a static dictionary that is filled by an initializer:

```csharp
private static readonly Dictionary<string, string> English = new() { ["menu.save"] = "Save", ... };
```

With that shape, an edit to a JSON file produces a new initializer, which hot reload cannot apply. `dotnet watch` then has to restart the app on every translation edit. Earlier versions of the generator had this problem with `LanguageStrings`.

## The shape the generator uses instead

The translations are data arrays that are built by methods, and the arrays are cached in static fields that have no initializer. The runtime clears the caches after a hot reload edit.

All key paths of all translation files, sorted ordinally, form the key index. The position of a key in that list is its index. `LanguageStrings` has these methods, which contain only data:

```csharp
private static string[] KeysData() =>
    new string[]
    {
        "menu.cancel",
        "menu.save",
    };

// "en"
private static string?[] StringsData0() =>
    new string?[]
    {
        "Cancel",
        "Save",
    };
```

There is one `StringsData` method per translation file, with one entry per key index (`null` when the file lacks the key), and `ParameterNamesData` with the placeholder names per key. Each array is cached on first use:

```csharp
private static string?[] Strings0() => s_strings0 ??= StringsData0();
```

The cache is a static field without initializer and the class has no static constructor. Two threads that start at the same time can both build an array. That is harmless, because the result is the same.

- `Language`, `LocalizedLanguage` and the group structs call `LanguageStrings.Translate` with the index of the key. The key is written in a comment next to the index.
- `LanguageStrings` finds the index of a key with a dictionary that is built from `KeysData` on first use, and reads the text from the array of the language.
- `ResolveLanguage` is a loop with `string.Equals` checks, with the aliases written into the method.
- `Languages`, `Keys` and `GetAllStrings` build a new array or list on every call.
- The run-time formatter behind `LanguageStrings.Format` and `Translate` is a set of static methods without static fields.

### Clearing the caches

When a text changes, only the data methods change, and hot reload can replace them. Methods that were already called keep their cached array, though. So the generator also emits:

```csharp
[assembly: global::System.Reflection.Metadata.MetadataUpdateHandler(typeof(global::MyApp.Localization.LanguageStrings))]

internal static void ClearCache(global::System.Type[]? updatedTypes)
```

After an update, the runtime calls `ClearCache` on the handler type. It sets every cache field to `null`, so the next lookup builds the arrays again from the edited methods.

The attribute is only emitted when the project compiles against `System.Reflection.Metadata.MetadataUpdateHandlerAttribute` (.NET 6 and later). The generator checks this against the compilation, separately from the translation files, so editing a translation does not run the check again. Without the type, for example on .NET Framework or .NET Standard, `ClearCache` is still emitted, but nothing calls it.

### Limitation

The caches are only cleared by tools and runtimes that call `MetadataUpdateHandler` handlers. `dotnet watch` and the IDE hot reload on .NET 6 and later do. If a tool applies the edit without calling the handler, the app keeps returning the old texts until it restarts. A project that targets a runtime without the attribute type has the same limitation.

## The cost

- The listing members allocate on every call. They are meant for occasional use (export, a locale endpoint, a language picker). Cache the result yourself if you need it often.
- `Language` resolves the language on every member access. Resolution is a few string comparisons without allocation.
- The arrays are built on first use, which takes time proportional to the number of keys. The first lookup after start or after a hot reload edit pays for it.
- The arrays and the key dictionary stay in memory after the first use.

Earlier versions emitted one `switch` over the keys per language and one `switch` per key over the languages. The compile time of a `switch` over string constants grows with the square of its number of arms, so large translation sets needed a hash-bucket workaround. An array initializer is compiled in time proportional to its length, so the generated code needs no workaround for large sets.

## What still needs a restart

Adding or removing keys, or changing placeholders, changes the set of members and their signatures. Whether such edits can be applied depends on the hot reload capabilities of the runtime and IDE, not on the generator. See [How to edit translations while the app runs](../how-to/use-hot-reload.md).
